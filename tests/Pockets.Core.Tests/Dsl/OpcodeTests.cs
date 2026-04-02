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

    private static DslState Run(GameState game, string program) =>
        DslInterpreter.Run(DslState.From(game), program);

    // ==================== Navigation ====================

    [Fact]
    public void Right_MovesCursor()
    {
        var state = MakeState();
        var result = Run(state, "right");
        Assert.Equal(new Position(0, 1), result.Game.Cursor.Position);
    }

    [Fact]
    public void Left_WrapsAround()
    {
        var state = MakeState();
        var result = Run(state, "left");
        Assert.Equal(new Position(0, 3), result.Game.Cursor.Position);
    }

    [Fact]
    public void Down_MovesCursor()
    {
        var state = MakeState();
        var result = Run(state, "down");
        Assert.Equal(new Position(1, 0), result.Game.Cursor.Position);
    }

    [Fact]
    public void MultipleNavigation()
    {
        var state = MakeState();
        var result = Run(state, "right right down");
        Assert.Equal(new Position(1, 2), result.Game.Cursor.Position);
    }

    // ==================== Grab and Drop ====================

    [Fact]
    public void Grab_RemovesFromCell_PlacesInHand()
    {
        var state = MakeState((0, new ItemStack(Rock, 5)));
        var result = Run(state, "grab");

        Assert.True(result.Game.CurrentCell.IsEmpty);
        Assert.True(result.Game.HasItemsInHand);
        Assert.Equal(5, result.Game.HandItems[0].Count);
    }

    [Fact]
    public void GrabDrop_MovesCycle()
    {
        var state = MakeState((0, new ItemStack(Rock, 5)));
        var result = Run(state, "grab right drop");

        // Cell 0 should be empty, cell 1 should have the rock
        Assert.True(result.Game.RootBag.Grid.GetCell(0).IsEmpty);
        Assert.Equal(5, result.Game.RootBag.Grid.GetCell(1).Stack!.Count);
        Assert.False(result.Game.HasItemsInHand);
    }

    [Fact]
    public void GrabHalf_SplitsStack()
    {
        var state = MakeState((0, new ItemStack(Rock, 10)));
        var result = Run(state, "grab-half");

        // Left half stays (5), right half goes to hand (5)
        Assert.Equal(5, result.Game.CurrentCell.Stack!.Count);
        Assert.True(result.Game.HasItemsInHand);
        Assert.Equal(5, result.Game.HandItems[0].Count);
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

        // Should be sorted by category then name: Material(Rock) before Weapon(Sword)
        var grid = result.Game.RootBag.Grid;
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
        Assert.True(afterEnter.Game.IsNested);
        Assert.Equal(3, afterEnter.Game.ActiveBag.Grid.Columns);

        var afterLeave = Run(state, "enter leave");
        Assert.False(afterLeave.Game.IsNested);
    }

    // ==================== Combinators ====================

    [Fact]
    public void Times_RepeatsBody()
    {
        var state = MakeState();
        var result = Run(state, "[ right ] 3 times");
        Assert.Equal(new Position(0, 3), result.Game.Cursor.Position);
    }

    [Fact]
    public void Try_CatchesErrors()
    {
        var state = MakeState(); // empty cell, grab will "fail" (no-op since empty)
        // This should not throw — try catches errors
        var result = Run(state, "try { leave }");
        // Stack should have a DslResult (failed, since we're at root)
        Assert.False(result.IsStackEmpty);
    }

    // ==================== Integration: DSL produces same results as direct API ====================

    [Fact]
    public void Regression_GrabDrop_MatchesDirectApi()
    {
        var state = MakeState((0, new ItemStack(Rock, 5)));

        // Direct API
        var directGrab = state.ToolGrab();
        var directState = directGrab.State.MoveCursor(Direction.Right);
        var directDrop = directState.ToolDrop();

        // DSL
        var dslResult = Run(state, "grab right drop");

        // Both should produce the same grid state
        var directGrid = directDrop.State.RootBag.Grid;
        var dslGrid = dslResult.Game.RootBag.Grid;

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
            var dslCell = dslResult.Game.RootBag.Grid.GetCell(i);
            Assert.Equal(directCell.IsEmpty, dslCell.IsEmpty);
            if (!directCell.IsEmpty)
            {
                Assert.Equal(directCell.Stack!.ItemType.Name, dslCell.Stack!.ItemType.Name);
                Assert.Equal(directCell.Stack.Count, dslCell.Stack.Count);
            }
        }
    }
}
