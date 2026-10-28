using System.Collections.Immutable;
using System.Text.Json.Nodes;
using Pockets.Core.Models;
using Pockets.Core.Rendering;
using Pockets.Core.Sim;
using Pockets.Realization;

namespace Pockets.JourneyRunner;

/// <summary>A golden render capture at a flagged checkpoint (Slice 8): its label + the render probe.</summary>
public record GoldenCapture(string Label, string RenderProbe);

/// <summary>Outcome of running one script against one driver.</summary>
public record RunResult(
    string Driver,
    IReadOnlyList<string> Checkpoints,
    IReadOnlyList<string> Failures,
    IReadOnlyList<string> Notes,
    IReadOnlyList<GoldenCapture> Goldens,
    IReadOnlyList<string>? EventLines = null,
    IReadOnlyList<string>? PoseLines = null)
{
    /// <summary>The event golden lines (<see cref="EventGolden.Lines"/> per journey step; start = -1), in order.</summary>
    public IReadOnlyList<string> EventLines { get; init; } = EventLines ?? Array.Empty<string>();

    /// <summary>The pose golden lines (<see cref="PoseGolden.Lines"/> per journey step; Slice 2.4), in order.</summary>
    public IReadOnlyList<string> PoseLines { get; init; } = PoseLines ?? Array.Empty<string>();

    public bool Passed => Failures.Count == 0;
}

/// <summary>
/// The observe → act → verify loop (folding agent_play_godot.py's pattern into one place):
/// for each script step it performs the action, observes the driver, runs the invariant pack
/// (stack validity, progressability, conservation), evaluates the step's assertions, and emits
/// one canonical checkpoint line. The checkpoint stream is identical across drivers by
/// construction (same shared serializer over the same Core state), so `make parity` diffs the
/// streams to prove the builds are in sync.
/// </summary>
public static class JourneyRunnerCore
{
    /// <param name="screenshotDir">
    /// When non-null and the driver implements <see cref="IScreenshotDriver"/> (real Godot only),
    /// each golden-flagged checkpoint saves a <c>&lt;label&gt;.png</c> here. Ignored otherwise.
    /// </param>
    /// <param name="replayFrom">
    /// When non-null (<c>--replay-check</c>), a factory for a fresh session of the same profile; for an
    /// in-process (<see cref="ISessionDriver"/>) driver the run's intent log is replayed on it after the loop
    /// and must reproduce every step's view-model and event stream (<see cref="ReplayCheck"/>).
    /// </param>
    public static RunResult Run(IJourneyDriver driver, JourneyScript script, string? screenshotDir = null,
        Func<GameSession>? replayFrom = null)
    {
        var checkpoints = new List<string>();
        var failures = new List<string>();
        var notes = new List<string>();
        var goldens = new List<GoldenCapture>();
        var eventLines = new List<string>();
        var poseLines = new List<string>();
        var poseCommits = new List<PoseCommit>();
        var posesMissing = false;
        var boundaries = new List<ReplayBoundary>();
        var sessionDriver = driver as ISessionDriver;
        var completed = true;

        var screenshotter = driver as IScreenshotDriver;
        if (screenshotDir is not null)
        {
            if (screenshotter is null)
                notes.Add($"screenshot capture requested but {driver.Name} has no viewport — skipped " +
                          "(only the live godot driver captures screenshots; see drift report).");
            else
                Directory.CreateDirectory(screenshotDir);
        }

        // Baseline observation before any step drives conservation comparisons.
        var start = driver.Observe();
        var prevViewModel = start.ViewModel.ToJsonString();
        var prevCensus = start.FullState is { } s0 ? InvariantChecker.Census(s0) : null;
        Emit(checkpoints, -1, "start", start.ViewModel);
        eventLines.AddRange(EventGolden.Lines(-1, start.Events ?? Array.Empty<string>()));
        RecordPoses(-1, start);
        if (replayFrom is not null && sessionDriver is not null)
            boundaries.Add(Boundary(-1, sessionDriver, start));
        VerifyStructural(driver.Name, -1, "start", start, failures, notes);

        for (int i = 0; i < script.Steps.Count; i++)
        {
            var step = script.Steps[i];
            var label = step.Checkpoint ?? $"step-{i}";

            try
            {
                Act(driver, step);
            }
            catch (Exception ex)
            {
                failures.Add($"[{i} {label}] action threw: {ex.Message}");
                completed = false;
                break; // a thrown action means the journey can't continue meaningfully
            }

            var obs = driver.Observe();

            // --- Invariant pack ---
            VerifyStructural(driver.Name, i, label, obs, failures, notes);
            var viewModel = obs.ViewModel.ToJsonString();
            VerifyEvents(i, label, obs, viewModel != prevViewModel, failures);
            prevViewModel = viewModel;

            // Conservation (requires full state; view-model-only drivers flag it as limited).
            if (obs.FullState is { } state)
            {
                var census = InvariantChecker.Census(state);
                if (prevCensus is not null)
                {
                    if (step.ExpectDelta is { } expectedDelta)
                    {
                        // Sanctioned removal/transform: the census must change by EXACTLY this map — no
                        // more appeared or vanished (accounts for e.g. a consumed recipe card precisely).
                        var deltaMismatch = CensusDeltaMismatch(prevCensus, census, expectedDelta);
                        if (deltaMismatch is not null)
                            failures.Add($"[{i} {label}] expected-delta mismatch: {deltaMismatch}");
                    }
                    else
                    {
                        var delta = InvariantChecker.CensusDelta(prevCensus, census);
                        if (delta.Count > 0)
                        {
                            if (step.Conserves)
                                failures.Add($"[{i} {label}] conservation breach: {string.Join("; ", delta)}");
                            else
                                notes.Add($"[{i} {label}] declared transform: {string.Join("; ", delta)}");
                        }
                    }
                }
                prevCensus = census;
            }
            else if (step.Conserves)
            {
                notes.Add($"[{i} {label}] conservation not checked — {driver.Name} exposes only the " +
                          "active-bag view-model over its transport (see drift report).");
            }

            // --- Step assertions ---
            if (step.AssertViewModel is { } expected && !JsonContains(obs.ViewModel, expected, out var mismatch))
                failures.Add($"[{i} {label}] assert-viewmodel: {mismatch}");

            if (step.AssertRender is { } probe)
            {
                if (obs.RenderProbe is null)
                    notes.Add($"[{i} {label}] assert-render skipped — {driver.Name} has no render probe here.");
                else if (!obs.RenderProbe.Contains(probe, StringComparison.Ordinal))
                    failures.Add($"[{i} {label}] assert-render: expected buffer to contain \"{probe}\"");
            }

            Emit(checkpoints, i, label, obs.ViewModel);
            eventLines.AddRange(EventGolden.Lines(i, obs.Events ?? Array.Empty<string>()));
            RecordPoses(i, obs);
            if (replayFrom is not null && sessionDriver is not null)
                boundaries.Add(Boundary(i, sessionDriver, obs));

            // --- Golden capture (Slice 8) — one artifact per flagged ledger-row materialization. ---
            if (step.Golden)
            {
                if (obs.RenderProbe is { } buffer)
                    goldens.Add(new GoldenCapture(label, buffer));
                else
                    notes.Add($"[{i} {label}] golden buffer skipped — {driver.Name} has no render probe.");

                if (screenshotter is not null && screenshotDir is not null)
                {
                    try { screenshotter.Screenshot(Path.Combine(screenshotDir, $"{label}.png")); }
                    catch (Exception ex) { failures.Add($"[{i} {label}] screenshot failed: {ex.Message}"); }
                }
            }
        }

        if (replayFrom is not null)
        {
            if (sessionDriver is null)
                notes.Add($"replay check skipped — {driver.Name} has no in-process session (replay needs the intent log).");
            else if (!completed)
                notes.Add("replay check skipped — the journey stopped on a thrown action.");
            else
            {
                var log = sessionDriver.Session.IntentLog;
                var replayFailures = ReplayCheck.Run(replayFrom(), log, boundaries);
                failures.AddRange(replayFailures.Select(f => "replay: " + f));
                if (replayFailures.Count == 0)
                    notes.Add($"replay check: {log.Count} intents replayed, {boundaries.Count} checkpoints and " +
                              $"{boundaries.Sum(b => b.Events.Count)} event lines identical");

                // Slice 2.4: the replayed pose lines must match at every commit too.
                var poseFailures = PoseReplayCheck.Run(replayFrom(), log, poseCommits);
                failures.AddRange(poseFailures.Select(f => "replay poses: " + f));
                if (poseFailures.Count == 0)
                    notes.Add($"replay check: {poseCommits.Count} commits and {poseCommits.Sum(c => c.Lines.Count)} pose lines identical");
            }
        }

        if (posesMissing)
            notes.Add($"{driver.Name} sent no poses (a pre-2.4 build?) — its pose stream is empty.");

        return new RunResult(driver.Name, checkpoints, failures, notes, goldens, eventLines, poseLines);

        // One observation's pose commits: the golden lines, and (in process) the replay record of each commit.
        void RecordPoses(int journeyStep, DriverObservation obs)
        {
            if (obs.Poses is not { } commits)
            {
                posesMissing = true;
                return;
            }
            poseLines.AddRange(PoseGolden.Lines(journeyStep, commits.Select(c => c.Lines)));
            poseCommits.AddRange(commits.Where(c => c.IntentCount is not null)
                .Select(c => new PoseCommit(journeyStep, c.IntentCount!.Value, c.AtMs, c.Lines)));
        }
    }

    /// <summary>One replay boundary: the session's intent count now, the observed view-model text and events.</summary>
    private static ReplayBoundary Boundary(int journeyStep, ISessionDriver driver, DriverObservation obs) =>
        new(journeyStep, driver.Session.IntentLog.Count, obs.ViewModel.ToJsonString(), obs.Events ?? Array.Empty<string>());

    /// <summary>
    /// Returns null when the census changed by EXACTLY <paramref name="expected"/> (name → signed delta),
    /// or a human-readable description of the first discrepancy. Any name not in <paramref name="expected"/>
    /// must be unchanged; any name in it must change by precisely that amount.
    /// </summary>
    private static string? CensusDeltaMismatch(
        ImmutableSortedDictionary<string, int> before,
        ImmutableSortedDictionary<string, int> after,
        IReadOnlyDictionary<string, int> expected)
    {
        var names = before.Keys.Union(after.Keys).OrderBy(n => n, StringComparer.Ordinal);
        foreach (var name in names)
        {
            before.TryGetValue(name, out var b);
            after.TryGetValue(name, out var a);
            var actual = a - b;
            expected.TryGetValue(name, out var want); // 0 when not listed → must be unchanged
            if (actual != want)
                return $"{name}: changed by {(actual >= 0 ? "+" : "")}{actual}, expected {(want >= 0 ? "+" : "")}{want}";
        }
        // Expected names that never appeared in either census (delta 0) but were asked to change.
        foreach (var (name, want) in expected)
            if (want != 0 && !before.ContainsKey(name) && !after.ContainsKey(name))
                return $"{name}: absent, expected change {(want >= 0 ? "+" : "")}{want}";
        return null;
    }

    private static void Act(IJourneyDriver driver, JourneyStep step)
    {
        if (step.Key is { } key) driver.Key(key);
        if (step.Back) driver.Back();
        if (step.Click is { } click) driver.Click(new Position(click.Row, click.Col), click.Button);
        if (step.AdvanceTimeMs is { } ms) driver.AdvanceTime(TimeSpan.FromMilliseconds(ms));
        if (step.Tick is { } ticks)
            for (int t = 0; t < ticks; t++) driver.Tick();
        // A step with only a comment/checkpoint/assert performs no action.
    }

    /// <summary>
    /// The Slice 1.6 events check: the driver's frontend must have received an event stream (the <c>events</c>
    /// every debug response carries, or the TUI controller's result events), and a step whose view-model
    /// changed must have emitted at least one event. The stream's content is diffed across drivers and
    /// against a golden from Slice 1.7.
    /// </summary>
    private static void VerifyEvents(int i, string label, DriverObservation obs, bool changed, List<string> failures)
    {
        if (obs.Events is null)
            failures.Add($"[{i} {label}] events: the driver reported no event stream (a pre-1.6 build?)");
        else if (changed && obs.Events.Count == 0)
            failures.Add($"[{i} {label}] events: the view-model changed but no event was emitted");
    }

    /// <summary>
    /// Runs stack-validity + progressability. Uses the full state when available; otherwise a
    /// reduced view-model-scoped check (active-bag cells: count/max/filter + cursor in bounds).
    /// </summary>
    private static void VerifyStructural(string driver, int i, string label,
        DriverObservation obs, List<string> failures, List<string> notes)
    {
        if (obs.FullState is { } state)
        {
            foreach (var v in InvariantChecker.Check(state))
                failures.Add($"[{i} {label}] invariant {v}");
        }
        else
        {
            foreach (var detail in CheckViewModelInvariants(obs.ViewModel))
                failures.Add($"[{i} {label}] invariant [view-model] {detail}");
            notes.Add($"[{i} {label}] structural invariants checked at view-model scope only ({driver}).");
        }
    }

    /// <summary>
    /// Reduced invariant check for view-model-only transports (real Godot over WebSocket):
    /// per active-bag cell, count in [1,max]; cursor within the reported grid.
    /// </summary>
    private static IEnumerable<string> CheckViewModelInvariants(JsonObject vm)
    {
        var cols = vm["gridColumns"]?.GetValue<int>() ?? 0;
        var rows = vm["gridRows"]?.GetValue<int>() ?? 0;

        if (vm["cells"] is JsonArray cells)
        {
            foreach (var cellNode in cells)
            {
                if (cellNode is not JsonObject cell) continue;
                if (cell["empty"]?.GetValue<bool>() ?? true) continue;
                var count = cell["count"]?.GetValue<int>() ?? 0;
                var max = cell["maxStack"]?.GetValue<int>() ?? int.MaxValue;
                var name = cell["item"]?.GetValue<string>() ?? "?";
                if (count < 1) yield return $"{name} has non-positive count {count}";
                if (count > max) yield return $"{name} exceeds max stack ({count} > {max})";
            }
        }

        var cr = vm["cursor"]?["row"]?.GetValue<int>() ?? 0;
        var cc = vm["cursor"]?["col"]?.GetValue<int>() ?? 0;
        if (cr < 0 || cr >= rows || cc < 0 || cc >= cols)
            yield return $"cursor ({cr},{cc}) out of bounds for {cols}x{rows}";
    }

    // --- Checkpoint emission ---

    private static void Emit(List<string> sink, int step, string label, JsonObject viewModel)
    {
        var line = new JsonObject
        {
            ["step"] = step,
            ["label"] = label,
            ["vm"] = viewModel.DeepClone()
        };
        sink.Add(line.ToJsonString());
    }

    // --- Recursive JSON subset containment ---

    /// <summary>
    /// True when every field in <paramref name="expected"/> is present in
    /// <paramref name="actual"/> with a matching value (objects recurse, arrays match by index
    /// as a prefix, scalars compare by JSON text). On failure, <paramref name="mismatch"/>
    /// describes the first divergence.
    /// </summary>
    private static bool JsonContains(JsonNode? actual, JsonNode? expected, out string mismatch)
    {
        mismatch = "";
        switch (expected)
        {
            case JsonObject eo:
                if (actual is not JsonObject ao) { mismatch = $"expected object, got {Describe(actual)}"; return false; }
                foreach (var (k, ev) in eo)
                {
                    if (!ao.ContainsKey(k)) { mismatch = $"missing key '{k}'"; return false; }
                    if (!JsonContains(ao[k], ev, out var inner)) { mismatch = $"'{k}': {inner}"; return false; }
                }
                return true;
            case JsonArray ea:
                if (actual is not JsonArray aa) { mismatch = $"expected array, got {Describe(actual)}"; return false; }
                for (int i = 0; i < ea.Count; i++)
                {
                    if (i >= aa.Count) { mismatch = $"array shorter than expected (index {i})"; return false; }
                    if (!JsonContains(aa[i], ea[i], out var inner)) { mismatch = $"[{i}]: {inner}"; return false; }
                }
                return true;
            default:
                var e = expected?.ToJsonString() ?? "null";
                var a = actual?.ToJsonString() ?? "null";
                if (e != a) { mismatch = $"expected {e}, got {a}"; return false; }
                return true;
        }
    }

    private static string Describe(JsonNode? n) => n?.ToJsonString() ?? "null";
}
