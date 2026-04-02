using System.Collections.Immutable;
using Pockets.Core.Models;
using Pockets.Core.Rendering;

namespace Pockets.Core.Tests.Models;

public class GameStateTests
{
    private static readonly ItemType Ore = new("Iron Ore", Category.Material, IsStackable: true, MaxStackSize: 20);
    private static readonly ItemType Gem = new("Ruby Gem", Category.Material, IsStackable: true, MaxStackSize: 20);
    private static readonly ItemType Sword = new("Magic Sword", Category.Weapon, IsStackable: false);

    private static readonly ImmutableArray<ItemType> SampleTypes =
        ImmutableArray.Create(Ore, Gem, Sword);

    // --- Diagram types for GridDiagram tests (3-char abbreviations, max stack 9) ---
    private static readonly ItemType Rck = new("Rck", Category.Material, IsStackable: true, MaxStackSize: 9);
    private static readonly ItemType Grs = new("Grs", Category.Material, IsStackable: true, MaxStackSize: 9);
    private static readonly ItemType Swd = new("Swd", Category.Weapon, IsStackable: false);
    private static readonly ItemType Bow = new("Bow", Category.Weapon, IsStackable: false);

    private static readonly Dictionary<string, ItemType> DiagramTypes = new()
    {
        ["Rck"] = Rck, ["Grs"] = Grs, ["Swd"] = Swd, ["Bow"] = Bow,
    };

    /// <summary>
    /// Helper: creates a small GameState from a GridDiagram for compact hand tests.
    /// </summary>
    private static GameState FromDiagram(string diagram, int handSize = 1)
    {
        var parsed = GridDiagram.Parse(diagram, DiagramTypes, gridColumns: 4, gridRows: 3);
        var allTypes = DiagramTypes.Values.Distinct().ToImmutableArray();
        var handBag = parsed.Hand.Length > 0
            ? new Bag(Grid.Create(handSize, 1)).AcquireItems(parsed.Hand).UpdatedBag
            : GameState.CreateHandBag(handSize);
        var rootBag = new Bag(parsed.Grid);
        var store = BagStore.Empty.Add(rootBag).Add(handBag);
        return new GameState(
            store,
            LocationMap.Create(handBag.Id, rootBag.Id, new Cursor(parsed.Cursor ?? new Position(0, 0))),
            allTypes);
    }

    // ==================== CreateStage1 ====================

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
    public void CreateStage1_EmptyHandBag()
    {
        var state = GameState.CreateStage1(SampleTypes, Array.Empty<ItemStack>());
        Assert.False(state.HasItemsInHand);
        Assert.Empty(state.HandItems);
    }

    [Fact]
    public void CreateStage1_HandSizeFromConfig()
    {
        var config = new GameConfig(HandSize: 3);
        var state = GameState.CreateStage1(SampleTypes, Array.Empty<ItemStack>(), config);
        Assert.Equal(3, state.HandBag.Grid.Columns);
    }

    // ==================== MoveCursor ====================

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

    // ==================== CurrentCell ====================

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

    // ==================== ToolGrab (GridDiagram-based) ====================

    [Fact]
    public void ToolGrab_CursorItem_MovesToHand()
    {
        // Diagram case 1: Grab from cursor
        var state = FromDiagram("[Rck5]*[Swd-] [    ] [    ]");
        var result = state.ToolGrab();

        Assert.True(result.Success);
        GridDiagram.AssertGridMatches(result.State.RootBag.Grid,
            DiagramTypes, "[    ] [Swd-] [    ] [    ]");
        Assert.Single(result.State.HandItems);
        Assert.Equal(Rck, result.State.HandItems[0].ItemType);
        Assert.Equal(5, result.State.HandItems[0].Count);
    }

    [Fact]
    public void ToolGrab_EmptyCell_NoOp()
    {
        // Diagram case 9: Grab empty cell
        var state = FromDiagram("[Rck5] [Swd-] [    ]*[    ]");
        var result = state.ToolGrab();

        Assert.True(result.Success);
        Assert.False(result.State.HasItemsInHand);
        GridDiagram.AssertGridMatches(result.State.RootBag.Grid,
            DiagramTypes, "[Rck5] [Swd-] [    ] [    ]");
    }

    [Fact]
    public void ToolGrab_HandFull_DifferentType_NoOp()
    {
        // Diagram case 2: Hand full (1 slot), different type
        var state = FromDiagram("[Rck5]*[Swd-] [    ] [    ]\nHand: (Grs3)");
        var result = state.ToolGrab();

        Assert.False(result.Success);
        Assert.Equal("Hand is full", result.Error);
        // Grid unchanged
        GridDiagram.AssertGridMatches(result.State.RootBag.Grid,
            DiagramTypes, "[Rck5] [Swd-] [    ] [    ]");
    }

    [Fact]
    public void ToolGrab_HandHasSameType_Merges()
    {
        // Diagram case 3: Grab merge (same type in hand)
        var state = FromDiagram("[Rck3]*[Swd-] [    ] [    ]\nHand: (Rck5)");
        var result = state.ToolGrab();

        Assert.True(result.Success);
        GridDiagram.AssertGridMatches(result.State.RootBag.Grid,
            DiagramTypes, "[    ] [Swd-] [    ] [    ]");
        Assert.Single(result.State.HandItems);
        Assert.Equal(Rck, result.State.HandItems[0].ItemType);
        Assert.Equal(8, result.State.HandItems[0].Count);
    }

    [Fact]
    public void ToolGrab_TwoSlotHand_FillsSecondSlot()
    {
        // Diagram case 4: 2-slot hand, different type fills second slot
        var state = FromDiagram("[Rck5]*[Swd-] [    ] [    ]\nHand: (Grs3)");
        // Recreate with 2-slot hand
        state = FromDiagram2Slot("[Rck5]*[Swd-] [    ] [    ]\nHand: (Grs3)");
        var result = state.ToolGrab();

        Assert.True(result.Success);
        GridDiagram.AssertGridMatches(result.State.RootBag.Grid,
            DiagramTypes, "[    ] [Swd-] [    ] [    ]");
        Assert.Equal(2, result.State.HandItems.Count);
    }

    // ==================== ToolDrop (GridDiagram-based) ====================

    [Fact]
    public void ToolDrop_EmptyHand_NoOp()
    {
        var state = FromDiagram("[Rck5] [Swd-] [    ]*[    ]");
        var result = state.ToolDrop();
        Assert.True(result.Success);
        Assert.False(result.State.HasItemsInHand);
    }

    [Fact]
    public void ToolDrop_OntoEmptyCell()
    {
        // Diagram case 5: Drop onto empty cursor cell
        var state = FromDiagram("[Rck5] [Swd-] [    ]*[    ]\nHand: (Rck3)");
        var result = state.ToolDrop();

        Assert.True(result.Success);
        GridDiagram.AssertGridMatches(result.State.RootBag.Grid,
            DiagramTypes, "[Rck5] [Swd-] [Rck3] [    ]");
        Assert.False(result.State.HasItemsInHand);
    }

    [Fact]
    public void ToolDrop_MergesSameType()
    {
        // Diagram case 6: Drop merge same type
        var state = FromDiagram("[    ] [Swd-] [Rck5]*[    ]\nHand: (Rck3)");
        var result = state.ToolDrop();

        Assert.True(result.Success);
        GridDiagram.AssertGridMatches(result.State.RootBag.Grid,
            DiagramTypes, "[    ] [Swd-] [Rck8] [    ]");
        Assert.False(result.State.HasItemsInHand);
    }

    [Fact]
    public void ToolDrop_OverflowAcquiresFromCell0()
    {
        // Diagram case 7: Drop overflow, remainder acquires from cell 0
        var state = FromDiagram("[    ] [Swd-] [Rck7]*[    ]\nHand: (Rck4)");
        var result = state.ToolDrop();

        Assert.True(result.Success);
        GridDiagram.AssertGridMatches(result.State.RootBag.Grid,
            DiagramTypes, "[Rck2] [Swd-] [Rck9] [    ]");
        Assert.False(result.State.HasItemsInHand);
    }

    [Fact]
    public void ToolDrop_DifferentType_NoOp()
    {
        // Diagram case 10: Drop on different type at cursor
        var state = FromDiagram("[Rck5] [Swd-]*[    ] [    ]\nHand: (Grs3)");
        var result = state.ToolDrop();

        Assert.False(result.Success);
        Assert.Contains("different item type", result.Error);
        // Grid unchanged
        GridDiagram.AssertGridMatches(result.State.RootBag.Grid,
            DiagramTypes, "[Rck5] [Swd-] [    ] [    ]");
        Assert.True(result.State.HasItemsInHand);
    }

    [Fact]
    public void ToolDrop_BagFull_NoOp()
    {
        // Diagram case 12: Drop overflow, bag full → no-op + error
        var state = FromDiagram(
            "[Rck9] [Swd-] [Grs9] [Bow-]\n[Rck7]*[Grs5] [Bow-] [Swd-]\n[Rck9] [Grs9] [Swd-] [Bow-]\nHand: (Rck5)");
        var result = state.ToolDrop();

        Assert.False(result.Success);
        Assert.Contains("bag is full", result.Error);
        Assert.True(result.State.HasItemsInHand);
    }

    [Fact]
    public void ToolDrop_SameCellGrabDrop_Roundtrip()
    {
        // Grab then drop at same cell → item restored
        var state = FromDiagram("[Rck5]*[Swd-] [    ] [    ]");
        var grabbed = state.ToolGrab();
        Assert.True(grabbed.Success);
        var dropped = grabbed.State.ToolDrop();
        Assert.True(dropped.Success);

        GridDiagram.AssertGridMatches(dropped.State.RootBag.Grid,
            DiagramTypes, "[Rck5] [Swd-] [    ] [    ]");
        Assert.False(dropped.State.HasItemsInHand);
    }

    [Fact]
    public void ToolDrop_ClearsHand()
    {
        var state = FromDiagram("[    ]*[    ] [    ] [    ]\nHand: (Rck5)");
        var result = state.ToolDrop();
        Assert.True(result.Success);
        Assert.False(result.State.HasItemsInHand);
    }

    // ==================== ToolQuickSplit (GridDiagram-based) ====================

    [Fact]
    public void ToolQuickSplit_EmptyCell_NoOp()
    {
        var state = FromDiagram("[    ]*[    ] [    ] [    ]");
        var result = state.ToolQuickSplit();
        Assert.True(result.Success);
        Assert.False(result.State.HasItemsInHand);
    }

    [Fact]
    public void ToolQuickSplit_CountOne_NoOp()
    {
        var state = FromDiagram("[Rck1]*[    ] [    ] [    ]");
        var result = state.ToolQuickSplit();
        Assert.True(result.Success);
        Assert.False(result.State.HasItemsInHand);
    }

    [Fact]
    public void ToolQuickSplit_EvenSplit_RightToHand()
    {
        // Diagram case 8 (even): 8 → 4 left / 4 right
        var state = FromDiagram("[Rck8]*[Swd-] [    ] [    ]");
        var result = state.ToolQuickSplit();

        Assert.True(result.Success);
        var cursorCell = result.State.RootBag.Grid.GetCell(new Position(0, 0));
        Assert.Equal(4, cursorCell.Stack!.Count);
        Assert.Single(result.State.HandItems);
        Assert.Equal(Rck, result.State.HandItems[0].ItemType);
        Assert.Equal(4, result.State.HandItems[0].Count);
    }

    [Fact]
    public void ToolQuickSplit_OddSplit_CeilingLeft_FloorRight()
    {
        // Diagram case 8: 7 → 4 left / 3 right to hand
        var state = FromDiagram("[Rck7]*[Swd-] [    ] [    ]");
        var result = state.ToolQuickSplit();

        Assert.True(result.Success);
        GridDiagram.AssertGridMatches(result.State.RootBag.Grid,
            DiagramTypes, "[Rck4] [Swd-] [    ] [    ]");
        Assert.Single(result.State.HandItems);
        Assert.Equal(Rck, result.State.HandItems[0].ItemType);
        Assert.Equal(3, result.State.HandItems[0].Count);
    }

    [Fact]
    public void ToolQuickSplit_HandFull_NoOp()
    {
        // Split when hand already has items → error
        var state = FromDiagram("[Rck7]*[Swd-] [    ] [    ]\nHand: (Grs3)");
        var result = state.ToolQuickSplit();

        Assert.False(result.Success);
        Assert.Equal("Hand is full", result.Error);
        // Grid unchanged
        GridDiagram.AssertGridMatches(result.State.RootBag.Grid,
            DiagramTypes, "[Rck7] [Swd-] [    ] [    ]");
    }

    // ==================== ToolSort ====================

    [Fact]
    public void ToolSort_EmptyGrid_NoOp()
    {
        var state = GameState.CreateStage1(SampleTypes, Array.Empty<ItemStack>());
        var result = state.ToolSort();
        Assert.True(result.Success);
    }

    [Fact]
    public void ToolSort_SingleItem_Stays()
    {
        var stacks = new[] { new ItemStack(Ore, 10) };
        var state = GameState.CreateStage1(SampleTypes, stacks);
        var result = state.ToolSort();
        var cell0 = result.State.RootBag.Grid.GetCell(0);
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
        var handBag = GameState.CreateHandBag();
        var store = BagStore.Empty.Add(bag).Add(handBag);
        var state = new GameState(store, LocationMap.Create(handBag.Id, bag.Id), SampleTypes);

        var result = state.ToolSort();

        var cell0 = result.State.RootBag.Grid.GetCell(0);
        Assert.Equal(Ore, cell0.Stack!.ItemType);
        var cell1 = result.State.RootBag.Grid.GetCell(1);
        Assert.Equal(Gem, cell1.Stack!.ItemType);
        var cell2 = result.State.RootBag.Grid.GetCell(2);
        Assert.Equal(Sword, cell2.Stack!.ItemType);
    }

    [Fact]
    public void ToolSort_SameType_MergesCounts()
    {
        var grid = Grid.Create(8, 4);
        grid = grid.SetCell(0, new Cell(new ItemStack(Ore, 10)));
        grid = grid.SetCell(1, new Cell(new ItemStack(Ore, 10)));
        var bag = new Bag(grid);
        var handBag = GameState.CreateHandBag();
        var store = BagStore.Empty.Add(bag).Add(handBag);
        var state = new GameState(store, LocationMap.Create(handBag.Id, bag.Id), SampleTypes);

        var result = state.ToolSort();
        Assert.Equal(20, result.State.RootBag.Grid.GetCell(0).Stack!.Count);
        Assert.True(result.State.RootBag.Grid.GetCell(1).IsEmpty);
    }

    [Fact]
    public void ToolSort_Overflow_SplitsAcrossCells()
    {
        var grid = Grid.Create(8, 4);
        grid = grid.SetCell(0, new Cell(new ItemStack(Ore, 15)));
        grid = grid.SetCell(1, new Cell(new ItemStack(Ore, 15)));
        var bag = new Bag(grid);
        var handBag = GameState.CreateHandBag();
        var store = BagStore.Empty.Add(bag).Add(handBag);
        var state = new GameState(store, LocationMap.Create(handBag.Id, bag.Id), SampleTypes);

        var result = state.ToolSort();
        Assert.Equal(20, result.State.RootBag.Grid.GetCell(0).Stack!.Count);
        Assert.Equal(10, result.State.RootBag.Grid.GetCell(1).Stack!.Count);
    }

    // ==================== ToolAcquireRandom ====================

    [Fact]
    public void ToolAcquireRandom_AddsOneItem()
    {
        var state = GameState.CreateStage1(SampleTypes, Array.Empty<ItemStack>());
        var rng = new Random(42);
        var result = state.ToolAcquireRandom(rng);

        var nonEmptyCells = Enumerable.Range(0, 32)
            .Select(i => result.State.RootBag.Grid.GetCell(i))
            .Where(c => !c.IsEmpty)
            .ToList();

        Assert.Single(nonEmptyCells);
        Assert.Equal(1, nonEmptyCells[0].Stack!.Count);
    }

    [Fact]
    public void ToolAcquireRandom_DeterministicWithSeed()
    {
        var state = GameState.CreateStage1(SampleTypes, Array.Empty<ItemStack>());
        var result1 = state.ToolAcquireRandom(new Random(42));
        var result2 = state.ToolAcquireRandom(new Random(42));

        var cell1 = result1.State.RootBag.Grid.GetCell(0);
        var cell2 = result2.State.RootBag.Grid.GetCell(0);
        Assert.Equal(cell1.Stack?.ItemType, cell2.Stack?.ItemType);
    }

    [Fact]
    public void ToolAcquireRandom_FullGrid_NoCrash()
    {
        var grid = Grid.Create(2, 1);
        grid = grid.SetCell(0, new Cell(new ItemStack(Ore, 20)));
        grid = grid.SetCell(1, new Cell(new ItemStack(Gem, 20)));
        var bag = new Bag(grid);
        var handBag = GameState.CreateHandBag();
        var store = BagStore.Empty.Add(bag).Add(handBag);
        var state = new GameState(store, LocationMap.Create(handBag.Id, bag.Id), SampleTypes);

        var rng = new Random(42);
        var exception = Record.Exception(() => state.ToolAcquireRandom(rng));
        Assert.Null(exception);
    }

    // ==================== Helper ====================

    private GameState FromDiagram2Slot(string diagram)
    {
        var parsed = GridDiagram.Parse(diagram, DiagramTypes, gridColumns: 4, gridRows: 3);
        var allTypes = DiagramTypes.Values.Distinct().ToImmutableArray();
        var handBag = parsed.Hand.Length > 0
            ? new Bag(Grid.Create(2, 1)).AcquireItems(parsed.Hand).UpdatedBag
            : GameState.CreateHandBag(2);
        var rootBag = new Bag(parsed.Grid);
        var store = BagStore.Empty.Add(rootBag).Add(handBag);
        return new GameState(
            store,
            LocationMap.Create(handBag.Id, rootBag.Id, new Cursor(parsed.Cursor ?? new Position(0, 0))),
            allTypes);
    }
}
