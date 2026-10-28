using System.Text.Json.Nodes;
using Pockets.Core.Data;
using Pockets.Core.Models;
using Pockets.Core.Rendering;

namespace Pockets.Core.Tests.Fine;

/// <summary>
/// Slice 2.8 test plumbing: a journey script driven through <see cref="DebugCommandHandler"/> (the mock-godot path),
/// one observation per journey step (the VM text and the step's event lines), with an optional hook after each step.
/// </summary>
internal static class JourneyCommands
{
    /// <summary>One journey step's L1 observation: the VM JSON and the canonical event lines it emitted.</summary>
    internal sealed record Observation(int Step, string ViewModel, IReadOnlyList<string> Events);

    /// <summary>Builds a fully independent demo session (registry and dialogue reloaded from disk each call).</summary>
    internal static GameSession NewDemoSession()
    {
        var registry = ContentLoader.LoadFromDirectory(TestPaths.DataDir);
        var dialogue = DialogueLoader.LoadFromDirectory(TestPaths.DataDir);
        return GameInitializer.CreateDemoProfile(registry, null, dialogue).NewSession();
    }

    /// <summary>The steps of <c>journeys/&lt;journeyFile&gt;</c>.</summary>
    internal static IReadOnlyList<JsonObject> Steps(string journeyFile)
    {
        var path = Path.Combine(TestPaths.RepoRoot, "journeys", journeyFile);
        return ((JsonObject)JsonNode.Parse(File.ReadAllText(path))!)["steps"]!.AsArray().Select(n => (JsonObject)n!).ToList();
    }

    /// <summary>Translates one journey step into debug commands, in JourneyRunnerCore.Act's order.</summary>
    internal static IEnumerable<string> CommandsFor(JsonObject step)
    {
        if (step["setMovement"] is JsonObject m)
            yield return new JsonObject { ["action"] = "setMovement", ["loc"] = m["loc"]?.GetValue<string>() ?? "B", ["model"] = m["model"]!.GetValue<string>() }.ToJsonString();

        if (step.ContainsKey("steer"))
            yield return step["steer"] switch
            {
                JsonObject so => new JsonObject { ["action"] = "steer", ["loc"] = so["loc"]?.GetValue<string>() ?? "B", ["dir"] = so["dir"]?.GetValue<string>() ?? "none" }.ToJsonString(),
                JsonValue sv => new JsonObject { ["action"] = "steer", ["loc"] = "B", ["dir"] = sv.GetValue<string>() }.ToJsonString(),
                _ => new JsonObject { ["action"] = "steer", ["loc"] = "B", ["dir"] = "none" }.ToJsonString()
            };

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
    /// Drives <paramref name="journeyFile"/> on <paramref name="controller"/> and returns one observation per step
    /// (start = -1). <paramref name="afterStep"/> runs after each observation (the start included), with the index of the
    /// step about to run next — the runner's <c>--fuzz-fine</c> hook point.
    /// </summary>
    internal static IReadOnlyList<Observation> Run(GameController controller, string journeyFile, Action<GameController, int>? afterStep = null)
    {
        var observations = new List<Observation> { new(-1, ViewModelSerializer.Serialize(controller.Session).ToJsonString(), []) };
        afterStep?.Invoke(controller, 0);
        var steps = Steps(journeyFile);
        for (var i = 0; i < steps.Count; i++)
        {
            var events = new List<string>();
            foreach (var cmd in CommandsFor(steps[i]))
            {
                var resp = JsonNode.Parse(DebugCommandHandler.Handle(controller, cmd))!;
                Assert.True(resp["error"] is null, $"step {i}: {cmd} → {resp["error"]}");
                events.AddRange(resp["events"]!.AsArray().Select(e => e!.GetValue<string>()));
            }
            observations.Add(new Observation(i, ViewModelSerializer.Serialize(controller.Session).ToJsonString(), events));
            afterStep?.Invoke(controller, i + 1);
        }
        return observations;
    }

    /// <summary>The first step whose VM or events differ, described; null when the two runs are identical.</summary>
    internal static string? FirstDifference(IReadOnlyList<Observation> expected, IReadOnlyList<Observation> actual)
    {
        if (expected.Count != actual.Count) return $"observation count {expected.Count} vs {actual.Count}";
        return expected.Zip(actual)
            .Where(p => p.First.ViewModel != p.Second.ViewModel || !p.First.Events.SequenceEqual(p.Second.Events))
            .Select(p => p.First.ViewModel != p.Second.ViewModel
                ? $"step {p.First.Step}: view-model differs"
                : $"step {p.First.Step}: events differ\n  expected: {string.Join(" | ", p.First.Events)}\n  actual:   {string.Join(" | ", p.Second.Events)}")
            .FirstOrDefault();
    }
}
