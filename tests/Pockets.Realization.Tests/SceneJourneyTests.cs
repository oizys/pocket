using System.Text.Json.Nodes;
using Pockets.Core;
using Pockets.Core.Data;
using Pockets.Core.Models;
using Pockets.Core.Rendering;
using Pockets.Core.Sim;

namespace Pockets.Realization.Tests;

/// <summary>Acceptance property "n = 1 ≡ VM": the rest scene agrees with the view model after every journey step.</summary>
public class SceneJourneyTests
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

    /// <summary>Asserts the scene of the controller's current state agrees with its view model; returns whether a body was present.</summary>
    private static bool AssertAgrees(GameController controller, string where)
    {
        var state = controller.Session.Current;
        var vm = ViewModelSerializer.Serialize(controller.Session);
        var scene = Scene.FromState(state, [LocationId.B, LocationId.H, LocationId.T]);
        var bag = state.ActiveBagAt(LocationId.B);

        var vmCells = vm["cells"]!.AsArray().Where(c => c!["empty"]!.GetValue<bool>() == false)
            .Select(c => (c!["row"]!.GetValue<int>(), c["col"]!.GetValue<int>())).OrderBy(t => t).ToList();
        var stackCells = scene.Poses.Where(p => p.Bag == bag.Id && p.Entity is EntityKey.Stack)
            .Select(p => (p.CellRow, p.CellCol)).OrderBy(t => t).ToList();
        Assert.True(vmCells.SequenceEqual(stackCells), $"{where}: B stacks differ");

        var body = scene.Find(new EntityKey.Body(LocationId.B));
        Assert.True(Scene.IsWorldBag(bag) == body is not null, $"{where}: body presence");
        if (body is { } b)
            Assert.True((b.CellRow, b.CellCol) == (vm["cursor"]!["row"]!.GetValue<int>(), vm["cursor"]!["col"]!.GetValue<int>()),
                $"{where}: body at cursor");

        Assert.True(scene.Poses.Count(p => p.Bag == state.HandBag.Id) == vm["hand"]!.AsArray().Count, $"{where}: hand");
        var toolbarSlots = scene.Poses.Where(p => p.Bag == state.ToolbarBagId && p.Entity is EntityKey.Stack)
            .Select(p => ((EntityKey.Stack)p.Entity).At.Index).ToList();
        var vmSlots = vm["toolbar"]!.AsArray().Select(s => s!["slot"]!.GetValue<int>()).ToList();
        Assert.True(vmSlots.SequenceEqual(toolbarSlots), $"{where}: toolbar slots");

        Assert.All(scene.Poses, p =>
        {
            Assert.Equal((0, 0), (p.SubX, p.SubY));
            Assert.Null(p.Height);
            if (p.Entity is EntityKey.Stack) Assert.Null(p.Facing);
        });
        return body is not null;
    }

    /// <summary>The rest scene equals the view model at the start and after every step of both journeys.</summary>
    [Theory]
    [InlineData("smoke.journey.json")]
    [InlineData("target-demo.journey.json")]
    public void Journey_RestScene_EqualsViewModel_AfterEveryStep(string journeyFile)
    {
        var registry = ContentLoader.LoadFromDirectory(TestPaths.DataDir);
        var dialogue = DialogueLoader.LoadFromDirectory(TestPaths.DataDir);
        var controller = new GameController(GameInitializer.CreateDemoProfile(registry, null, dialogue).NewSession());
        var path = Path.Combine(TestPaths.RepoRoot, "journeys", journeyFile);
        var steps = ((JsonObject)JsonNode.Parse(File.ReadAllText(path))!)["steps"]!.AsArray().Select(n => (JsonObject)n!).ToList();

        var sawBody = AssertAgrees(controller, "start");
        foreach (var (step, i) in steps.Select((s, i) => (s, i)))
        {
            foreach (var cmd in CommandsFor(step))
                Assert.True(JsonNode.Parse(DebugCommandHandler.Handle(controller, cmd))!["error"] is null, cmd);
            sawBody |= AssertAgrees(controller, $"step {i}");
        }

        if (journeyFile.StartsWith("target-demo", StringComparison.Ordinal))
            Assert.True(sawBody, "target-demo never had a Body");
    }
}
