using System.Collections.Immutable;
using Pockets.Core.Fine;
using Pockets.Core.Models;
using Pockets.Core.Sim;

namespace Pockets.Realization.Tests;

/// <summary>
/// Slice 2.7 in Realization. The D2-B integrator traced in zoomed ASCII (1 cell → 4×4) through the real session shell:
/// a held E crosses a cell, a tree refuses and the body clamps, a diagonal crosses X then Y and clamps in the corner, the
/// outer edge clamps with no Move, release settles, and the nudge is capped. Then the pose side (P2-D13): a free body's
/// pose is its cell plus (Px, Py), an integrator-derived CursorMoved adds no walk track and does not snap, an explicit
/// fine commit never snaps, and the 8-way input quantizer Godot uses.
/// </summary>
public class FreeMoveTests
{
    private static readonly ItemType TreeType = new("Tree", Category.Material, IsStackable: false);
    private static readonly ItemType Vase = new("Vase", Category.Material, IsStackable: false);

    /// <summary>
    /// A realtime, Free, Forest 3×2 world, cursor at (<paramref name="row"/>, <paramref name="col"/>):
    /// <code>
    /// . . T
    /// . V .
    /// </code>
    /// </summary>
    private static GameController World(int row = 0, int col = 0)
    {
        var grid = Grid.Create(3, 2)
            .SetCell(2, new Cell(new ItemStack(TreeType, 1), Frame: new TreeFrame()))
            .SetCell(4, new Cell(new ItemStack(Vase, 1)));
        var root = new Bag(grid, "Forest");
        var hand = GameState.CreateHandBag();
        var state = new GameState(BagStore.Empty.Add(root).Add(hand),
            LocationMap.Create(hand.Id, root.Id, new Cursor(new Position(row, col))), ImmutableArray.Create(TreeType, Vase));
        var c = new GameController(GameSession.New(state, ImmutableArray<Recipe>.Empty, TickMode.Realtime));
        c.SetMovement(LocationId.B, MovementModel.Free);
        return c;
    }

    private static Bag BagB(GameController c) => c.Session.Current.ActiveBagAt(LocationId.B);

    /// <summary>B's bag in zoomed ASCII at n = 4, with the session's fine view.</summary>
    private static string Render(GameController c) =>
        ZoomAscii.Render(Scene.FromState(c.Session.Current, [LocationId.B], FineView.Of(c.Session.Fine)).Poses, BagB(c));

    /// <summary>Line <paramref name="line"/> of a render.</summary>
    private static string Line(string render, int line) => render.Split('\n')[line];

    /// <summary>Runs <paramref name="ticks"/> fine ticks, returning each tick's render line and its derived intents.</summary>
    private static List<(string Line, List<Intent> Moves, bool Bumped)> Trace(GameController c, int ticks, int line)
    {
        var rows = new List<(string, List<Intent>, bool)>();
        for (var i = 0; i < ticks; i++)
        {
            var r = c.FineTick();
            rows.Add((Line(Render(c), line),
                r.Events.OfType<SimEvent.IntentApplied>().Select(e => e.Intent).ToList(),
                r.Events.Any(e => e is SimEvent.Bumped)));
        }
        return rows;
    }

    private static Intent Move(Direction d) => new Intent.Move(LocationId.B, d);

    // ==================== Integrator traces (zoomed ASCII) ====================

    [Fact]
    public void HeldEast_CrossesAtTickThree_ThenTheTreeRefusesAtTickSeven()
    {
        var c = World();
        Assert.Equal("..@.|....|..T.", Line(Render(c), 2));
        c.Steer(LocationId.B, Direction8.E);

        var trace = Trace(c, 8, line: 2);

        Assert.Equal(new[]
        {
            "...@|....|..T.", // 1: Px 4
            "....|@...|..T.", // 2: Px 8, on the boundary
            "....|.@..|..T.", // 3: crossed (Move Right), Px −4
            "....|..@.|..T.",
            "....|...@|..T.",
            "....|....|@.T.", // 6: Px 8, against the tree's cell
            "....|...@|..T.", // 7: refused: clamped to +7, bumped
            "....|...@|..T."  // 8: pressed, no second bump
        }, trace.Select(t => t.Line));
        Assert.Equal(new[] { 3, 7 }, trace.Select((t, i) => (t, i)).Where(x => x.t.Moves.Count > 0).Select(x => x.i + 1));
        Assert.Equal(new[] { 7 }, trace.Select((t, i) => (t, i)).Where(x => x.t.Bumped).Select(x => x.i + 1));
    }

    [Fact]
    public void Diagonal_CrossesXThenY_InOneTick_AndClampsInTheCorner()
    {
        var c = World();
        c.Steer(LocationId.B, Direction8.SE);
        var first = Trace(c, 3, line: 0);
        Assert.Equal(new[] { Move(Direction.Right), Move(Direction.Down) }, first[2].Moves);
        Assert.Equal(
            "....|....|....\n....|....|....\n....|....|..T.\n....|....|....\n" +
            "----+----+----\n" +
            "....|@...|....\n....|....|....\n....|..V.|....\n....|....|....",
            Render(c));

        var rest = Trace(c, 12, line: 0);
        Assert.Equal(new[] { Move(Direction.Right) }, rest.SelectMany(t => t.Moves)); // into the corner; Y clamps at the edge
        Assert.Equal(
            "....|....|....\n....|....|....\n....|....|..T.\n....|....|....\n" +
            "----+----+----\n" +
            "....|....|....\n....|....|....\n....|....|....\n....|...V|...@", // the vase nudged (+6, +6)
            Render(c));
        var body = c.Session.Fine.BodyAt(LocationId.B, BagB(c).Id);
        Assert.Equal((7, 7, 0, 0), (body.Px, body.Py, body.Vx, body.Vy));
    }

    [Fact]
    public void OuterEdge_Clamps_WithNoMove()
    {
        var c = World(row: 1, col: 0);
        c.Steer(LocationId.B, Direction8.W);
        var trace = Trace(c, 4, line: 7);
        Assert.Equal(new[] { ".@..|..V.|....", "@...|..V.|....", "@...|..V.|....", "@...|..V.|...." }, trace.Select(t => t.Line));
        Assert.All(trace, t => Assert.Empty(t.Moves));
        Assert.Equal(new Position(1, 0), c.Session.Current.Locations.Get(LocationId.B).Cursor.Position);
    }

    [Fact]
    public void Release_SettlesToTheCentre()
    {
        var c = World();
        c.Steer(LocationId.B, Direction8.E);
        Trace(c, 2, line: 2);
        c.Steer(LocationId.B, null);
        var trace = Trace(c, 3, line: 2);
        Assert.Equal(new[] { "...@|....|..T.", "..@.|....|..T.", "..@.|....|..T." }, trace.Select(t => t.Line));
        Assert.True(c.Session.Fine.IsEmpty);
    }

    [Fact]
    public void WalkingThroughTheVase_NudgesIt_CappedAtSix()
    {
        var c = World(row: 1, col: 0);
        var vase = new CellRef(BagB(c).Id, 4);
        c.SetFineOffset(LocationId.B, 4, 4, 0);
        c.Steer(LocationId.B, Direction8.E);
        var trace = Trace(c, 6, line: 7);

        Assert.Equal((6, 0), c.Session.Fine.OffsetAt(vase));
        // +4 preset, +1 at tick 2 (the body on the boundary overlaps the vase's cell by half), +1 at tick 3, then capped.
        Assert.Equal("....|...V|@...", trace[^1].Line);
        Assert.Equal((6, 0), c.Session.Fine.OffsetAt(vase));
    }

    // ==================== Poses (P2-D13) ====================

    [Fact]
    public void Scene_MarksASteeredFreeBody_AsDriven()
    {
        var c = World();
        Assert.Empty(Scene.FromState(c.Session.Current, [LocationId.B], FineView.Of(c.Session.Fine)).Driven);
        c.Steer(LocationId.B, Direction8.E);
        var scene = Scene.FromState(c.Session.Current, [LocationId.B], FineView.Of(c.Session.Fine));
        Assert.Equal(new[] { LocationId.B }, scene.Driven);
        Assert.True(FineView.Of(c.Session.Fine).IsDriven(LocationId.B, BagB(c).Id));
        Assert.False(FineView.Empty.IsDriven(LocationId.B, BagB(c).Id));
    }

    [Fact]
    public void ADerivedMove_AddsNoBodyTrack_AndDoesNotSnapAPop()
    {
        var c = World();
        var stream = new PoseStream(c.Session.Current, new BagNames(), null, FineView.Of(c.Session.Fine));
        void Commit(ControllerResult r) => stream.Commit(r.Events, c.Session.Current, 0, FineView.Of(c.Session.Fine));

        Commit(c.Steer(LocationId.B, Direction8.E));
        Commit(c.FineTick());
        Commit(c.FineTick());
        Commit(c.HandleKey(GameKey.AcquireRandom));  // input: a pop track
        var pop = Assert.Single(stream.Timeline.Tracks);

        var crossing = c.FineTick();
        Assert.Contains(crossing.Events, e => e is SimEvent.CursorMoved);
        Commit(crossing);

        Assert.Equal(new[] { pop.Entity }, stream.Timeline.Tracks.Select(t => t.Entity)); // kept, no walk added
        var body = stream.Timeline.Scene.Find(new EntityKey.Body(LocationId.B))!.Value;
        Assert.Equal((1, 0, -4, 0, Facing.E), (body.CellCol, body.CellRow, body.SubX, body.SubY, body.Facing));
    }

    [Fact]
    public void ADerivedBump_AddsNoLunge()
    {
        var c = World();
        var stream = new PoseStream(c.Session.Current, new BagNames(), null, FineView.Of(c.Session.Fine));
        c.Steer(LocationId.B, Direction8.E);
        var bumped = false;
        for (var i = 0; i < 7; i++)
        {
            var r = c.FineTick();
            bumped |= r.Events.Any(e => e is SimEvent.Bumped);
            stream.Commit(r.Events, c.Session.Current, 0, FineView.Of(c.Session.Fine));
            Assert.Empty(stream.Timeline.Tracks);
        }
        Assert.True(bumped);
    }

    [Fact]
    public void AKeyMove_OfAnUndrivenBodyInAFreeBag_StillWalks()
    {
        var c = World();
        var stream = new PoseStream(c.Session.Current, new BagNames(), null, FineView.Of(c.Session.Fine));
        var r = c.HandleKey(GameKey.Right);
        stream.Commit(r.Events, c.Session.Current, 0, FineView.Of(c.Session.Fine));
        Assert.Contains(stream.Timeline.Tracks, t => t.Entity is EntityKey.Body);
    }

    [Fact]
    public void IsClock_TreatsADrivenMoveSpanAsNonSnapping()
    {
        var bag = Guid.NewGuid();
        var events = new SimEvent[]
        {
            new SimEvent.IntentApplied(1, 0, new Intent.Move(LocationId.B, Direction.Right)),
            new SimEvent.CursorMoved(1, 1, LocationId.B, bag, new Position(0, 0), new Position(0, 1))
        };
        Assert.False(Choreography.IsClock(events));
        Assert.True(Choreography.IsClock(events, ImmutableHashSet.Create(LocationId.B)));
        Assert.False(Choreography.IsClock(events, ImmutableHashSet.Create(LocationId.C)));
    }

    [Fact]
    public void AFineCommit_WithNoEvents_DoesNotSnap()
    {
        var c = World();
        var policy = RealizationPolicy.Default;
        var t = Timeline.Rest(Scene.FromState(c.Session.Current, PoseStream.Shown, FineView.Empty));
        var r = c.HandleKey(GameKey.AcquireRandom);
        var scene = Scene.FromState(c.Session.Current, PoseStream.Shown, FineView.Empty);
        t = t.Commit(r.Events, scene, policy);
        Assert.Single(t.Tracks);

        Assert.Single(t.Commit(Array.Empty<SimEvent>(), scene, policy, 0, fineTick: true).Tracks);
        Assert.Empty(t.Commit(Array.Empty<SimEvent>(), scene, policy).Tracks); // an input commit snaps (D7)
    }

    [Fact]
    public void PosePlayer_FineCommit_KeepsTheTrack()
    {
        var c = World();
        var player = new PosePlayer(c.Session.Current, FineView.Of(c.Session.Fine));
        var r = c.HandleKey(GameKey.AcquireRandom);
        player.Commit(r.Events, c.Session.Current, 0, FineView.Of(c.Session.Fine));
        player.Advance(40);
        c.Steer(LocationId.B, Direction8.S);
        var tick = c.FineTick();
        player.Commit(tick.Events, c.Session.Current, 0, FineView.Of(c.Session.Fine), fineTick: true);
        Assert.Single(player.Timeline.Tracks);
    }

    [Fact]
    public void IsWorldBag_AgreesWithTheIntegrator()
    {
        foreach (var bag in new[]
                 {
                     new Bag(Grid.Create(2, 2)), new Bag(Grid.Create(2, 2), "Forest"), new Bag(Grid.Create(2, 2), "Cave"),
                     new Bag(Grid.Create(2, 2), "Quiet 1") { EnterOnly = true }, new Bag(Grid.Create(2, 2), "Quiet 1")
                 })
            Assert.Equal(Scene.IsWorldBag(bag), Integrator.IsWorldBag(bag));
    }

    // ==================== Input quantization (Godot) ====================

    [Theory]
    [InlineData(false, false, false, false, null)]
    [InlineData(true, false, false, false, Direction8.N)]
    [InlineData(false, true, false, true, Direction8.SE)]
    [InlineData(true, false, true, false, Direction8.NW)]
    [InlineData(true, true, false, true, Direction8.E)]   // up + down cancel
    [InlineData(true, true, true, true, null)]
    public void FromArrows_IsEightWay(bool up, bool down, bool left, bool right, Direction8? expected)
    {
        Assert.Equal(expected, SteerInput.FromArrows(up, down, left, right));
    }

    [Theory]
    [InlineData(0, 0, null)]
    [InlineData(100, 50, null)]               // inside the dead zone (200)
    [InlineData(1000, 0, Direction8.E)]
    [InlineData(1000, 400, Direction8.E)]     // under tan 22.5° ≈ 0.414
    [InlineData(1000, 430, Direction8.SE)]
    [InlineData(-700, -700, Direction8.NW)]
    [InlineData(300, -1000, Direction8.N)]
    [InlineData(-1000, 0, Direction8.W)]
    public void FromStick_QuantizesToEightWays_WithADeadZone(int x, int y, Direction8? expected)
    {
        Assert.Equal(expected, SteerInput.FromStick(x, y, deadZone: 200));
    }
}
