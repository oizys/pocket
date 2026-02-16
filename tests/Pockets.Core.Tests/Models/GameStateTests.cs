using System.Collections.Immutable;
using Pockets.Core.Models;

namespace Pockets.Core.Tests.Models;

public class GameStateTests
{
    private static readonly ItemType Ore = new("Iron Ore", Category.Material, IsStackable: true, MaxStackSize: 20);
    private static readonly ItemType Gem = new("Ruby Gem", Category.Material, IsStackable: true, MaxStackSize: 20);
    private static readonly ItemType Sword = new("Magic Sword", Category.Weapon, IsStackable: false);

    private static readonly ImmutableArray<ItemType> SampleTypes =
        ImmutableArray.Create(Ore, Gem, Sword);

    [Fact]
    public void CreateStage1_HasEightByFourGrid()
    {
        var state = GameState.CreateStage1(SampleTypes, Array.Empty<ItemStack>());
        Assert.Equal(8, state.RootBag.Grid.Columns);
        Assert.Equal(4, state.RootBag.Grid.Rows);
    }

    [Fact]
    public void CreateStage1_CursorAtOrigin()
    {
        var state = GameState.CreateStage1(SampleTypes, Array.Empty<ItemStack>());
        Assert.Equal(new Position(0, 0), state.Cursor.Position);
    }

    [Fact]
    public void CreateStage1_StoresItemTypes()
    {
        var state = GameState.CreateStage1(SampleTypes, Array.Empty<ItemStack>());
        Assert.Equal(3, state.ItemTypes.Length);
    }

    [Fact]
    public void CreateStage1_AcquiresInitialStacks()
    {
        var stacks = new[] { new ItemStack(Ore, 10), new ItemStack(Gem, 5) };
        var state = GameState.CreateStage1(SampleTypes, stacks);

        var cell0 = state.RootBag.Grid.GetCell(0);
        Assert.NotNull(cell0.Stack);
        Assert.Equal(Ore, cell0.Stack.ItemType);
        Assert.Equal(10, cell0.Stack.Count);

        var cell1 = state.RootBag.Grid.GetCell(1);
        Assert.NotNull(cell1.Stack);
        Assert.Equal(Gem, cell1.Stack.ItemType);
        Assert.Equal(5, cell1.Stack.Count);
    }

    [Fact]
    public void MoveCursor_Right_UpdatesPosition()
    {
        var state = GameState.CreateStage1(SampleTypes, Array.Empty<ItemStack>());
        var moved = state.MoveCursor(Direction.Right);
        Assert.Equal(new Position(0, 1), moved.Cursor.Position);
    }

    [Fact]
    public void MoveCursor_WrapsAtEdge()
    {
        var state = GameState.CreateStage1(SampleTypes, Array.Empty<ItemStack>());
        var moved = state.MoveCursor(Direction.Left);
        Assert.Equal(new Position(0, 7), moved.Cursor.Position);
    }

    [Fact]
    public void MoveCursor_ReturnsNewState_OriginalUnchanged()
    {
        var state = GameState.CreateStage1(SampleTypes, Array.Empty<ItemStack>());
        var moved = state.MoveCursor(Direction.Down);
        Assert.Equal(new Position(0, 0), state.Cursor.Position);
        Assert.Equal(new Position(1, 0), moved.Cursor.Position);
    }

    [Fact]
    public void CurrentCell_ReturnsEmptyForEmptyGrid()
    {
        var state = GameState.CreateStage1(SampleTypes, Array.Empty<ItemStack>());
        Assert.True(state.CurrentCell.IsEmpty);
    }

    [Fact]
    public void CurrentCell_ReturnsCellAtCursorPosition()
    {
        var stacks = new[] { new ItemStack(Ore, 10) };
        var state = GameState.CreateStage1(SampleTypes, stacks);

        Assert.NotNull(state.CurrentCell.Stack);
        Assert.Equal(Ore, state.CurrentCell.Stack.ItemType);

        var moved = state.MoveCursor(Direction.Right);
        Assert.True(moved.CurrentCell.IsEmpty);
    }

    // ToolGrab Tests

    [Fact]
    public void ToolGrab_EmptyCell_NoOp()
    {
        var state = GameState.CreateStage1(SampleTypes, Array.Empty<ItemStack>());
        var grabbed = state.ToolGrab();
        Assert.Equal(state, grabbed);
        Assert.False(grabbed.HasItemsInHand);
    }

    [Fact]
    public void ToolGrab_NonEmptyCell_AddsToHand()
    {
        var stacks = new[] { new ItemStack(Ore, 10) };
        var state = GameState.CreateStage1(SampleTypes, stacks);
        var grabbed = state.ToolGrab();
        Assert.True(grabbed.HasItemsInHand);
        Assert.Contains(new Position(0, 0), grabbed.ActiveHand);
    }

    [Fact]
    public void ToolGrab_ToggleCancelsGrab()
    {
        var stacks = new[] { new ItemStack(Ore, 10) };
        var state = GameState.CreateStage1(SampleTypes, stacks);
        var grabbed = state.ToolGrab();
        var released = grabbed.ToolGrab();
        Assert.False(released.HasItemsInHand);
        Assert.Null(released.Hand);
    }

    [Fact]
    public void ToolGrab_ItemStaysInGrid()
    {
        var stacks = new[] { new ItemStack(Ore, 10) };
        var state = GameState.CreateStage1(SampleTypes, stacks);
        var grabbed = state.ToolGrab();
        var cell = grabbed.RootBag.Grid.GetCell(0);
        Assert.NotNull(cell.Stack);
        Assert.Equal(Ore, cell.Stack.ItemType);
        Assert.Equal(10, cell.Stack.Count);
    }

    [Fact]
    public void ToolGrab_HandContainsCursorPosition()
    {
        var stacks = new[] { new ItemStack(Ore, 10) };
        var state = GameState.CreateStage1(SampleTypes, stacks);
        var grabbed = state.ToolGrab();
        Assert.NotNull(grabbed.Hand);
        Assert.Single(grabbed.Hand);
        Assert.Equal(new Position(0, 0), grabbed.Hand.First());
    }

    // ToolDrop Tests

    [Fact]
    public void ToolDrop_EmptyHand_NoOp()
    {
        var state = GameState.CreateStage1(SampleTypes, Array.Empty<ItemStack>());
        var dropped = state.ToolDrop();
        Assert.Equal(state, dropped);
    }

    [Fact]
    public void ToolDrop_DropsIntoCursorCell()
    {
        // Grab Ore(10) from cell 0, move cursor to cell 1, drop → item at cursor (cell 1)
        var stacks = new[] { new ItemStack(Ore, 10) };
        var state = GameState.CreateStage1(SampleTypes, stacks);
        var grabbed = state.ToolGrab();
        var moved = grabbed.MoveCursor(Direction.Right); // cursor now at (0,1)
        var dropped = moved.ToolDrop();

        Assert.True(dropped.RootBag.Grid.GetCell(0).IsEmpty);
        var cursorCell = dropped.RootBag.Grid.GetCell(1);
        Assert.NotNull(cursorCell.Stack);
        Assert.Equal(Ore, cursorCell.Stack.ItemType);
        Assert.Equal(10, cursorCell.Stack.Count);
    }

    [Fact]
    public void ToolDrop_MergesIntoCursorCell()
    {
        // Ore(10) at cell 0, Ore(8) at cell 1. Grab cell 0, move to cell 1, drop → merge
        var stacks = new[] { new ItemStack(Ore, 10), new ItemStack(Ore, 8) };
        var state = GameState.CreateStage1(SampleTypes, stacks);
        var grabbed = state.ToolGrab();
        var moved = grabbed.MoveCursor(Direction.Right);
        var dropped = moved.ToolDrop();

        Assert.True(dropped.RootBag.Grid.GetCell(0).IsEmpty);
        var cursorCell = dropped.RootBag.Grid.GetCell(1);
        Assert.NotNull(cursorCell.Stack);
        Assert.Equal(Ore, cursorCell.Stack.ItemType);
        Assert.Equal(18, cursorCell.Stack.Count);
    }

    [Fact]
    public void ToolDrop_RemainderAcquiredFromTop()
    {
        // Ore(15) at cell 0, Ore(8) at cell 1. Grab cell 0, move to cell 1, drop
        // Merge: 8+15=23, max 20 → Ore(20) at cell 1, remainder Ore(3) acquired from cell 0
        var stacks = new[] { new ItemStack(Ore, 15), new ItemStack(Ore, 8) };
        var state = GameState.CreateStage1(SampleTypes, stacks);
        var grabbed = state.ToolGrab();
        var moved = grabbed.MoveCursor(Direction.Right);
        var dropped = moved.ToolDrop();

        var cell0 = dropped.RootBag.Grid.GetCell(0);
        Assert.NotNull(cell0.Stack);
        Assert.Equal(Ore, cell0.Stack.ItemType);
        Assert.Equal(3, cell0.Stack.Count);

        var cell1 = dropped.RootBag.Grid.GetCell(1);
        Assert.NotNull(cell1.Stack);
        Assert.Equal(20, cell1.Stack.Count);
    }

    [Fact]
    public void ToolDrop_SameCellDropRestoresItem()
    {
        // Grab and drop at same cell → item stays put
        var stacks = new[] { new ItemStack(Ore, 10) };
        var state = GameState.CreateStage1(SampleTypes, stacks);
        var grabbed = state.ToolGrab();
        var dropped = grabbed.ToolDrop();

        var cell0 = dropped.RootBag.Grid.GetCell(0);
        Assert.NotNull(cell0.Stack);
        Assert.Equal(Ore, cell0.Stack.ItemType);
        Assert.Equal(10, cell0.Stack.Count);
    }

    [Fact]
    public void ToolDrop_ClearsHandState()
    {
        var stacks = new[] { new ItemStack(Ore, 10) };
        var state = GameState.CreateStage1(SampleTypes, stacks);
        var grabbed = state.ToolGrab();
        var dropped = grabbed.ToolDrop();
        Assert.False(dropped.HasItemsInHand);
        Assert.Null(dropped.Hand);
    }

    // ToolQuickSplit Tests

    [Fact]
    public void ToolQuickSplit_EmptyCell_NoOp()
    {
        var state = GameState.CreateStage1(SampleTypes, Array.Empty<ItemStack>());
        var split = state.ToolQuickSplit();
        Assert.Equal(state, split);
    }

    [Fact]
    public void ToolQuickSplit_CountOne_NoOp()
    {
        var stacks = new[] { new ItemStack(Ore, 1) };
        var state = GameState.CreateStage1(SampleTypes, stacks);
        var split = state.ToolQuickSplit();
        Assert.Equal(state, split);
    }

    [Fact]
    public void ToolQuickSplit_EvenSplit()
    {
        var stacks = new[] { new ItemStack(Ore, 10) };
        var state = GameState.CreateStage1(SampleTypes, stacks);
        var split = state.ToolQuickSplit();
        var cell0 = split.RootBag.Grid.GetCell(0);
        Assert.NotNull(cell0.Stack);
        Assert.Equal(5, cell0.Stack.Count);
        var cell1 = split.RootBag.Grid.GetCell(1);
        Assert.NotNull(cell1.Stack);
        Assert.Equal(5, cell1.Stack.Count);
    }

    [Fact]
    public void ToolQuickSplit_OddSplit_CeilingLeft()
    {
        var stacks = new[] { new ItemStack(Ore, 7) };
        var state = GameState.CreateStage1(SampleTypes, stacks);
        var split = state.ToolQuickSplit();
        var cell0 = split.RootBag.Grid.GetCell(0);
        Assert.NotNull(cell0.Stack);
        Assert.Equal(4, cell0.Stack.Count);
        var cell1 = split.RootBag.Grid.GetCell(1);
        Assert.NotNull(cell1.Stack);
        Assert.Equal(3, cell1.Stack.Count);
    }

    [Fact]
    public void ToolQuickSplit_RightHalfIsGrabbed()
    {
        var stacks = new[] { new ItemStack(Ore, 10) };
        var state = GameState.CreateStage1(SampleTypes, stacks);
        var split = state.ToolQuickSplit();

        Assert.True(split.HasItemsInHand);
        Assert.Contains(new Position(0, 1), split.ActiveHand);
        Assert.DoesNotContain(state.Cursor.Position, split.ActiveHand);
    }

    [Fact]
    public void ToolQuickSplit_RightHalfPlacedInSeparateCell()
    {
        var stacks = new[] { new ItemStack(Ore, 10) };
        var state = GameState.CreateStage1(SampleTypes, stacks);
        var split = state.ToolQuickSplit();
        var cell0 = split.RootBag.Grid.GetCell(0);
        var cell1 = split.RootBag.Grid.GetCell(1);
        Assert.NotNull(cell0.Stack);
        Assert.NotNull(cell1.Stack);
        Assert.Equal(cell0.Stack.ItemType, cell1.Stack.ItemType);
        Assert.NotEqual(10, cell0.Stack.Count);
    }

    // ToolSort Tests

    [Fact]
    public void ToolSort_EmptyGrid_NoOp()
    {
        var state = GameState.CreateStage1(SampleTypes, Array.Empty<ItemStack>());
        var sorted = state.ToolSort();
        Assert.Equal(state, sorted);
    }

    [Fact]
    public void ToolSort_SingleItem_Stays()
    {
        var stacks = new[] { new ItemStack(Ore, 10) };
        var state = GameState.CreateStage1(SampleTypes, stacks);
        var sorted = state.ToolSort();
        var cell0 = sorted.RootBag.Grid.GetCell(0);
        Assert.NotNull(cell0.Stack);
        Assert.Equal(Ore, cell0.Stack.ItemType);
        Assert.Equal(10, cell0.Stack.Count);
    }

    [Fact]
    public void ToolSort_MultiType_SortedByCategoryThenName()
    {
        var grid = Grid.Create(8, 4);
        grid = grid.SetCell(0, new Cell(new ItemStack(Sword, 1)));
        grid = grid.SetCell(1, new Cell(new ItemStack(Gem, 5)));
        grid = grid.SetCell(2, new Cell(new ItemStack(Ore, 10)));
        var bag = new Bag(grid);
        var state = new GameState(bag, new Cursor(new Position(0, 0)), SampleTypes);

        var sorted = state.ToolSort();

        var cell0 = sorted.RootBag.Grid.GetCell(0);
        Assert.NotNull(cell0.Stack);
        Assert.Equal(Ore, cell0.Stack.ItemType);

        var cell1 = sorted.RootBag.Grid.GetCell(1);
        Assert.NotNull(cell1.Stack);
        Assert.Equal(Gem, cell1.Stack.ItemType);

        var cell2 = sorted.RootBag.Grid.GetCell(2);
        Assert.NotNull(cell2.Stack);
        Assert.Equal(Sword, cell2.Stack.ItemType);
    }

    [Fact]
    public void ToolSort_SameType_MergesCounts()
    {
        var grid = Grid.Create(8, 4);
        grid = grid.SetCell(0, new Cell(new ItemStack(Ore, 10)));
        grid = grid.SetCell(1, new Cell(new ItemStack(Ore, 10)));
        var bag = new Bag(grid);
        var state = new GameState(bag, new Cursor(new Position(0, 0)), SampleTypes);

        var sorted = state.ToolSort();

        var cell0 = sorted.RootBag.Grid.GetCell(0);
        Assert.NotNull(cell0.Stack);
        Assert.Equal(Ore, cell0.Stack.ItemType);
        Assert.Equal(20, cell0.Stack.Count);

        var cell1 = sorted.RootBag.Grid.GetCell(1);
        Assert.True(cell1.IsEmpty);
    }

    [Fact]
    public void ToolSort_Overflow_SplitsAcrossCells()
    {
        var grid = Grid.Create(8, 4);
        grid = grid.SetCell(0, new Cell(new ItemStack(Ore, 15)));
        grid = grid.SetCell(1, new Cell(new ItemStack(Ore, 15)));
        var bag = new Bag(grid);
        var state = new GameState(bag, new Cursor(new Position(0, 0)), SampleTypes);

        var sorted = state.ToolSort();

        var cell0 = sorted.RootBag.Grid.GetCell(0);
        Assert.NotNull(cell0.Stack);
        Assert.Equal(Ore, cell0.Stack.ItemType);
        Assert.Equal(20, cell0.Stack.Count);

        var cell1 = sorted.RootBag.Grid.GetCell(1);
        Assert.NotNull(cell1.Stack);
        Assert.Equal(Ore, cell1.Stack.ItemType);
        Assert.Equal(10, cell1.Stack.Count);
    }

    // ToolAcquireRandom Tests

    [Fact]
    public void ToolAcquireRandom_AddsOneItem()
    {
        var state = GameState.CreateStage1(SampleTypes, Array.Empty<ItemStack>());
        var rng = new Random(42);
        var acquired = state.ToolAcquireRandom(rng);

        var nonEmptyCells = Enumerable.Range(0, 32)
            .Select(i => acquired.RootBag.Grid.GetCell(i))
            .Where(c => !c.IsEmpty)
            .ToList();

        Assert.Single(nonEmptyCells);
        Assert.Equal(1, nonEmptyCells[0].Stack!.Count);
    }

    [Fact]
    public void ToolAcquireRandom_DeterministicWithSeed()
    {
        var state = GameState.CreateStage1(SampleTypes, Array.Empty<ItemStack>());
        var acquired1 = state.ToolAcquireRandom(new Random(42));
        var acquired2 = state.ToolAcquireRandom(new Random(42));

        var cell1 = acquired1.RootBag.Grid.GetCell(0);
        var cell2 = acquired2.RootBag.Grid.GetCell(0);

        Assert.Equal(cell1.Stack?.ItemType, cell2.Stack?.ItemType);
    }

    [Fact]
    public void ToolAcquireRandom_FullGrid_NoCrash()
    {
        var grid = Grid.Create(2, 1);
        grid = grid.SetCell(0, new Cell(new ItemStack(Ore, 20)));
        grid = grid.SetCell(1, new Cell(new ItemStack(Gem, 20)));
        var bag = new Bag(grid);
        var state = new GameState(bag, new Cursor(new Position(0, 0)), SampleTypes);

        var rng = new Random(42);
        var exception = Record.Exception(() => state.ToolAcquireRandom(rng));
        Assert.Null(exception);
    }
}
