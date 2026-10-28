using System.Linq;
using System.Text.Json.Nodes;
using Pockets.Core;
using Pockets.Core.Data;
using Pockets.Core.Models;
using Pockets.Core.Rendering;

namespace Pockets.Core.Tests.Sim;

/// <summary>
/// Two-layer Phase 1, Slice 1.3 acceptance: replaying a session's recorded <c>IntentLog</c> against an
/// independent fresh session reproduces the exact view-model at every journey step boundary.
/// </summary>
public class IntentReplayTests
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
        var profile = GameInitializer.CreateDemoProfile(registry, null, dialogue);
        return profile.NewSession();
    }

    /// <summary>Returns a short excerpt of both strings around the first differing character.</summary>
    private static string DiffExcerpt(string expected, string actual)
    {
        var n = Math.Min(expected.Length, actual.Length);
        var idx = Enumerable.Range(0, n).FirstOrDefault(i => expected[i] != actual[i], n);
        var start = Math.Max(0, idx - 40);
        string Slice(string s) => s.Substring(start, Math.Min(80, s.Length - start));
        return $"first diff at char {idx}\n  recorded: ...{Slice(expected)}...\n  replayed: ...{Slice(actual)}...";
    }

    /// <summary>
    /// Drives a journey through DebugCommandHandler recording (intent count, VM JSON) per step, then replays
    /// the IntentLog on an independent session and checks VMs at every recorded boundary plus the log itself.
    /// </summary>
    [Theory]
    [InlineData("smoke.journey.json")]
    [InlineData("target-demo.journey.json")]
    public void Journey_ReplayedFromIntentLog_ReproducesEveryStepViewModel(string journeyFile)
    {
        var controller = new GameController(NewDemoSession());

        var path = Path.Combine(TestPaths.RepoRoot, "journeys", journeyFile);
        var steps = ((JsonObject)JsonNode.Parse(File.ReadAllText(path))!)["steps"]!.AsArray()
            .Select(n => (JsonObject)n!)
            .ToList();

        // (journey step index, intent log count, vm); index -1 is the initial state.
        var recorded = new List<(int Step, int Count, string Vm)>
        {
            (-1, controller.Session.IntentLog.Count, ViewModelSerializer.SerializeToString(controller.Session))
        };

        for (var i = 0; i < steps.Count; i++)
        {
            foreach (var cmd in CommandsFor(steps[i]))
            {
                var resp = JsonNode.Parse(DebugCommandHandler.Handle(controller, cmd))!;
                Assert.True(resp["error"] is null, $"step {i}: transport error for {cmd}");
            }
            recorded.Add((i, controller.Session.IntentLog.Count, ViewModelSerializer.SerializeToString(controller.Session)));
        }

        var log = controller.Session.IntentLog;
        Assert.True(log.Count > 0, $"{journeyFile}: recorded IntentLog is empty");

        // Replay on a second, independent session.
        var replay = NewDemoSession();
        var failures = new List<string>();

        void CheckBoundaries(int replayed)
        {
            foreach (var rec in recorded.Where(r => r.Count == replayed))
            {
                var vm = ViewModelSerializer.SerializeToString(replay);
                if (vm != rec.Vm)
                    failures.Add($"journey step {rec.Step} (after {replayed} intents): VM mismatch; {DiffExcerpt(rec.Vm, vm)}");
            }
        }

        CheckBoundaries(0);
        for (var k = 0; k < log.Count; k++)
        {
            replay = replay.Apply(log[k].Intent);
            CheckBoundaries(k + 1);
        }

        Assert.True(failures.Count == 0, $"{journeyFile}:\n" + string.Join("\n", failures));

        Assert.Equal(log.Count, replay.IntentLog.Count);
        var mismatches = Enumerable.Range(0, log.Count)
            .Where(k => log[k].Step != replay.IntentLog[k].Step || !Equals(log[k].Intent, replay.IntentLog[k].Intent))
            .Select(k => $"#{k}: recorded ({log[k].Step}, {log[k].Intent}) vs replayed ({replay.IntentLog[k].Step}, {replay.IntentLog[k].Intent})")
            .ToList();
        Assert.True(mismatches.Count == 0, $"{journeyFile}: IntentLog differs:\n" + string.Join("\n", mismatches));
    }
}
