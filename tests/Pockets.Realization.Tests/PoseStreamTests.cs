using System.Collections.Immutable;
using System.Text.Json.Nodes;
using Pockets.Core;
using Pockets.Core.Data;
using Pockets.Core.Models;
using Pockets.Core.Sim;

namespace Pockets.Realization.Tests;

/// <summary>
/// Slice 2.4: the pose lines of one commit (P2-D5 samples of the moving entities at u ∈ {0, ¼, ½, ¾, 1}) and the
/// <see cref="PoseStream"/> that keeps one timeline per run and names bags through the run's <see cref="EventText"/>.
/// </summary>
public class PoseStreamTests
{
    private static readonly ItemType Rock = new("Plain Rock", Category.Material, true);

    /// <summary>A world-ish scene: the body of B at (0, <paramref name="col"/>) and one centred stack in cell 3.</summary>
    private static Scene SceneOf(Guid bag, int col) =>
        new(ImmutableArray.Create(
            new Pose(new EntityKey.Body(LocationId.B), bag, col, 0, 0, 0, null, Facing.S),
            new Pose(new EntityKey.Stack(new CellRef(bag, 3)), bag, 3, 0, 0, 0, null, null)))
        {
            Columns = ImmutableDictionary<Guid, int>.Empty.Add(bag, 4)
        };

    /// <summary>A player walk of B from (0, <paramref name="from"/>) to (0, <paramref name="to"/>).</summary>
    private static SimEvent[] Walk(Guid bag, int from, int to) =>
    [
        new SimEvent.IntentApplied(1, 0, new Intent.Move(LocationId.B, Direction.Right)),
        new SimEvent.CursorMoved(1, 1, LocationId.B, bag, new Position(0, from), new Position(0, to))
    ];

    /// <summary>A walk gives five lines of the body only (the stack does not move), at t = SpanMs·q/4.</summary>
    [Fact]
    public void Lines_Walk_SamplesTheBodyAtQuarterPoints()
    {
        var bag = Guid.NewGuid();
        var t = Timeline.Rest(SceneOf(bag, 0)).Commit(Walk(bag, 0, 1), SceneOf(bag, 1), RealizationPolicy.Default);

        var lines = PoseStream.Lines(t, new PoseText());

        Assert.Equal(5, lines.Length);
        Assert.Equal(new[] { "u=0/4 t=0 ", "u=1/4 t=31 ", "u=2/4 t=62 ", "u=3/4 t=93 ", "u=4/4 t=125 " },
            lines.Select(l => l[..(l.IndexOf("entity=", StringComparison.Ordinal))]));
        Assert.All(lines, l => Assert.Contains("entity=Body(B) bag=bag#1", l));
        Assert.Contains("col=0 row=0 subX=0 subY=0 height=-", lines[0]);
        Assert.Contains("col=1 row=0 subX=0 subY=0 height=-", lines[4]);
    }

    /// <summary>A commit with no tracks has no moving entity, so it has no lines.</summary>
    [Fact]
    public void Lines_NoTracks_IsEmpty()
    {
        var bag = Guid.NewGuid();
        var t = Timeline.Rest(SceneOf(bag, 0)).Commit([], SceneOf(bag, 0), RealizationPolicy.Default);
        Assert.Empty(PoseStream.Lines(t, new PoseText()));
    }

    /// <summary>The policy is a sampling input: walk 125 → 126 ms changes the lines (the slice's mutation check, in small).</summary>
    [Fact]
    public void Lines_DependOnThePolicy()
    {
        var bag = Guid.NewGuid();
        var a = Timeline.Rest(SceneOf(bag, 0)).Commit(Walk(bag, 0, 1), SceneOf(bag, 1), RealizationPolicy.Default);
        var b = Timeline.Rest(SceneOf(bag, 0)).Commit(Walk(bag, 0, 1), SceneOf(bag, 1),
            RealizationPolicy.Default with { WalkMs = 126 });
        Assert.NotEqual(PoseStream.Lines(a, new PoseText()), PoseStream.Lines(b, new PoseText()));
    }

    /// <summary>
    /// The stream names bags through the run's <see cref="EventText"/>: naming the events first, then the poses, gives
    /// the pose lines the events' <c>bag#N</c>. <see cref="PoseStream.Last"/> holds the last commit's lines.
    /// </summary>
    [Fact]
    public void Stream_SharesBagNamesWithEventText_AndKeepsLast()
    {
        var controller = DemoController();
        var text = new EventText();
        var stream = new PoseStream(controller.Session.Current, text.Names);
        Assert.Empty(stream.Last);

        var dismiss = controller.HandleKey(GameKey.Primary);
        var eventLines = dismiss.Events.Select(text.Line).ToList();
        stream.Commit(dismiss.Events, controller.Session.Current, 0);

        var right = controller.HandleKey(GameKey.Right);
        eventLines.AddRange(right.Events.Select(text.Line));
        var lines = stream.Commit(right.Events, controller.Session.Current, 0);

        Assert.NotEmpty(lines);
        Assert.Equal(lines, stream.Last);
        var moved = eventLines.Last(l => l.Contains("CursorMoved", StringComparison.Ordinal));
        var bag = moved.Split(' ').First(f => f.StartsWith("bag=", StringComparison.Ordinal));
        Assert.All(lines, l => Assert.Contains(bag, l));
    }

    /// <summary>Each commit's lines are prefixed <c>{journeyStep}:{k}</c> by commit ordinal; an empty commit still counts.</summary>
    [Fact]
    public void Golden_PrefixesByJourneyStepAndCommitOrdinal()
    {
        IReadOnlyList<string>[] commits = [["u=0/4 a", "u=4/4 b"], [], ["u=0/4 c"]];
        Assert.Equal(new[] { "7:0 u=0/4 a", "7:0 u=4/4 b", "7:2 u=0/4 c" }, PoseGolden.Lines(7, commits));
    }

    /// <summary>The demo controller, as the journeys start it.</summary>
    internal static GameController DemoController()
    {
        var registry = ContentLoader.LoadFromDirectory(TestPaths.DataDir);
        var dialogue = DialogueLoader.LoadFromDirectory(TestPaths.DataDir);
        return new GameController(GameInitializer.CreateDemoProfile(registry, null, dialogue).NewSession());
    }
}
