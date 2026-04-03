using System.Collections.Immutable;
using Pockets.Core.Dsl;
using Pockets.Core.Models;

namespace Pockets.Core.Tests.Dsl;

public class OpcodeTests
{
    private static readonly ItemType Rock = new("Rock", Category.Material, IsStackable: true, MaxStackSize: 20);
    private static readonly ItemType Sword = new("Sword", Category.Weapon, IsStackable: false);
    private static readonly ImmutableArray<ItemType> Types = ImmutableArray.Create(Rock, Sword);

    private static GameState MakeState(params (int index, ItemStack stack)[] items)
    {
        var grid = Grid.Create(4, 2);
        foreach (var (index, stack) in items)
            grid = grid.SetCell(index, new Cell(stack));
        var rootBag = new Bag(grid);
        var handBag = GameState.CreateHandBag();
        var store = BagStore.Empty.Add(rootBag).Add(handBag);
        return new GameState(store, LocationMap.Create(handBag.Id, rootBag.Id), Types);
    }

    private static OpResult Run(GameState game, string program) =>
        DslInterpreter.RunProgram(game, program);

    // ==================== Navigation ====================

    [Fact]
    public void Right_MovesCursor()
    {
        var state = MakeState();
        var result = Run(state, "right");
        Assert.Equal(new Position(0, 1), result.State.Cursor.Position);
    }

    [Fact]
    public void Left_WrapsAround()
    {
        var state = MakeState();
        var result = Run(state, "left");
        Assert.Equal(new Position(0, 3), result.State.Cursor.Position);
    }

    [Fact]
    public void Down_MovesCursor()
    {
        var state = MakeState();
        var result = Run(state, "down");
        Assert.Equal(new Position(1, 0), result.State.Cursor.Position);
    }

    [Fact]
    public void MultipleNavigation()
    {
        var state = MakeState();
        var result = Run(state, "right right down");
        Assert.Equal(new Position(1, 2), result.State.Cursor.Position);
    }

    // ==================== Grab and Drop ====================

    [Fact]
    public void Grab_RemovesFromCell_PlacesInHand()
    {
        var state = MakeState((0, new ItemStack(Rock, 5)));
        var result = Run(state, "grab");

        Assert.True(result.State.CurrentCell.IsEmpty);
        Assert.True(result.State.HasItemsInHand);
        Assert.Equal(5, result.State.HandItems[0].Count);
    }

    [Fact]
    public void GrabDrop_MovesCycle()
    {
        var state = MakeState((0, new ItemStack(Rock, 5)));
        var result = Run(state, "grab right drop");

        Assert.True(result.State.RootBag.Grid.GetCell(0).IsEmpty);
        Assert.Equal(5, result.State.RootBag.Grid.GetCell(1).Stack!.Count);
        Assert.False(result.State.HasItemsInHand);
    }

    [Fact]
    public void GrabHalf_SplitsStack()
    {
        var state = MakeState((0, new ItemStack(Rock, 10)));
        var result = Run(state, "grab-half");

        Assert.Equal(5, result.State.CurrentCell.Stack!.Count);
        Assert.True(result.State.HasItemsInHand);
        Assert.Equal(5, result.State.HandItems[0].Count);
    }

    // ==================== Sort ====================

    [Fact]
    public void Sort_OrganizesBag()
    {
        var state = MakeState(
            (0, new ItemStack(Sword, 1)),
            (1, new ItemStack(Rock, 5)),
            (2, new ItemStack(Rock, 3)));
        var result = Run(state, "sort");

        var grid = result.State.RootBag.Grid;
        Assert.Equal("Rock", grid.GetCell(0).Stack!.ItemType.Name);
        Assert.Equal(8, grid.GetCell(0).Stack!.Count);
        Assert.Equal("Sword", grid.GetCell(1).Stack!.ItemType.Name);
    }

    // ==================== Enter/Leave ====================

    [Fact]
    public void EnterLeave_RoundTrips()
    {
        var innerBag = new Bag(Grid.Create(3, 3), "Forest");
        var bagType = new ItemType("Forest Bag", Category.Bag, IsStackable: false);
        var grid = Grid.Create(4, 2).SetCell(0, new Cell(new ItemStack(bagType, 1, ContainedBagId: innerBag.Id)));
        var rootBag = new Bag(grid);
        var handBag = GameState.CreateHandBag();
        var store = BagStore.Empty.Add(rootBag).Add(handBag).Add(innerBag);
        var types = Types.Add(bagType);
        var state = new GameState(store, LocationMap.Create(handBag.Id, rootBag.Id), types);

        var afterEnter = Run(state, "enter");
        Assert.True(afterEnter.State.IsNested);
        Assert.Equal(3, afterEnter.State.ActiveBag.Grid.Columns);

        var afterLeave = Run(state, "enter leave");
        Assert.False(afterLeave.State.IsNested);
    }

    // ==================== Combinators ====================

    [Fact]
    public void Times_RepeatsBody()
    {
        var state = MakeState();
        var result = Run(state, "[ right ] 3 times");
        Assert.Equal(new Position(0, 3), result.State.Cursor.Position);
    }

    [Fact]
    public void Try_CatchesErrors()
    {
        var state = MakeState();
        var result = Run(state, "[ leave ] try");
        // try catches the error, result should still be ok (errors cleared)
        Assert.True(result.IsOk);
    }

    // ==================== OpResult threading ====================

    [Fact]
    public void OpResult_PreservesBefore()
    {
        var state = MakeState((0, new ItemStack(Rock, 5)));
        var result = Run(state, "grab right drop");

        // Before should be the original state
        Assert.Equal(state, result.Before);
        // State should be different (items moved)
        Assert.NotEqual(state, result.State);
    }

    [Fact]
    public void OpResult_AccumulatesErrors()
    {
        var state = MakeState(); // empty grid
        // leave fails (at root), but execution continues
        var result = Run(state, "leave");
        Assert.False(result.IsOk);
        Assert.Contains("Already at root bag", result.Errors[0]);
    }

    // ==================== Regression: DSL matches direct API ====================

    [Fact]
    public void Regression_GrabDrop_MatchesDirectApi()
    {
        var state = MakeState((0, new ItemStack(Rock, 5)));

        var directGrab = state.ToolGrab();
        var directState = directGrab.State.MoveCursor(Direction.Right);
        var directDrop = directState.ToolDrop();

        var dslResult = Run(state, "grab right drop");

        var directGrid = directDrop.State.RootBag.Grid;
        var dslGrid = dslResult.State.RootBag.Grid;

        Assert.True(directGrid.GetCell(0).IsEmpty);
        Assert.True(dslGrid.GetCell(0).IsEmpty);
        Assert.Equal(directGrid.GetCell(1).Stack!.Count, dslGrid.GetCell(1).Stack!.Count);
    }

    [Fact]
    public void Regression_Sort_MatchesDirectApi()
    {
        var state = MakeState(
            (0, new ItemStack(Sword, 1)),
            (1, new ItemStack(Rock, 5)));

        var directResult = state.ToolSort();
        var dslResult = Run(state, "sort");

        for (int i = 0; i < 8; i++)
        {
            var directCell = directResult.State.RootBag.Grid.GetCell(i);
            var dslCell = dslResult.State.RootBag.Grid.GetCell(i);
            Assert.Equal(directCell.IsEmpty, dslCell.IsEmpty);
            if (!directCell.IsEmpty)
            {
                Assert.Equal(directCell.Stack!.ItemType.Name, dslCell.Stack!.ItemType.Name);
                Assert.Equal(directCell.Stack.Count, dslCell.Stack.Count);
            }
        }
    }
}
