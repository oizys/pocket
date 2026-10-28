using System.Text.Json.Nodes;
using Pockets.Core;
using Pockets.Core.Data;
using Pockets.Core.Models;
using Pockets.Core.Sim;

namespace Pockets.Realization.Tests;

/// <summary>
/// Timelines over both journeys: every step's events are committed, and the u = 1 sample (the TUI's n = 1 frame)
/// is the rest scene of the new state, so the "n = 1 ≡ VM" property of <see cref="SceneJourneyTests"/> still holds.
/// </summary>
public class TimelineJourneyTests
{
    private static readonly LocationId[] Shown = [LocationId.B, LocationId.H, LocationId.T];

    /// <summary>Runs one journey step on the controller directly (same order as JourneyRunnerCore.Act) and returns each call's result.</summary>
    private static IEnumerable<ControllerResult> Act(GameController controller, JsonObject step)
    {
        if (step["key"]?.GetValue<string>() is { } key)
            yield return controller.HandleKey(Enum.Parse<GameKey>(key, ignoreCase: true));

        if (step["back"]?.GetValue<bool>() == true)
            yield return controller.HandleBackClick();

        if (step["click"] is JsonObject click)
            yield return controller.HandleGridClick(
                new Position(click["row"]?.GetValue<int>() ?? 0, click["col"]?.GetValue<int>() ?? 0),
                Enum.TryParse<ClickType>(click["button"]?.GetValue<string>() ?? "Primary", true, out var b) ? b : ClickType.Primary);

        if (step["advanceTime"]?.GetValue<int>() is { } ms)
            yield return controller.AdvanceClock(TimeSpan.FromMilliseconds(ms));

        if (step["tick"]?.GetValue<int>() is { } ticks)
            foreach (var _ in Enumerable.Range(0, ticks))
                yield return controller.Tick();
    }

    /// <summary>Runs a journey, committing every call's events; yields (timeline, controller) after each commit.</summary>
    private static IEnumerable<(Timeline Timeline, GameController Controller)> Run(string journeyFile)
    {
        var registry = ContentLoader.LoadFromDirectory(TestPaths.DataDir);
        var dialogue = DialogueLoader.LoadFromDirectory(TestPaths.DataDir);
        var controller = new GameController(GameInitializer.CreateDemoProfile(registry, null, dialogue).NewSession());
        var path = Path.Combine(TestPaths.RepoRoot, "journeys", journeyFile);
        var steps = ((JsonObject)JsonNode.Parse(File.ReadAllText(path))!)["steps"]!.AsArray().Select(n => (JsonObject)n!).ToList();

        var timeline = Timeline.Rest(Scene.FromState(controller.Session.Current, Shown));
        foreach (var step in steps)
            foreach (var result in Act(controller, step))
            {
                timeline = timeline.Commit(result.Events, Scene.FromState(controller.Session.Current, Shown, null, timeline.Facings),
                    RealizationPolicy.Default);
                yield return (timeline, controller);
            }
    }

    /// <summary>
    /// After every commit the u = 1 sample is the rest scene (same cells as the VM, everything centred, no height),
    /// and while tracks are live every sampled stack is still in a shown bag on the grid.
    /// </summary>
    [Theory]
    [InlineData("smoke.journey.json")]
    [InlineData("target-demo.journey.json")]
    public void Journey_SampleAtEnd_IsRestScene(string journeyFile)
    {
        var tracks = 0;
        var walks = 0;
        foreach (var (t, controller) in Run(journeyFile))
        {
            var rest = Scene.FromState(controller.Session.Current, Shown, null, t.Facings);
            Assert.Equal(rest.Poses, t.Sample(t.SpanMs));
            Assert.All(t.Sample(t.SpanMs), p =>
            {
                Assert.Equal((0, 0), (p.SubX, p.SubY));
                Assert.Null(p.Height);
            });
            Assert.All(new[] { 0, t.SpanMs / 4, t.SpanMs / 2, t.SpanMs * 3 / 4 }.SelectMany(ms => t.Sample(ms)), p =>
            {
                Assert.InRange(p.SubX, -8, 8);
                Assert.InRange(p.SubY, -8, 8);
                Assert.True(t.Scene.Columns.ContainsKey(p.Bag), "sampled pose in a shown bag");
            });
            tracks += t.Tracks.Length;
            walks += t.Tracks.Count(k => k.Entity is EntityKey.Body && k.Curve != Curve.PingPong);
        }

        Assert.True(tracks > 0, "the journey produced no tracks");
        if (journeyFile.StartsWith("target-demo", StringComparison.Ordinal))
            Assert.True(walks > 0, "target-demo never walked the body");
    }

    /// <summary>Determinism: two runs of target-demo give an identical PoseText stream at u ∈ {0, ¼, ½, ¾, 1} of each commit.</summary>
    [Fact]
    public void TargetDemo_PoseStream_IsDeterministic()
    {
        static List<string> Stream()
        {
            var text = new PoseText();
            return Run("target-demo.journey.json")
                .SelectMany((r, k) => new[] { 0, r.Timeline.SpanMs / 4, r.Timeline.SpanMs / 2, r.Timeline.SpanMs * 3 / 4, r.Timeline.SpanMs }
                    .SelectMany(ms => r.Timeline.Sample(ms).Select(p => $"{k} {ms} {text.Line(p)}")))
                .ToList();
        }

        var a = Stream();
        Assert.Equal(a, Stream());
    }
}
