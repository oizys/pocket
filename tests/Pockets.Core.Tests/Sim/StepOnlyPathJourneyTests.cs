using System.Linq;
using System.Text.Json.Nodes;
using Pockets.Core;
using Pockets.Core.Data;
using Pockets.Core.Models;
using Pockets.Core.Rendering;

namespace Pockets.Core.Tests.Sim;

/// <summary>
/// Two-layer Phase 1, Slice 1.2 acceptance (item 3): <c>Simulation.Step</c> is the ONLY path that
/// advances <see cref="GameSession.TickCount"/> or changes the world. Drives journeys/smoke.journey.json
/// in-process the way the mock-godot driver does (DebugCommandHandler over a GameController) and
/// compares per-step session snapshots against <see cref="GameSession.DebugStepCount"/>.
/// </summary>
public class StepOnlyPathJourneyTests
{
    /// <summary>One observation of the session facts the test compares across a journey step.</summary>
    private sealed record Snapshot(GameState Current, int TickCount, int UndoDepth, int StepCount);

    /// <summary>Captures the controller's current session facts plus the thread-static Step counter.</summary>
    private static Snapshot Capture(GameController controller)
    {
        var s = controller.Session;
        return new Snapshot(s.Current, s.TickCount, s.UndoDepth, GameSession.DebugStepCount);
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
    /// Every smoke-journey step: TickCount delta equals the Step-application delta; a step that applied
    /// no Step result left the world (Store, KnownRecipes, ZonesReached) untouched.
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

            // 1. Step is the only path that advances TickCount.
            Assert.True(tickDelta == stepDelta,
                $"step {i}: TickCount delta {tickDelta} != DebugStepCount delta {stepDelta}");

            if (stepDelta == 0)
            {
                // 2. No Step applied => the world did not change. Cursor moves, dialogue advance,
                // split/recipe-menu modals and undo are still direct session writes in Slice 1.2 (they
                // move behind Step in Slice 1.3), so changes confined to Locations/Dialogue/Ui are
                // allowed here; the Store instance, KnownRecipes and ZonesReached must be untouched.
                Assert.True(ReferenceEquals(before.Current.Store, after.Current.Store),
                    $"step {i}: BagStore instance changed without a Step");
                Assert.True(before.Current.KnownRecipes.SetEquals(after.Current.KnownRecipes),
                    $"step {i}: KnownRecipes changed without a Step");
                Assert.True(before.Current.ZonesReached.SequenceEqual(after.Current.ZonesReached),
                    $"step {i}: ZonesReached changed without a Step");
            }
            else if (!ReferenceEquals(before.Current, after.Current))
            {
                worldChangedViaStep++;
            }
        }

        // 3. Sanity: the journey actually exercised the Step path and changed state through it.
        Assert.True(totalSteps > 0, "journey made no Step calls");
        Assert.True(worldChangedViaStep > 0, "no journey step changed Current via a Step result");
    }
}
