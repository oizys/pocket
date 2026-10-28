using System.Linq;
using System.Text.Json.Nodes;
using Pockets.Core;
using Pockets.Core.Data;
using Pockets.Core.Models;
using Pockets.Core.Rendering;

namespace Pockets.Core.Tests.Sim;

/// <summary>
/// Two-layer Phase 1, Slice 1.2 acceptance (item 3), tightened by Slice 1.3: <c>Simulation.Step</c> is the
/// ONLY path that advances <see cref="GameSession.TickCount"/> or changes any L1 state. Drives
/// journeys/smoke.journey.json in-process the way the mock-godot driver does (DebugCommandHandler over a
/// GameController) and compares per-step session snapshots against <see cref="GameSession.DebugStepCount"/>.
/// </summary>
public class StepOnlyPathJourneyTests
{
    /// <summary>One observation of the session facts the test compares across a journey step.</summary>
    private sealed record Snapshot(
        GameState Current, int TickCount, int UndoDepth, int StepCount,
        SplitModeState? SplitMode, RecipeMenuState? RecipeMenu, TimeSpan Elapsed, int IntentCount);

    /// <summary>Captures the controller's current session facts plus the thread-static Step counter.</summary>
    private static Snapshot Capture(GameController controller)
    {
        var s = controller.Session;
        return new Snapshot(s.Current, s.TickCount, s.UndoDepth, GameSession.DebugStepCount,
            s.SplitMode, s.RecipeMenu, s.Elapsed, s.IntentLog.Count);
    }

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

    /// <summary>
    /// Every smoke-journey step: TickCount advances only through Step (never by more than the number of
    /// Step results applied); a journey step that applied no Step result left every L1 field untouched
    /// (the same <see cref="GameState"/> instance, TickCount, split/menu modal state, clock) — since Slice 1.3
    /// cursor moves, dialogue, the modals and the clock are intents too, so nothing is exempt. Every
    /// state-changing journey step also grew the intent log.
    /// </summary>
    [Fact]
    public void SmokeJourney_OnlyStepAdvancesTickCountAndChangesWorld()
    {
        var registry = ContentLoader.LoadFromDirectory(TestPaths.DataDir);
        var dialogue = DialogueLoader.LoadFromDirectory(TestPaths.DataDir);
        var profile = GameInitializer.CreateDemoProfile(registry, null, dialogue);
        var controller = new GameController(profile.NewSession());

        var path = Path.Combine(TestPaths.RepoRoot, "journeys", "smoke.journey.json");
        var steps = ((JsonObject)JsonNode.Parse(File.ReadAllText(path))!)["steps"]!.AsArray()
            .Select(n => (JsonObject)n!)
            .ToList();

        var totalSteps = 0;
        var worldChangedViaStep = 0;

        for (var i = 0; i < steps.Count; i++)
        {
            var before = Capture(controller);
            foreach (var cmd in CommandsFor(steps[i]))
            {
                var resp = JsonNode.Parse(DebugCommandHandler.Handle(controller, cmd))!;
                Assert.True(resp["error"] is null, $"step {i}: transport error for {cmd}");
            }
            var after = Capture(controller);

            var stepDelta = after.StepCount - before.StepCount;
            var tickDelta = after.TickCount - before.TickCount;
            totalSteps += stepDelta;

            // 1. Step is the only path that advances TickCount (non-timed intents step without a tick).
            Assert.True(tickDelta >= 0 && tickDelta <= stepDelta,
                $"step {i}: TickCount delta {tickDelta} exceeds DebugStepCount delta {stepDelta}");

            if (stepDelta == 0)
            {
                // 2. No Step applied => no L1 field changed at all (the smoke journey never undoes).
                Assert.True(ReferenceEquals(before.Current, after.Current),
                    $"step {i}: GameState changed without a Step");
                Assert.True(before.SplitMode == after.SplitMode && before.RecipeMenu == after.RecipeMenu
                        && before.Elapsed == after.Elapsed,
                    $"step {i}: modal or clock state changed without a Step");
            }
            else
            {
                // 3. A state-changing journey step is recorded in the intent log.
                Assert.True(after.IntentCount > before.IntentCount, $"step {i}: Step applied but no intent was logged");
                if (!ReferenceEquals(before.Current, after.Current))
                    worldChangedViaStep++;
            }
        }

        // 3. Sanity: the journey actually exercised the Step path and changed state through it.
        Assert.True(totalSteps > 0, "journey made no Step calls");
        Assert.True(worldChangedViaStep > 0, "no journey step changed Current via a Step result");
    }
}
