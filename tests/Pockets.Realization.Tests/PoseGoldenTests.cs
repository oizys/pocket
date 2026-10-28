using System.Text.Json.Nodes;
using Pockets.Core.Models;
using Pockets.Core.Sim;

namespace Pockets.Realization.Tests;

/// <summary>
/// Slice 2.4 acceptance over the journeys: the debug-protocol pose stream of target-demo is the committed golden
/// (<c>journeys/goldens/target-demo.poses</c>), a one-field policy change fails it (walk 125 → 126 ms), and the pose
/// replay check reproduces every commit from the intent log.
/// </summary>
public class PoseGoldenTests
{
    private static string GoldenPath => Path.Combine(TestPaths.RepoRoot, "journeys", "goldens", "target-demo.poses");

    /// <summary>A journey's steps.</summary>
    private static List<JsonObject> Steps(string journeyFile) =>
        ((JsonObject)JsonNode.Parse(File.ReadAllText(Path.Combine(TestPaths.RepoRoot, "journeys", journeyFile)))!)["steps"]!
            .AsArray().Select(n => (JsonObject)n!).ToList();

    /// <summary>One journey step as debug commands, in JourneyRunnerCore.Act's order.</summary>
    private static IEnumerable<JsonObject> Commands(JsonObject step)
    {
        if (step["key"]?.GetValue<string>() is { } key)
            yield return new JsonObject { ["action"] = "key", ["key"] = key };
        if (step["back"]?.GetValue<bool>() == true)
            yield return new JsonObject { ["action"] = "back" };
        if (step["click"] is JsonObject click)
            yield return new JsonObject
            {
                ["action"] = "click", ["row"] = click["row"]?.GetValue<int>() ?? 0, ["col"] = click["col"]?.GetValue<int>() ?? 0,
                ["button"] = click["button"]?.GetValue<string>() ?? "Primary"
            };
        if (step["advanceTime"]?.GetValue<int>() is { } ms)
            yield return new JsonObject { ["action"] = "advanceTime", ["ms"] = ms };
        if (step["tick"]?.GetValue<int>() is { } ticks)
            foreach (var _ in Enumerable.Range(0, ticks))
                yield return new JsonObject { ["action"] = "tick" };
    }

    /// <summary>
    /// Runs a journey over the mock-godot path and returns its golden lines plus the replay record of every commit.
    /// </summary>
    private static (List<string> Golden, List<PoseCommit> Commits, GameController Controller) OverHandler(string journeyFile)
    {
        var c = PoseStreamTests.DemoController();
        var golden = new List<string>();
        var commits = new List<PoseCommit>();
        foreach (var (step, i) in Steps(journeyFile).Select((s, i) => (s, i)))
        {
            var lines = new List<IReadOnlyList<string>>();
            foreach (var cmd in Commands(step))
            {
                var resp = JsonNode.Parse(PoseDebugCommandHandler.Handle(c, cmd.ToJsonString()))!;
                var poses = resp["poses"]!.AsArray().Select(n => n!.GetValue<string>()).ToList();
                lines.Add(poses);
                commits.Add(new PoseCommit(i, c.Session.IntentLog.Count, PoseDebugCommandHandler.AtMs(cmd), poses));
            }
            golden.AddRange(PoseGolden.Lines(i, lines));
        }
        return (golden, commits, c);
    }

    /// <summary>
    /// The same journey driven on the controller directly with a <see cref="PoseStream"/> under
    /// <paramref name="policy"/>: events named first, then poses, as the handler does.
    /// </summary>
    private static List<string> Direct(string journeyFile, RealizationPolicy policy)
    {
        var c = PoseStreamTests.DemoController();
        var text = new EventText();
        var stream = new PoseStream(c.Session.Current, text.Names, policy);
        var golden = new List<string>();
        foreach (var (step, i) in Steps(journeyFile).Select((s, i) => (s, i)))
        {
            var lines = new List<IReadOnlyList<string>>();
            foreach (var cmd in Commands(step))
            {
                var result = Apply(c, cmd);
                _ = result.Events.Select(text.Line).ToList();
                lines.Add(stream.Commit(result.Events, c.Session.Current, PoseDebugCommandHandler.AtMs(cmd)));
            }
            golden.AddRange(PoseGolden.Lines(i, lines));
        }
        return golden;
    }

    /// <summary>A debug command applied on the controller directly.</summary>
    private static ControllerResult Apply(GameController c, JsonObject cmd) => cmd["action"]!.GetValue<string>() switch
    {
        "key" => c.HandleKey(Enum.Parse<GameKey>(cmd["key"]!.GetValue<string>(), ignoreCase: true)),
        "back" => c.HandleBackClick(),
        "click" => c.HandleGridClick(new Position(cmd["row"]!.GetValue<int>(), cmd["col"]!.GetValue<int>()),
            Enum.TryParse<ClickType>(cmd["button"]!.GetValue<string>(), true, out var b) ? b : ClickType.Primary),
        "advanceTime" => c.AdvanceClock(TimeSpan.FromMilliseconds(cmd["ms"]!.GetValue<int>())),
        "tick" => c.Tick(),
        var a => throw new ArgumentException(a)
    };

    /// <summary>Acceptance 1: target-demo over the debug protocol is the committed pose golden, and it walks the body.</summary>
    [Fact]
    public void TargetDemo_OverHandler_IsTheGolden()
    {
        var (golden, _, _) = OverHandler("target-demo.journey.json");
        Assert.True(File.Exists(GoldenPath), "journeys/goldens/target-demo.poses is missing (make record-goldens)");
        Assert.Equal(File.ReadAllLines(GoldenPath), golden);
        Assert.Contains(golden, l => l.Contains("entity=Body(B)", StringComparison.Ordinal));
    }

    /// <summary>The handler and a direct <see cref="PoseStream"/> agree, so the TUI driver's stream matches mock-godot's.</summary>
    [Theory]
    [InlineData("smoke.journey.json")]
    [InlineData("target-demo.journey.json")]
    public void Handler_EqualsDirectStream(string journeyFile) =>
        Assert.Equal(OverHandler(journeyFile).Golden, Direct(journeyFile, RealizationPolicy.Default));

    /// <summary>Acceptance 5: a one-field policy change (walk 125 → 126 ms) fails the golden.</summary>
    [Fact]
    public void Mutation_Walk126_FailsTheGolden()
    {
        var golden = File.ReadAllLines(GoldenPath);
        Assert.Equal(golden, Direct("target-demo.journey.json", RealizationPolicy.Default));
        Assert.NotEqual(golden, Direct("target-demo.journey.json", RealizationPolicy.Default with { WalkMs = 126 }));
    }

    /// <summary>Acceptance 4: replaying the intent log reproduces every commit's pose lines.</summary>
    [Theory]
    [InlineData("smoke.journey.json")]
    [InlineData("target-demo.journey.json")]
    public void Replay_ReproducesEveryCommit(string journeyFile)
    {
        var (_, commits, c) = OverHandler(journeyFile);
        Assert.Contains(commits, k => k.Lines.Count > 0);
        Assert.Empty(PoseReplayCheck.Run(PoseStreamTests.DemoController().Session, c.Session.IntentLog, commits));
    }

    /// <summary>A tampered recorded line is reported with its journey step.</summary>
    [Fact]
    public void Replay_ReportsADifferingLine()
    {
        var (_, commits, c) = OverHandler("smoke.journey.json");
        var at = commits.FindIndex(k => k.Lines.Count > 0);
        commits[at] = commits[at] with { Lines = commits[at].Lines.Select(l => l.Replace("t=0 ", "t=1 ")).ToList() };

        var failures = PoseReplayCheck.Run(PoseStreamTests.DemoController().Session, c.Session.IntentLog, commits);
        Assert.Single(failures);
        Assert.Contains($"journey step {commits[at].JourneyStep}", failures[0]);
    }
}
