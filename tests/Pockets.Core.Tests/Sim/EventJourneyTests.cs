using System.Linq;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Pockets.Core;
using Pockets.Core.Data;
using Pockets.Core.Models;
using Pockets.Core.Rendering;
using Pockets.Core.Sim;

namespace Pockets.Core.Tests.Sim;

/// <summary>
/// Two-layer Phase 1, Slice 1.5 acceptance on both journeys: the coverage check (P1-D1: every changed cell,
/// slot and cursor is named by an event) on every step, and the event stream's canonical text being identical
/// across two independent sessions (fresh bag GUIDs each), so it is GUID-free and deterministic (P1-D8).
/// </summary>
public class EventJourneyTests
{
    /// <summary>Translates one journey step into DebugCommandHandler JSON commands (same order as JourneyRunnerCore.Act).</summary>
    private static IEnumerable<string> CommandsFor(JsonObject step)
    {
        if (step["key"]?.GetValue<string>() is { } key)
            yield return new JsonObject { ["action"] = "key", ["key"] = key }.ToJsonString();

        if (step["back"]?.GetValue<bool>() == true)
            yield return new JsonObject { ["action"] = "back" }.ToJsonString();

        if (step["click"] is JsonObject click)
            yield return new JsonObject
            {
                ["action"] = "click",
                ["row"] = click["row"]?.GetValue<int>() ?? 0,
                ["col"] = click["col"]?.GetValue<int>() ?? 0,
                ["button"] = click["button"]?.GetValue<string>() ?? "Primary"
            }.ToJsonString();

        if (step["advanceTime"]?.GetValue<int>() is { } ms)
            yield return new JsonObject { ["action"] = "advanceTime", ["ms"] = (long)ms }.ToJsonString();

        if (step["tick"]?.GetValue<int>() is { } ticks)
            foreach (var _ in Enumerable.Range(0, ticks))
                yield return new JsonObject { ["action"] = "tick" }.ToJsonString();
    }

    /// <summary>Builds a fully independent demo session (registry and dialogue reloaded from disk each call).</summary>
    private static GameSession NewDemoSession()
    {
        var registry = ContentLoader.LoadFromDirectory(TestPaths.DataDir);
        var dialogue = DialogueLoader.LoadFromDirectory(TestPaths.DataDir);
        return GameInitializer.CreateDemoProfile(registry, null, dialogue).NewSession();
    }

    /// <summary>Drives <paramref name="journeyFile"/> through DebugCommandHandler and returns its intent log.</summary>
    private static IReadOnlyList<Intent> RecordIntents(string journeyFile)
    {
        var controller = new GameController(NewDemoSession());
        var path = Path.Combine(TestPaths.RepoRoot, "journeys", journeyFile);
        var steps = ((JsonObject)JsonNode.Parse(File.ReadAllText(path))!)["steps"]!.AsArray().Select(n => (JsonObject)n!);
        foreach (var step in steps)
            foreach (var cmd in CommandsFor(step))
                Assert.True(JsonNode.Parse(DebugCommandHandler.Handle(controller, cmd))!["error"] is null, cmd);
        return controller.Session.IntentLog.Select(e => e.Intent).ToList();
    }

    /// <summary>
    /// Re-runs <paramref name="intents"/> from a fresh session's <see cref="SimState"/>, one
    /// <see cref="Simulation.Step"/> per sub-intent (composites expanded, exactly as the shell runs them; the
    /// shell writes back exactly the stepped state), calling <paramref name="onStep"/> with each step's
    /// pre-state and result. Neither journey undoes, so no snapshot handling is needed.
    /// </summary>
    private static void Replay(IReadOnlyList<Intent> intents, Action<Intent, SimState, StepResult<SimState>> onStep)
    {
        var sim = NewDemoSession().ToSimState();
        foreach (var intent in intents)
        {
            Assert.IsNotType<Intent.Undo>(intent);
            foreach (var sub in Simulation.Expand(sim, intent))
            {
                var result = Simulation.Step(sim, new[] { sub });
                onStep(sub, sim, result);
                sim = result.State;
            }
        }
    }

    [Theory]
    [InlineData("smoke.journey.json")]
    [InlineData("target-demo.journey.json")]
    public void Journey_EveryChangedCellSlotAndCursor_IsNamedByAnEvent(string journeyFile)
    {
        var failures = new List<string>();
        var steps = 0;
        Replay(RecordIntents(journeyFile), (intent, pre, result) =>
        {
            steps++;
            var missing = EventCoverage.Uncovered(pre, result.State, result.Events);
            if (missing.Count > 0)
                failures.Add($"step {pre.TickCount + 1} {intent}: " + string.Join("; ", missing)
                    + "\n    events: " + string.Join(" | ", EventText.Lines(result.Events)));
        });

        Assert.True(steps > 0);
        Assert.True(failures.Count == 0, $"{journeyFile}: {failures.Count} uncovered step(s):\n" + string.Join("\n", failures.Take(20)));
    }

    [Theory]
    [InlineData("smoke.journey.json")]
    [InlineData("target-demo.journey.json")]
    public void Journey_EventText_IsIdenticalAcrossIndependentSessions_AndGuidFree(string journeyFile)
    {
        var intents = RecordIntents(journeyFile);

        List<string> Stream()
        {
            var text = new EventText();
            var lines = new List<string>();
            Replay(intents, (_, _, result) => lines.AddRange(result.Events.Select(text.Line)));
            return lines;
        }

        var first = Stream();
        var second = Stream();

        Assert.NotEmpty(first);
        Assert.Equal(first, second);
        var guid = new Regex("[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-");
        Assert.DoesNotContain(first, l => guid.IsMatch(l));
        // Every mechanic the journeys exercise shows up as a semantic event, not just the envelope.
        var kinds = first.Select(l => l.Split(' ')[2]).ToHashSet();
        Assert.Superset(new HashSet<string> { "IntentApplied", "CursorMoved", "TimeAdvanced" }, kinds);
    }

    /// <summary>
    /// The frontend stream (every DebugCommandHandler response's <c>events</c>, one EventText per run) equals
    /// the stream of re-stepping the recorded intents with <see cref="Simulation.Step"/> per sub-intent — an
    /// event-only no-op (a refused peek during a beat) is in the intent log, so replay loses nothing.
    /// </summary>
    [Theory]
    [InlineData("smoke.journey.json")]
    [InlineData("target-demo.journey.json")]
    public void Journey_FrontendEventStream_EqualsReplayedStream(string journeyFile)
    {
        var controller = new GameController(NewDemoSession());
        var path = Path.Combine(TestPaths.RepoRoot, "journeys", journeyFile);
        var steps = ((JsonObject)JsonNode.Parse(File.ReadAllText(path))!)["steps"]!.AsArray().Select(n => (JsonObject)n!);
        var frontend = new List<string>();
        foreach (var cmd in steps.SelectMany(CommandsFor))
            frontend.AddRange(JsonNode.Parse(DebugCommandHandler.Handle(controller, cmd))!["events"]!.AsArray()
                .Select(e => e!.GetValue<string>()));

        var text = new EventText();
        var replayed = new List<string>();
        Replay(controller.Session.IntentLog.Select(e => e.Intent).ToList(),
            (_, _, result) => replayed.AddRange(result.Events.Select(text.Line)));

        Assert.NotEmpty(frontend);
        Assert.Equal(replayed, frontend);
    }
}
