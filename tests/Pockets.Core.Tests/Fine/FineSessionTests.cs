using System.Collections.Immutable;
using Pockets.Core.Fine;
using Pockets.Core.Models;
using Pockets.Core.Rendering;
using Pockets.Core.Sim;

namespace Pockets.Core.Tests.Fine;

/// <summary>
/// Slice 2.6, the shell plumbing (P2-D9): <see cref="GameSession.Fine"/> is session state outside L1. The shell-handled
/// <see cref="Intent.SetFineOffset"/> (P2-D11, test-only) sets an offset without a Step; every Step is reconciled;
/// undo snapshots and realtime checkpoints carry the fine state, and replay of the intent log rebuilds it. The VM never
/// shows it.
/// </summary>
public class FineSessionTests
{
    private static readonly ItemType Rock = new("Rock", Category.Material, IsStackable: true, MaxStackSize: 20);

    /// <summary>A 4×2 root with rocks at cells 1–3 (5, 7, 9); cursor at (0,0).</summary>
    private static GameState MakeState()
    {
        var grid = Grid.Create(4, 2)
            .SetCell(1, new Cell(new ItemStack(Rock, 5)))
            .SetCell(2, new Cell(new ItemStack(Rock, 7)))
            .SetCell(3, new Cell(new ItemStack(Rock, 9)));
        var root = new Bag(grid);
        var hand = GameState.CreateHandBag();
        return new GameState(BagStore.Empty.Add(root).Add(hand), LocationMap.Create(hand.Id, root.Id), ImmutableArray.Create(Rock));
    }

    private static GameSession NewSession(TickMode mode = TickMode.Rogue, EventLogPolicy? policy = null) =>
        GameSession.New(MakeState(), ImmutableArray<Recipe>.Empty, mode) with { LogPolicy = policy ?? EventLogPolicy.Default };

    private static GameSession Run(GameSession session, params Intent[] intents) =>
        intents.Aggregate(session, (s, i) => s.Apply(i));

    private static CellRef Root(GameSession s, int index) => new(s.Current.ActiveBagAt(LocationId.B).Id, index);

    private static Intent Offset(int index, int dx, int dy) => new Intent.SetFineOffset(LocationId.B, index, dx, dy);

    private static readonly Intent Right = new Intent.Move(LocationId.B, Direction.Right);
    private static readonly Intent Grab = new Intent.Grab(LocationId.B);
    private static readonly Intent Drop = new Intent.Drop(LocationId.B);
    private static readonly Intent Undo = new Intent.Undo();

    [Fact]
    public void NewSession_HasEmptyFineState()
    {
        var s = NewSession();
        Assert.Same(FineState.Empty, s.Fine);
        Assert.Empty(s.FineMarks);
        Assert.Empty(s.FineCheckpoints);
    }

    [Fact]
    public void SetFineOffset_SetsTheOffset_WithoutAStep()
    {
        var s = NewSession();
        var (next, trace, events) = s.ApplyTraced(Offset(2, 3, -2));

        Assert.Equal((3, -2), next.Fine.OffsetAt(Root(s, 2)));
        Assert.Null(trace);
        Assert.Empty(events);
        Assert.Equal(s.TickCount, next.TickCount);
        Assert.Same(s.Current, next.Current);
        Assert.Same(s.EventLog, next.EventLog);
        Assert.Equal(s.UndoDepth, next.UndoDepth);
        Assert.Equal(s.ActionLog, next.ActionLog);
    }

    [Fact]
    public void SetFineOffset_IsLogged_WhenItChangesTheFineState_Only()
    {
        var s = Run(NewSession(), Offset(2, 3, -2));
        Assert.Equal(new[] { (1, Offset(2, 3, -2)) }, s.IntentLog);

        Assert.Same(s, s.Apply(Offset(2, 3, -2)));  // same value: no change, not logged
        Assert.Same(s, s.Apply(Offset(0, 1, 1)));   // empty cell: no change
    }

    [Fact]
    public void SetFineOffset_IsNotInTheViewModel()
    {
        var s = NewSession();
        var set = Run(s, Offset(1, 4, 4), Offset(3, -6, 2));
        Assert.Equal(ViewModelSerializer.Serialize(s).ToJsonString(), ViewModelSerializer.Serialize(set).ToJsonString());
    }

    [Fact]
    public void Step_RejectsTheShellHandledIntent()
    {
        var s = NewSession();
        Assert.Throws<InvalidOperationException>(() => Simulation.Step(s.ToSimState(), new[] { Offset(1, 1, 1) }));
    }

    [Fact]
    public void AnL1MoveOfTheStack_ClearsItsOffset()
    {
        var s = Run(NewSession(), Offset(1, 2, 2), Offset(2, 1, 1), Right);
        Assert.Equal((2, 2), s.Fine.OffsetAt(Root(s, 1))); // a cursor move touches no stack

        var grabbed = s.Apply(Grab); // cell 1 → hand
        Assert.Equal((0, 0), grabbed.Fine.OffsetAt(Root(s, 1)));
        Assert.Equal((1, 1), grabbed.Fine.OffsetAt(Root(s, 2)));
    }

    [Fact]
    public void NoOpStep_KeepsTheSessionInstance()
    {
        var s = Run(NewSession(), Offset(1, 2, 2));
        Assert.Same(s, s.Apply(Drop)); // empty hand
    }

    [Fact]
    public void Undo_RestoresTheFineStateAtTheSnapshot()
    {
        var s = Run(NewSession(), Right, Offset(1, 2, 2), Offset(2, 1, 1));
        var marked = s.Fine;
        var grabbed = Run(s, Grab, Offset(3, -3, 0));
        Assert.Equal((0, 0), grabbed.Fine.OffsetAt(Root(s, 1)));
        Assert.Single(grabbed.FineMarks);

        var undone = grabbed.Apply(Undo);

        Assert.Equal(marked, undone.Fine);
        Assert.Empty(undone.FineMarks);
        Assert.Equal(undone.UndoDepth, undone.FineMarks.Count());
    }

    [Fact]
    public void FineMarks_RunParallelToTheUndoStack_AndTrimWithIt()
    {
        var s = GameSession.New(MakeState(), ImmutableArray<Recipe>.Empty, TickMode.Rogue, maxUndoDepth: 2);
        s = Run(s, Right, Offset(1, 1, 0), Grab, Drop, Offset(2, 0, 1), Grab, Drop);
        Assert.Equal(2, s.UndoDepth);
        Assert.Equal(2, s.FineMarks.Count());
    }

    [Fact]
    public void Undo_WithNoFineMark_ResetsToEmpty()
    {
        var s = Run(NewSession(), Right, Offset(1, 2, 2));
        var outside = s with { UndoStack = s.UndoStack.Push(s.Current) }; // a snapshot pushed from outside the shell
        Assert.Same(FineState.Empty, outside.Apply(Undo).Fine);
    }

    // In realtime a cursor move does not advance TickCount (no time advance), so these tests step with tools.

    [Fact]
    public void Realtime_Checkpoints_CarryTheFineStateAfterTheirStep()
    {
        var policy = new EventLogPolicy(CheckpointEvery: 2);
        var s = Run(NewSession(TickMode.Realtime, policy), Offset(2, 1, 1), Right, Grab, Offset(3, 2, 0), Drop);

        var checkpoint = Assert.Single(s.Checkpoints);
        var fine = Assert.Single(s.FineCheckpoints);
        Assert.Equal(checkpoint.Mark, fine.Mark);
        Assert.Equal(s.Fine, fine.Fine);
    }

    [Fact]
    public void Realtime_Undo_DropsFineCheckpointsOfTheAbandonedTimeline()
    {
        var policy = new EventLogPolicy(CheckpointEvery: 2);
        var s = Run(NewSession(TickMode.Realtime, policy), Right, Grab, Drop, Grab, Drop);
        Assert.Equal(new[] { 2, 4 }, s.Checkpoints.Select(c => c.Mark.Step));
        Assert.Equal(new[] { 2, 4 }, s.FineCheckpoints.Select(c => c.Mark.Step));

        var undone = s.Apply(Undo);

        Assert.Equal(undone.Checkpoints.Select(c => c.Mark), undone.FineCheckpoints.Select(c => c.Mark));
    }

    [Fact]
    public void Realtime_FineCheckpointRing_EvictsWithTheCheckpointRing()
    {
        var policy = new EventLogPolicy(CheckpointEvery: 1, CheckpointRing: 3);
        var s = Run(NewSession(TickMode.Realtime, policy), Right, Grab, Drop, Grab, Drop, Grab);
        Assert.Equal(s.Checkpoints.Select(c => c.Mark), s.FineCheckpoints.Select(c => c.Mark));
        Assert.Equal(3, s.FineCheckpoints.Count);
    }

    [Fact]
    public void Replay_RebuildsTheFineState()
    {
        var start = MakeState();
        GameSession Fresh() => GameSession.New(start, ImmutableArray<Recipe>.Empty, TickMode.Rogue);
        var live = Run(Fresh(), Right, Offset(1, 2, 2), Offset(2, 1, -1), Grab, Offset(3, -3, 0), Undo,
            Offset(2, 6, 6), Right);
        Assert.False(live.Fine.IsEmpty);

        var replayed = ReplayCheck.Replay(Fresh(), live.IntentLog).Last().Session;

        Assert.Equal(live.Fine, replayed.Fine);
        Assert.Equal(live.FineMarks, replayed.FineMarks);
    }
}
