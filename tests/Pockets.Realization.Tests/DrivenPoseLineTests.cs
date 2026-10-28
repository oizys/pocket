using System.Collections.Immutable;
using Pockets.Core.Models;
using Pockets.Core.Sim;

namespace Pockets.Realization.Tests;

/// <summary>
/// Slice 2.8, pinning the glide: a D2-B body has no tracks (2.7), so the sampled lines never show its sub-cell walk. A
/// <b>driven</b> commit — one where a shown body that is driven before or after the commit moved — appends that body's
/// rest pose as one <c>fine &lt;PoseText&gt;</c> line. Every other commit is unchanged, so <c>target-demo.poses</c>
/// stays byte-identical (no body is ever driven there).
/// </summary>
public class DrivenPoseLineTests
{
    private static readonly ItemType TreeType = new("Tree", Category.Material, IsStackable: false);

    private static readonly ItemType Vase = new("Vase", Category.Material, IsStackable: false);

    /// <summary>
    /// A realtime Forest 4×1 world, cursor at (0, 0), with a tree at (0, 3) and, when <paramref name="vase"/>, a vase at
    /// (0, 1); Free unless <paramref name="free"/> is false.
    /// </summary>
    private static GameController World(bool free = true, bool vase = false)
    {
        var grid = Grid.Create(4, 1).SetCell(3, new Cell(new ItemStack(TreeType, 1), Frame: new TreeFrame()));
        if (vase) grid = grid.SetCell(1, new Cell(new ItemStack(Vase, 1)));
        var root = new Bag(grid, "Forest");
        var hand = GameState.CreateHandBag();
        var state = new GameState(BagStore.Empty.Add(root).Add(hand),
            LocationMap.Create(hand.Id, root.Id, new Cursor(new Position(0, 0))), ImmutableArray.Create(TreeType, Vase));
        var c = new GameController(GameSession.New(state, ImmutableArray<Recipe>.Empty, TickMode.Realtime));
        if (free) c.SetMovement(LocationId.B, MovementModel.Free);
        return c;
    }

    /// <summary>A pose stream following <paramref name="c"/>, and a commit function that applies one controller call.</summary>
    private static Func<Func<GameController, ControllerResult>, ImmutableArray<string>> Stream(GameController c)
    {
        var text = new EventText();
        var stream = new PoseStream(c.Session.Current, text.Names, null, FineView.Of(c.Session.Fine));
        return act =>
        {
            var r = act(c);
            foreach (var e in r.Events) text.Line(e);
            return stream.Commit(r.Events, c.Session.Current, 0, FineView.Of(c.Session.Fine));
        };
    }

    private static IEnumerable<string> FineLines(ImmutableArray<string> lines) => lines.Where(l => l.StartsWith("fine ", StringComparison.Ordinal));

    [Fact]
    public void ASteer_ThatMovesNothing_AddsNoFineLine()
    {
        var commit = Stream(World());
        Assert.Empty(FineLines(commit(c => c.Steer(LocationId.B, Direction8.E))));
    }

    [Fact]
    public void EachDrivenTick_AddsTheBodysRestPose_AcrossTheCrossing()
    {
        var c = World();
        var commit = Stream(c);
        commit(x => x.Steer(LocationId.B, Direction8.E));

        var ticks = Enumerable.Range(0, 3).Select(_ => commit(x => x.FineTick())).ToList();

        Assert.Equal(new[]
        {
            "fine entity=Body(B) bag=bag#1 col=0 row=0 subX=4 subY=0 height=- facing=S",
            "fine entity=Body(B) bag=bag#1 col=0 row=0 subX=8 subY=0 height=- facing=S",
            "fine entity=Body(B) bag=bag#1 col=1 row=0 subX=-4 subY=0 height=- facing=E",
        }, ticks.SelectMany(FineLines));
        // The fine line comes after any sampled lines of the same commit, and there is one per driven body.
        Assert.All(ticks, t => Assert.Equal("fine ", t[^1][..5]));
    }

    [Fact]
    public void Settling_PinsEveryStepDownToTheCentre_ThenStops()
    {
        var c = World();
        var commit = Stream(c);
        commit(x => x.Steer(LocationId.B, Direction8.E));
        commit(x => x.FineTick()); // Px 4
        commit(x => x.FineTick()); // Px 8
        commit(x => x.Steer(LocationId.B, null));

        var settle = Enumerable.Range(0, 3).Select(_ => commit(x => x.FineTick())).ToList();

        Assert.Equal(new[]
        {
            "fine entity=Body(B) bag=bag#1 col=0 row=0 subX=4 subY=0 height=- facing=S",
            "fine entity=Body(B) bag=bag#1 col=0 row=0 subX=0 subY=0 height=- facing=S",
        }, settle.SelectMany(FineLines));
        Assert.Empty(FineLines(settle[2])); // at rest and removed: no longer driven, nothing moved
    }

    [Fact]
    public void ANudge_AddsTheNudgedStacksRestPose_AfterTheBody()
    {
        var c = World(free: true, vase: true);
        var commit = Stream(c);
        commit(x => x.Steer(LocationId.B, Direction8.E));

        var lines = Enumerable.Range(0, 3).Select(_ => commit(x => x.FineTick()).ToList()).ToList();

        // Tick 2 reaches Px 8: the body overlaps the vase's cell by half and pushes it 1/16 E.
        Assert.Equal(new[]
        {
            "fine entity=Body(B) bag=bag#1 col=0 row=0 subX=8 subY=0 height=- facing=S",
            "fine entity=Stack(bag#1[1]) bag=bag#1 col=1 row=0 subX=1 subY=0 height=- facing=-",
        }, FineLines(lines[1].ToImmutableArray()));
        // Tick 3 crosses onto the vase and pushes it again.
        Assert.Equal(new[]
        {
            "fine entity=Body(B) bag=bag#1 col=1 row=0 subX=-4 subY=0 height=- facing=E",
            "fine entity=Stack(bag#1[1]) bag=bag#1 col=1 row=0 subX=2 subY=0 height=- facing=-",
        }, FineLines(lines[2].ToImmutableArray()));
    }

    [Fact]
    public void AnUndrivenKeyMove_AddsNoFineLine_EvenInAFreeBag()
    {
        var commit = Stream(World());
        var lines = commit(x => x.HandleKey(GameKey.Right));
        Assert.NotEmpty(lines); // the D2-A walk still samples its track
        Assert.Empty(FineLines(lines));
    }

    [Fact]
    public void AGridLockedBag_NeverAddsAFineLine()
    {
        var c = World(free: false);
        var commit = Stream(c);
        var all = new[] { commit(x => x.Steer(LocationId.B, Direction8.E)), commit(x => x.FineTick()), commit(x => x.HandleKey(GameKey.Right)) };
        Assert.Empty(all.SelectMany(FineLines));
    }
}
