using System.Linq;
using System.Text.Json.Nodes;
using Pockets.Core;
using Pockets.Core.Data;
using Pockets.Core.Models;
using Pockets.Core.Rendering;
using Pockets.Core.Sim;

namespace Pockets.Core.Tests.Sim;

/// <summary>
/// Two-layer Phase 1, Slice 1.7 acceptance: the event-golden line format and the replay gate
/// (<see cref="ReplayCheck"/>) pass on both journeys and fail on a mutated event, a dropped event and a
/// mutated view-model.
/// </summary>
public class ReplayCheckTests
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

    /// <summary>
    /// Drives a journey through DebugCommandHandler (as the mock-godot driver does), recording a boundary per
    /// step: the "state" query's view-model text and the "events" lines of every response since the last boundary.
    /// </summary>
    private static (IReadOnlyList<(int Step, Intent Intent)> Log, List<ReplayBoundary> Boundaries) Record(string journeyFile)
    {
        var controller = new GameController(NewDemoSession());
        var path = Path.Combine(TestPaths.RepoRoot, "journeys", journeyFile);
        var steps = ((JsonObject)JsonNode.Parse(File.ReadAllText(path))!)["steps"]!.AsArray().Select(n => (JsonObject)n!).ToList();

        var events = new List<string>();
        ReplayBoundary Boundary(int i)
        {
            var vm = JsonNode.Parse(DebugCommandHandler.Handle(controller, """{"action":"state"}"""))!["state"]!.ToJsonString();
            var b = new ReplayBoundary(i, controller.Session.IntentLog.Count, vm, events.ToList());
            events.Clear();
            return b;
        }

        var boundaries = new List<ReplayBoundary> { Boundary(-1) };
        for (var i = 0; i < steps.Count; i++)
        {
            foreach (var cmd in CommandsFor(steps[i]))
            {
                var resp = JsonNode.Parse(DebugCommandHandler.Handle(controller, cmd))!;
                Assert.True(resp["error"] is null, $"step {i}: transport error for {cmd}");
                events.AddRange(resp["events"]!.AsArray().Select(e => e!.GetValue<string>()));
            }
            boundaries.Add(Boundary(i));
        }
        return (controller.Session.IntentLog.ToList(), boundaries);
    }

    [Theory]
    [InlineData("smoke.journey.json")]
    [InlineData("target-demo.journey.json")]
    public void Journey_Replay_PassesTheGate(string journeyFile)
    {
        var (log, boundaries) = Record(journeyFile);
        Assert.NotEmpty(log);
        Assert.Contains(boundaries, b => b.Events.Count > 0);
        Assert.Empty(ReplayCheck.Run(NewDemoSession(), log, boundaries));
    }

    [Theory]
    [InlineData("smoke.journey.json")]
    [InlineData("target-demo.journey.json")]
    public void MutatedEvent_FailsTheGate_NamingTheJourneyStep(string journeyFile)
    {
        var (log, boundaries) = Record(journeyFile);
        var at = boundaries.IndexOf(boundaries.Skip(boundaries.Count / 2).First(b => b.Events.Count > 0));
        var target = boundaries[at];
        var events = target.Events.ToList();
        events[0] = events[0].Replace("IntentApplied", "IntentAppliex");
        boundaries[at] = target with { Events = events };

        var failures = ReplayCheck.Run(NewDemoSession(), log, boundaries);

        var failure = Assert.Single(failures);
        Assert.Contains($"journey step {target.JourneyStep}:", failure);
        Assert.Contains("IntentAppliex", failure);
    }

    [Fact]
    public void DroppedEvent_AndMutatedViewModel_FailTheGate()
    {
        var (log, boundaries) = Record("smoke.journey.json");
        var at = boundaries.FindIndex(b => b.Events.Count > 1);

        var dropped = boundaries.ToList();
        dropped[at] = dropped[at] with { Events = dropped[at].Events.Skip(1).ToList() };
        Assert.Contains(ReplayCheck.Run(NewDemoSession(), log, dropped), f => f.StartsWith($"journey step {boundaries[at].JourneyStep}:"));

        var vmAt = boundaries.Count / 2;
        var mutated = boundaries.ToList();
        mutated[vmAt] = mutated[vmAt] with { ViewModel = mutated[vmAt].ViewModel.Replace("\"cursor\"", "\"cursoR\"") };
        Assert.Contains(ReplayCheck.Run(NewDemoSession(), log, mutated),
            f => f.StartsWith($"journey step {boundaries[vmAt].JourneyStep}:") && f.Contains("view-model"));
    }

    [Fact]
    public void BoundaryPastTheLog_AndAlteredLog_Fail()
    {
        var (log, boundaries) = Record("smoke.journey.json");

        var past = boundaries.Append(new ReplayBoundary(99, log.Count + 1, "{}", Array.Empty<string>())).ToList();
        Assert.Contains(ReplayCheck.Run(NewDemoSession(), log, past), f => f.Contains("journey step 99"));

        var shorter = log.Take(log.Count - 1).ToList();
        Assert.NotEmpty(ReplayCheck.Run(NewDemoSession(), shorter, boundaries));
    }

    [Fact]
    public void EventGolden_Lines_PrefixJourneyStepAndSubStepOrdinal()
    {
        var lines = new[] { "25 0 IntentApplied a=1", "25 1 Bumped b=2", "25 0 IntentApplied c=3", "26 0 TimeAdvanced d=4" };

        Assert.Equal(new[]
        {
            "7:0 25 0 IntentApplied a=1", "7:0 25 1 Bumped b=2", "7:1 25 0 IntentApplied c=3", "7:2 26 0 TimeAdvanced d=4"
        }, EventGolden.Lines(7, lines));
        Assert.Equal(new[] { "-1:0 3 2 Bumped" }, EventGolden.Lines(-1, new[] { "3 2 Bumped" }));
        Assert.Empty(EventGolden.Lines(0, Array.Empty<string>()));
    }
}
