using System.Collections.Immutable;
using System.Text.Json.Nodes;
using Pockets.Core.Models;
using Pockets.Core.Rendering;

namespace Pockets.JourneyRunner;

/// <summary>Outcome of running one script against one driver.</summary>
public record RunResult(
    string Driver,
    IReadOnlyList<string> Checkpoints,
    IReadOnlyList<string> Failures,
    IReadOnlyList<string> Notes)
{
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
    public static RunResult Run(IJourneyDriver driver, JourneyScript script)
    {
        var checkpoints = new List<string>();
        var failures = new List<string>();
        var notes = new List<string>();

        // Baseline observation before any step drives conservation comparisons.
        var start = driver.Observe();
        var prevCensus = start.FullState is { } s0 ? InvariantChecker.Census(s0) : null;
        Emit(checkpoints, -1, "start", start.ViewModel);
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
                break; // a thrown action means the journey can't continue meaningfully
            }

            var obs = driver.Observe();

            // --- Invariant pack ---
            VerifyStructural(driver.Name, i, label, obs, failures, notes);

            // Conservation (requires full state; view-model-only drivers flag it as limited).
            if (obs.FullState is { } state)
            {
                var census = InvariantChecker.Census(state);
                if (prevCensus is not null)
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
        }

        return new RunResult(driver.Name, checkpoints, failures, notes);
    }

    private static void Act(IJourneyDriver driver, JourneyStep step)
    {
        if (step.Key is { } key) driver.Key(key);
        if (step.Back) driver.Back();
        if (step.Click is { } click) driver.Click(new Position(click.Row, click.Col), click.Button);
        if (step.Tick is { } ticks)
            for (int t = 0; t < ticks; t++) driver.Tick();
        // A step with only a comment/checkpoint/assert performs no action.
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
