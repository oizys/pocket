using System.Collections.Immutable;
using Pockets.Core.Fine;
using Pockets.Core.Models;
using Pockets.Core.Sim;

namespace Pockets.Core.Tests.Fine;

/// <summary>
/// Slice 2.6: <see cref="FineLogic.Reconcile"/>, run by the shell after every Step. Any L1 move of a stack clears its
/// offset (P2-D11): a cell named as a departure or arrival by StackMoved, StackSplit, StackMerged, StackRemoved,
/// Harvested or BagRearranged loses it, and so does a cell that is empty after the step. A body is re-anchored to its
/// cell centre by CursorMoved, BagEntered and BagLeft. Nothing to reconcile returns the same instance.
/// </summary>
public class FineReconcileTests
{
    private static readonly ItemType Rock = new("Rock", Category.Material, IsStackable: true, MaxStackSize: 20);

    /// <summary>A 4×2 root and a 4×2 second bag, every cell holding rocks; the hand is empty.</summary>
    private static GameState Full(out Guid a, out Guid b)
    {
        var grid = Enumerable.Range(0, 8).Aggregate(Grid.Create(4, 2), (g, i) => g.SetCell(i, new Cell(new ItemStack(Rock, 3))));
        var root = new Bag(grid);
        var other = new Bag(grid);
        var hand = GameState.CreateHandBag();
        a = root.Id;
        b = other.Id;
        return new GameState(BagStore.Empty.Add(root).Add(hand).Add(other), LocationMap.Create(hand.Id, root.Id),
            ImmutableArray.Create(Rock));
    }

    /// <summary>Offsets on cells 0–3 of bag a and cell 0 of bag b.</summary>
    private static FineState Offsets(Guid a, Guid b) =>
        Enumerable.Range(0, 4).Aggregate(FineState.Empty, (f, i) => f.WithOffset(new CellRef(a, i), 1, 1))
            .WithOffset(new CellRef(b, 0), 2, 2);

    /// <summary>The cells of <paramref name="fine"/> that still have an offset, as (bag, index), ordered.</summary>
    private static List<(Guid, int)> Cells(FineState fine) =>
        fine.Offsets.Keys.Select(c => (c.Bag, c.Index)).OrderBy(c => c.Bag).ThenBy(c => c.Index).ToList();

    private static List<(Guid, int)> Expected(params (Guid, int)[] cells) =>
        cells.OrderBy(c => c.Item1).ThenBy(c => c.Item2).ToList();

    [Fact]
    public void NoEvents_ReturnsTheSameInstance()
    {
        var game = Full(out var a, out var b);
        var fine = Offsets(a, b);
        Assert.Same(fine, FineLogic.Reconcile(fine, Array.Empty<SimEvent>(), game));
    }

    [Fact]
    public void Empty_StaysTheSameInstance_UnderAnyEvents()
    {
        var game = Full(out var a, out _);
        var events = new SimEvent[]
        {
            new SimEvent.StackMoved(1, 0, Rock, 3, new CellRef(a, 0), new CellRef(a, 1), StackCause.Grab),
            new SimEvent.CursorMoved(1, 1, LocationId.B, a, new Position(0, 0), new Position(0, 1)),
            new SimEvent.BagEntered(1, 2, LocationId.B, a, 1),
        };
        Assert.Same(FineState.Empty, FineLogic.Reconcile(FineState.Empty, events, game));
    }

    [Fact]
    public void UnrelatedEvents_KeepTheSameInstance()
    {
        var game = Full(out var a, out var b);
        var fine = Offsets(a, b);
        var events = new SimEvent[] { new SimEvent.TimeAdvanced(1, 0) };
        Assert.Same(fine, FineLogic.Reconcile(fine, events, game));
    }

    [Fact]
    public void StackMoved_DropsBothEnds()
    {
        var game = Full(out var a, out var b);
        var next = FineLogic.Reconcile(Offsets(a, b),
            new[] { new SimEvent.StackMoved(1, 0, Rock, 3, new CellRef(a, 0), new CellRef(b, 0), StackCause.Grab) }, game);
        Assert.Equal(Expected((a, 1), (a, 2), (a, 3)), Cells(next));
    }

    [Fact]
    public void StackSplit_DropsBothEnds()
    {
        var game = Full(out var a, out var b);
        var next = FineLogic.Reconcile(Offsets(a, b),
            new[] { new SimEvent.StackSplit(1, 0, Rock, new CellRef(a, 1), new CellRef(a, 2), 2, 1, StackCause.Split) }, game);
        Assert.Equal(Expected((a, 0), (a, 3), (b, 0)), Cells(next));
    }

    [Fact]
    public void StackMerged_DropsBothEnds()
    {
        var game = Full(out var a, out var b);
        var next = FineLogic.Reconcile(Offsets(a, b),
            new[] { new SimEvent.StackMerged(1, 0, Rock, 2, new CellRef(a, 3), new CellRef(a, 0), StackCause.Drop) }, game);
        Assert.Equal(Expected((a, 1), (a, 2), (b, 0)), Cells(next));
    }

    [Fact]
    public void StackRemoved_DropsTheCell()
    {
        var game = Full(out var a, out var b);
        var next = FineLogic.Reconcile(Offsets(a, b),
            new[] { new SimEvent.StackRemoved(1, 0, Rock, 3, new CellRef(a, 2), StackCause.Dsl) }, game);
        Assert.Equal(Expected((a, 0), (a, 1), (a, 3), (b, 0)), Cells(next));
    }

    [Fact]
    public void Harvested_DropsTheCellAndItsYields()
    {
        var game = Full(out var a, out var b);
        var harvested = new SimEvent.Harvested(1, 0, new CellRef(a, 1),
            ImmutableArray.Create(new Placed(Rock, 1, new CellRef(b, 0))));
        var next = FineLogic.Reconcile(Offsets(a, b), new[] { harvested }, game);
        Assert.Equal(Expected((a, 0), (a, 2), (a, 3)), Cells(next));
    }

    [Fact]
    public void BagRearranged_DropsEveryCellOfThatBag()
    {
        var game = Full(out var a, out var b);
        var next = FineLogic.Reconcile(Offsets(a, b),
            new[] { new SimEvent.BagRearranged(1, 0, a, ImmutableArray.Create(3, 2, 1, 0, 4, 5, 6, 7)) }, game);
        Assert.Equal(Expected((b, 0)), Cells(next));
    }

    [Fact]
    public void ACellEmptyAfterTheStep_LosesItsOffset()
    {
        var game = Full(out var a, out var b);
        var bag = game.Store.GetById(a)!;
        var emptied = game with { Store = game.Store.Set(a, bag with { Grid = bag.Grid.SetCell(3, new Cell()) }) };
        var next = FineLogic.Reconcile(Offsets(a, b), new[] { new SimEvent.TimeAdvanced(1, 0) }, emptied);
        Assert.Equal(Expected((a, 0), (a, 1), (a, 2), (b, 0)), Cells(next));
    }

    [Fact]
    public void CursorMoved_ReAnchorsThatBody_Only()
    {
        var game = Full(out var a, out var b);
        var fine = FineState.Empty
            .WithBody(LocationId.B, a, new FineBody(5, -3, 4, 0, Direction8.E))
            .WithBody(LocationId.B, b, new FineBody(2, 2, 0, 0, null))
            .WithBody(LocationId.H, a, new FineBody(1, 1, 0, 0, null));

        var next = FineLogic.Reconcile(fine,
            new[] { new SimEvent.CursorMoved(1, 0, LocationId.B, a, new Position(0, 0), new Position(0, 1)) }, game);

        Assert.Equal(new FineBody(0, 0, 4, 0, Direction8.E), next.BodyAt(LocationId.B, a));
        Assert.Equal(new FineBody(2, 2, 0, 0, null), next.BodyAt(LocationId.B, b));
        Assert.Equal(new FineBody(1, 1, 0, 0, null), next.BodyAt(LocationId.H, a));
    }

    [Fact]
    public void CursorMoved_OfABodyWithOnlyAPosition_LeavesNoEntry()
    {
        var game = Full(out var a, out _);
        var fine = FineState.Empty.WithBody(LocationId.B, a, new FineBody(5, -3, 0, 0, null));
        var next = FineLogic.Reconcile(fine,
            new[] { new SimEvent.CursorMoved(1, 0, LocationId.B, a, new Position(0, 0), new Position(0, 1)) }, game);
        Assert.True(next.IsEmpty);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void BagEnteredOrLeft_ReAnchorsEveryBodyOfThatLocation(bool entered)
    {
        var game = Full(out var a, out var b);
        var fine = FineState.Empty
            .WithBody(LocationId.B, a, new FineBody(5, -3, 0, 0, null))
            .WithBody(LocationId.B, b, new FineBody(2, 2, 0, 0, null))
            .WithBody(LocationId.H, a, new FineBody(1, 1, 0, 0, null));
        SimEvent e = entered ? new SimEvent.BagEntered(1, 0, LocationId.B, b, 1) : new SimEvent.BagLeft(1, 0, LocationId.B, a, 0);

        var next = FineLogic.Reconcile(fine, new[] { e }, game);

        Assert.Equal(FineBody.Rest, next.BodyAt(LocationId.B, a));
        Assert.Equal(FineBody.Rest, next.BodyAt(LocationId.B, b));
        Assert.Equal(new FineBody(1, 1, 0, 0, null), next.BodyAt(LocationId.H, a));
    }

    [Fact]
    public void SetOffset_TargetsTheActiveBagCell_ClampsAndIgnoresEmptyOrOutOfRange()
    {
        var game = Full(out var a, out _);
        var bag = game.Store.GetById(a)!;
        game = game with { Store = game.Store.Set(a, bag with { Grid = bag.Grid.SetCell(5, new Cell()) }) };

        var set = FineLogic.SetOffset(FineState.Empty, game, LocationId.B, 2, 9, -1);
        Assert.Equal((6, -1), set.OffsetAt(new CellRef(a, 2)));
        Assert.Same(set, FineLogic.SetOffset(set, game, LocationId.B, 5, 1, 1));   // empty cell
        Assert.Same(set, FineLogic.SetOffset(set, game, LocationId.B, 8, 1, 1));   // out of range
        Assert.Same(set, FineLogic.SetOffset(set, game, LocationId.B, -1, 1, 1));  // out of range
        Assert.Same(set, FineLogic.SetOffset(set, game, LocationId.C, 2, 1, 1));   // C has no bag here
        Assert.True(FineLogic.SetOffset(set, game, LocationId.B, 2, 0, 0).IsEmpty);
    }
}
