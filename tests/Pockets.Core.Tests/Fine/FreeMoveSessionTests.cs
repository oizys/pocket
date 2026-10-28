using System.Collections.Immutable;
using Pockets.Core.Fine;
using Pockets.Core.Models;
using Pockets.Core.Rendering;
using Pockets.Core.Sim;

namespace Pockets.Core.Tests.Fine;

/// <summary>
/// Slice 2.7 in the shell (P2-D7, P2-D8, P2-D12): the per-bag flag set by the shell-handled <see cref="Intent.SetMovement"/>,
/// the held direction set by <see cref="Intent.Steer"/>, and the 8 Hz <see cref="Intent.FineTick"/> that integrates every
/// free body and turns a cell crossing into an ordinary <see cref="Intent.Move"/> run through Step (A1). Step never sees
/// the three; a derived Move is a normal step with normal events and is not logged separately (the FineTick is).
/// </summary>
public class FreeMoveSessionTests
{
    private static readonly ItemType Rock = new("Rock", Category.Material, IsStackable: true, MaxStackSize: 20);

    /// <summary>
    /// A realtime Forest root, 4×3 with a rock at cell 5 = (1, 1) and a tree at cell 6 = (1, 2), cursor at
    /// <paramref name="row"/>, <paramref name="col"/>:
    /// <code>
    /// . . . .
    /// . R T .
    /// . . . .
    /// </code>
    /// </summary>
    private static GameState MakeWorld(int row = 0, int col = 0)
    {
        var grid = Grid.Create(4, 3)
            .SetCell(5, new Cell(new ItemStack(Rock, 3)))
            .SetCell(6, new Cell(new ItemStack(Rock, 1), Frame: new TreeFrame()));
        var root = new Bag(grid, "Forest");
        var hand = GameState.CreateHandBag();
        return new GameState(BagStore.Empty.Add(root).Add(hand),
            LocationMap.Create(hand.Id, root.Id, new Cursor(new Position(row, col))), ImmutableArray.Create(Rock));
    }

    private static GameSession NewSession(TickMode mode = TickMode.Realtime, int row = 0, int col = 0) =>
        GameSession.New(MakeWorld(row, col), ImmutableArray<Recipe>.Empty, mode);

    /// <summary>A session whose B bag is Free, cursor at (row, col).</summary>
    private static GameSession Free(TickMode mode = TickMode.Realtime, int row = 0, int col = 0) =>
        NewSession(mode, row, col).Apply(SetFree);

    private static GameSession Run(GameSession session, params Intent[] intents) =>
        intents.Aggregate(session, (s, i) => s.Apply(i));

    private static GameSession Ticks(GameSession s, int n) =>
        Enumerable.Range(0, n).Aggregate(s, (x, _) => x.Apply(Tick));

    private static readonly Intent SetFree = new Intent.SetMovement(LocationId.B, MovementModel.Free);
    private static readonly Intent Tick = new Intent.FineTick();
    private static Intent Steer(Direction8? d) => new Intent.Steer(LocationId.B, d);

    private static Guid BagB(GameSession s) => s.Current.ActiveBagAt(LocationId.B).Id;
    private static FineBody Body(GameSession s) => s.Fine.BodyAt(LocationId.B, BagB(s));
    private static Position Cursor(GameSession s) => s.Current.Locations.Get(LocationId.B).Cursor.Position;

    // ==================== The flag ====================

    [Fact]
    public void SetMovement_SetsTheFlagOnTheActiveBag_WithoutAStep()
    {
        var s = NewSession();
        var (next, trace, events) = s.ApplyTraced(SetFree);

        Assert.Equal(MovementModel.Free, next.Current.ActiveBagAt(LocationId.B).Movement);
        Assert.Null(trace);
        Assert.Empty(events);
        Assert.Equal(s.TickCount, next.TickCount);
        Assert.Same(s.EventLog, next.EventLog);
        Assert.Equal(s.UndoDepth, next.UndoDepth);
        Assert.Equal(s.ActionLog, next.ActionLog);
        Assert.Equal(new[] { (1, SetFree) }, next.IntentLog);
        Assert.Same(next, next.Apply(SetFree)); // unchanged: not logged
    }

    [Fact]
    public void SetMovement_IsNotInTheViewModel()
    {
        var s = NewSession();
        Assert.Equal(ViewModelSerializer.Serialize(s).ToJsonString(), ViewModelSerializer.Serialize(s.Apply(SetFree)).ToJsonString());
    }

    [Fact]
    public void IsFree_NeedsRealtime_AWorldBag_AndTheFlag()
    {
        Assert.False(NewSession().IsFree(LocationId.B));
        Assert.True(Free().IsFree(LocationId.B));
        Assert.False(Free(TickMode.Rogue).IsFree(LocationId.B));
        Assert.False(Free().IsFree(LocationId.C)); // no such location
    }

    [Fact]
    public void Step_RejectsTheThreeShellHandledIntents()
    {
        var s = NewSession();
        foreach (var intent in new[] { SetFree, Tick, Steer(Direction8.E) })
            Assert.Throws<InvalidOperationException>(() => Simulation.Step(s.ToSimState(), new[] { intent }));
    }

    // ==================== Steer ====================

    [Fact]
    public void Steer_SetsHeld_LoggedOnlyOnChange_NoStep()
    {
        var s = Free();
        var (next, trace, events) = s.ApplyTraced(Steer(Direction8.E));

        Assert.Equal(new FineBody(0, 0, 0, 0, Direction8.E), Body(next));
        Assert.Null(trace);
        Assert.Empty(events);
        Assert.Same(s.Current, next.Current);
        Assert.Equal(s.IntentLog.Count + 1, next.IntentLog.Count);
        Assert.Same(next, next.Apply(Steer(Direction8.E)));
    }

    [Fact]
    public void Steer_InAGridLockedBag_OrRogue_IsANoOp()
    {
        var locked = NewSession();
        Assert.Same(locked, locked.Apply(Steer(Direction8.E)));
        var rogue = Free(TickMode.Rogue);
        Assert.Same(rogue, rogue.Apply(Steer(Direction8.E)));
    }

    [Fact]
    public void Release_OfAnUnmovedBody_RemovesIt()
    {
        var s = Run(Free(), Steer(Direction8.E), Steer(null));
        Assert.True(s.Fine.IsEmpty);
    }

    // ==================== FineTick ====================

    [Fact]
    public void IdleFineTick_IsANoOp_AndNotLogged()
    {
        var s = Free();
        Assert.Same(s, s.Apply(Tick));
        Assert.False(s.NeedsFineTick);
        var locked = NewSession();
        Assert.Same(locked, locked.Apply(Tick));
    }

    [Fact]
    public void HeldEast_GlidesAndCrosses_AsAnOrdinaryMove()
    {
        var s = Run(Free(), Steer(Direction8.E));
        Assert.True(s.NeedsFineTick);
        s = Ticks(s, 2);
        Assert.Equal((8, 0), (Body(s).Px, Body(s).Py));
        Assert.Equal(new Position(0, 0), Cursor(s));

        var (crossed, _, events) = s.ApplyTraced(Tick);

        Assert.Equal(new Position(0, 1), Cursor(crossed));
        Assert.Equal(-4, Body(crossed).Px); // not re-anchored by the derived CursorMoved
        Assert.Contains(events, e => e is SimEvent.IntentApplied { Intent: Intent.Move { At: LocationId.B, Dir: Direction.Right } });
        Assert.Contains(events, e => e is SimEvent.CursorMoved { Loc: LocationId.B });
        Assert.Equal(Tick, crossed.IntentLog[^1].Intent); // logged as the FineTick, not the Move
        Assert.DoesNotContain(crossed.IntentLog, e => e.Intent is Intent.Move);
        Assert.Equal(s.UndoDepth, crossed.UndoDepth);
        Assert.Equal(s.TickCount, crossed.TickCount); // realtime: a Move applies with no time advance
        Assert.Equal(s.EventLog.End + events.Length, crossed.EventLog.End);
    }

    [Fact]
    public void HeldEast_ReachesTheOuterEdge_AndClamps_WithNoWrap()
    {
        var s = Ticks(Run(Free(), Steer(Direction8.E)), 20);
        Assert.Equal(new Position(0, 3), Cursor(s));
        Assert.Equal((7, 0), (Body(s).Px, Body(s).Vx));
        var still = s.Apply(Tick);
        Assert.Same(s, still); // pressed against the edge: nothing changes, nothing logged
    }

    [Fact]
    public void ATree_RefusesTheCrossing_ClampsAndBumpsOnce()
    {
        // From (1, 0) held E: into the rock cell (1, 1) at tick 3, then the tree at (1, 2) refuses at tick 7.
        var s = Run(Free(row: 1, col: 0), Steer(Direction8.E));
        var all = new List<SimEvent>();
        for (var i = 0; i < 12; i++)
        {
            var (next, _, events) = s.ApplyTraced(Tick);
            all.AddRange(events);
            s = next;
        }
        Assert.Equal(new Position(1, 1), Cursor(s));
        Assert.Equal((7, 0, Direction8.E), (Body(s).Px, Body(s).Vx, Body(s).Held));
        Assert.Single(all.OfType<SimEvent.Bumped>(), b => b.Reason == BumpReason.Unnavigable && b.Dir == Direction.Right);
        Assert.Equal(1, s.Current.Dialogue.TreeBumpCount);
    }

    [Fact]
    public void Diagonal_CrossesXThenY_InOneTick()
    {
        var s = Ticks(Run(Free(), Steer(Direction8.SE)), 2);
        var (next, _, events) = s.ApplyTraced(Tick);
        var moves = events.OfType<SimEvent.IntentApplied>().Select(e => e.Intent).ToList();
        Assert.Equal(new Intent[] { new Intent.Move(LocationId.B, Direction.Right), new Intent.Move(LocationId.B, Direction.Down) }, moves);
        Assert.Equal(new Position(1, 1), Cursor(next));
        Assert.Equal((-7, -7), (Body(next).Px, Body(next).Py));
    }

    [Fact]
    public void Release_SettlesToTheCellCentre()
    {
        var s = Ticks(Run(Free(), Steer(Direction8.E)), 2); // Px 8
        s = Ticks(s.Apply(Steer(null)), 1);
        Assert.Equal(4, Body(s).Px);
        s = Ticks(s, 2);
        Assert.True(s.Fine.IsEmpty);
        Assert.False(s.NeedsFineTick);
        Assert.Equal(new Position(0, 0), Cursor(s));
    }

    [Fact]
    public void WalkingThroughAStack_NudgesIt_AndAnL1MoveOfItClearsIt()
    {
        // From (1, 0) held E: at tick 2 the body (Px 8) overlaps the rock cell by half, from tick 3 it is in it, and at
        // tick 7 the tree refuses it, so it stops pushing (v = 0).
        var s = Ticks(Run(Free(row: 1, col: 0), Steer(Direction8.E)), 6);
        var rock = new CellRef(BagB(s), 5);
        Assert.Equal(new Position(1, 1), Cursor(s));
        Assert.Equal((5, 0), s.Fine.OffsetAt(rock));
        s = Ticks(s, 6);
        Assert.Equal((5, 0), s.Fine.OffsetAt(rock));

        var grabbed = Run(s, Steer(null), new Intent.Grab(LocationId.B));
        Assert.Equal((0, 0), grabbed.Fine.OffsetAt(rock));
    }

    [Fact]
    public void ATuiMove_InAFreeBag_IsD2A_AndReanchors()
    {
        var s = Ticks(Run(Free(), Steer(Direction8.S)), 1); // Py 4
        var moved = s.Apply(new Intent.Move(LocationId.B, Direction.Right));
        Assert.Equal(new Position(0, 1), Cursor(moved));
        Assert.Equal((0, 0), (Body(moved).Px, Body(moved).Py));
    }

    [Fact]
    public void Rogue_WithTheFlagOn_BehavesExactlyLikeD2A()
    {
        var keys = new Intent[]
        {
            new Intent.Move(LocationId.B, Direction.Down), new Intent.Move(LocationId.B, Direction.Right),
            new Intent.Move(LocationId.B, Direction.Right), new Intent.Move(LocationId.B, Direction.Left)
        };
        var locked = Run(NewSession(TickMode.Rogue), keys);
        var free = Run(Free(TickMode.Rogue), keys.Concat(new[] { Steer(Direction8.E), Tick, Tick, Tick }).ToArray());

        Assert.Equal(ViewModelSerializer.Serialize(locked).ToJsonString(), ViewModelSerializer.Serialize(free).ToJsonString());
        Assert.Equal(locked.EventLog.End, free.EventLog.End);
        Assert.True(free.Fine.IsEmpty);
    }

    [Fact]
    public void FineTick_IsInertWhileADialogueBeatShows()
    {
        var s = Run(Free(), Steer(Direction8.E));
        var shown = s with { Current = s.Current with { Dialogue = s.Current.Dialogue with { Queue = ImmutableList.Create("beat") } } };
        Assert.True(shown.Current.Dialogue.IsActive);
        Assert.Same(shown, shown.Apply(Tick));
    }

    [Fact]
    public void FineTick_DropsABodyWhoseBagIsNoLongerFree()
    {
        var s = Ticks(Run(Free(), Steer(Direction8.E)), 1);
        var locked = s.Apply(new Intent.SetMovement(LocationId.B, MovementModel.GridLocked));
        Assert.False(locked.Fine.IsEmpty);
        var ticked = locked.Apply(Tick);
        Assert.True(ticked.Fine.IsEmpty);
        Assert.Equal(new Position(0, 0), Cursor(ticked));
    }

    // ==================== Replay, undo ====================

    [Fact]
    public void Replay_OfTheIntentLog_RebuildsL1AndFine()
    {
        var start = MakeWorld(row: 1, col: 0); // one start state: replay needs the same bag ids
        GameSession Fresh() => GameSession.New(start, ImmutableArray<Recipe>.Empty, TickMode.Realtime);
        var live = Ticks(Run(Fresh().Apply(SetFree), Steer(Direction8.E)), 9);
        live = Ticks(Run(live, Steer(Direction8.SE)), 5);
        live = Ticks(Run(live, Steer(null)), 4);

        var replayed = ReplayCheck.Replay(Fresh(), live.IntentLog).Last().Session;

        Assert.Equal(live.Fine, replayed.Fine);
        Assert.Equal(ViewModelSerializer.Serialize(live).ToJsonString(), ViewModelSerializer.Serialize(replayed).ToJsonString());
        Assert.Equal(live.EventLog.End, replayed.EventLog.End);
    }

    [Fact]
    public void Undo_RestoresTheFineStateAtTheLastL1Snapshot()
    {
        var s = Ticks(Run(Free(), Steer(Direction8.E)), 1); // Px 4 at cell (0, 0)
        var marked = s.Fine;
        s = s.Apply(new Intent.AcquireRandom(LocationId.B)); // pushes a snapshot carrying `marked`
        s = Ticks(s, 4);                                      // crossed into (0, 1)
        var undone = s.Apply(new Intent.Undo());
        Assert.Equal(marked, undone.Fine);
    }

    // ==================== The controller's clock ====================

    [Fact]
    public void AdvanceClock_DrivesFineTicks_FromTheSameAccumulatorAsTick()
    {
        var c = new GameController(Run(Free(), Steer(Direction8.E)));
        c.AdvanceClock(TimeSpan.FromMilliseconds(250));
        Assert.Equal(8, Body(c.Session).Px);
        c.AdvanceClock(TimeSpan.FromMilliseconds(100));   // 350: no new 125 ms boundary
        Assert.Equal(8, Body(c.Session).Px);
        var result = c.AdvanceClock(TimeSpan.FromMilliseconds(25)); // 375: the third boundary
        Assert.Equal(new Position(0, 1), Cursor(c.Session));
        Assert.Contains(result.Events, e => e is SimEvent.CursorMoved);
    }

    [Fact]
    public void Controller_SteerFineTickAndSetMovement_SendTheIntents()
    {
        var c = new GameController(NewSession());
        c.SetMovement(LocationId.B, MovementModel.Free);
        c.Steer(LocationId.B, Direction8.E);
        c.FineTick();
        Assert.Equal(4, Body(c.Session).Px);
        Assert.Equal(new Intent[] { SetFree, Steer(Direction8.E), Tick }, c.Session.IntentLog.Select(e => e.Intent));
    }
}
