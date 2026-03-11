using System.Collections.Immutable;
using Pockets.Core.Models;

namespace Pockets.Core.Tests.Models;

/// <summary>
/// Verifies that cell frames (InputSlotFrame/OutputSlotFrame) are preserved
/// after tool operations that modify a cell's stack contents.
/// </summary>
public class FramePreservationTests
{
    private static readonly ItemType Rock = new("Plain Rock", Category.Material, IsStackable: true, MaxStackSize: 20);
    private static readonly ItemType Wood = new("Rough Wood", Category.Material, IsStackable: true, MaxStackSize: 20);
    private static readonly ItemType BagType = new("Test Bag", Category.Bag, IsStackable: false);

    private static readonly ImmutableArray<ItemType> AllTypes =
        ImmutableArray.Create(Rock, Wood, BagType);

    /// <summary>
    /// Creates a simple 4x2 root bag with a single cell at position (0,0) containing
    /// the given item stack and frame. Cursor starts at (0,0).
    /// </summary>
    private static GameState CreateStateWithFramedCell(ItemStack? stack, CellFrame frame)
    {
        var rootGrid = Grid.Create(4, 2);
        rootGrid = rootGrid.SetCell(0, new Cell(stack, Frame: frame));
        var rootBag = new Bag(rootGrid);
        return new GameState(rootBag, new Cursor(new Position(0, 0)), AllTypes, GameState.CreateHandBag());
    }

    /// <summary>
    /// Creates a state with hand holding one Rock, cursor at (0,0).
    /// The cell at (0,0) has the given item stack and frame.
    /// </summary>
    private static GameState CreateStateWithHandAndFramedCell(ItemStack? cellStack, CellFrame frame, ItemStack handStack)
    {
        var rootGrid = Grid.Create(4, 2);
        rootGrid = rootGrid.SetCell(0, new Cell(cellStack, Frame: frame));
        var rootBag = new Bag(rootGrid);

        var handBag = GameState.CreateHandBag();
        var (filledHand, _) = handBag.AcquireItems(new[] { handStack });

        return new GameState(rootBag, new Cursor(new Position(0, 0)), AllTypes, filledHand);
    }

    // ==================== ToolGrab ====================

    [Fact]
    public void ToolGrab_PreservesInputSlotFrame()
    {
        // Arrange: cell at (0,0) has an InputSlotFrame and a Rock item
        var frame = new InputSlotFrame("in1");
        var state = CreateStateWithFramedCell(new ItemStack(Rock, 3), frame);

        // Act: grab from the cell
        var result = state.ToolGrab();

        // Assert: operation succeeded, cell is empty, but frame is preserved
        Assert.True(result.Success);
        var cell = result.State.ActiveBag.Grid.GetCell(0);
        Assert.True(cell.IsEmpty, "Cell should be empty after grab");
        Assert.NotNull(cell.Frame);
        Assert.IsType<InputSlotFrame>(cell.Frame);
        Assert.Equal("in1", ((InputSlotFrame)cell.Frame).SlotId);
    }

    [Fact]
    public void ToolGrab_PreservesOutputSlotFrame()
    {
        // Arrange: cell at (0,0) has an OutputSlotFrame and a Rock item
        var frame = new OutputSlotFrame("out1");
        var state = CreateStateWithFramedCell(new ItemStack(Rock, 5), frame);

        // Act: grab from the cell
        var result = state.ToolGrab();

        // Assert: operation succeeded, cell is empty, but frame is preserved
        Assert.True(result.Success);
        var cell = result.State.ActiveBag.Grid.GetCell(0);
        Assert.True(cell.IsEmpty, "Cell should be empty after grab");
        Assert.NotNull(cell.Frame);
        Assert.IsType<OutputSlotFrame>(cell.Frame);
        Assert.Equal("out1", ((OutputSlotFrame)cell.Frame).SlotId);
    }

    // ==================== ToolSwap ====================

    [Fact]
    public void ToolSwap_PreservesFrame()
    {
        // Arrange: cell at (0,0) has InputSlotFrame + Rock; hand has Wood
        var frame = new InputSlotFrame("in1");
        var state = CreateStateWithHandAndFramedCell(
            cellStack: new ItemStack(Rock, 3),
            frame: frame,
            handStack: new ItemStack(Wood, 2));

        // Act: swap hand (Wood) with cell (Rock)
        var result = state.ToolSwap();

        // Assert: cell now has Wood, frame is preserved
        Assert.True(result.Success);
        var cell = result.State.ActiveBag.Grid.GetCell(0);
        Assert.False(cell.IsEmpty, "Cell should have item after swap");
        Assert.Equal(Wood, cell.Stack!.ItemType);
        Assert.NotNull(cell.Frame);
        Assert.IsType<InputSlotFrame>(cell.Frame);
        Assert.Equal("in1", ((InputSlotFrame)cell.Frame).SlotId);
    }

    // ==================== ToolPlaceOne ====================

    [Fact]
    public void ToolPlaceOne_PreservesFrame()
    {
        // Arrange: empty cell at (0,0) has InputSlotFrame; hand has Rock
        var frame = new InputSlotFrame("in1");
        var state = CreateStateWithHandAndFramedCell(
            cellStack: null,
            frame: frame,
            handStack: new ItemStack(Rock, 5));

        // Act: place one from hand into the empty framed cell
        var result = state.ToolPlaceOne();

        // Assert: cell has 1 Rock, frame is preserved
        Assert.True(result.Success);
        var cell = result.State.ActiveBag.Grid.GetCell(0);
        Assert.False(cell.IsEmpty, "Cell should have item after PlaceOne");
        Assert.Equal(Rock, cell.Stack!.ItemType);
        Assert.Equal(1, cell.Stack.Count);
        Assert.NotNull(cell.Frame);
        Assert.IsType<InputSlotFrame>(cell.Frame);
        Assert.Equal("in1", ((InputSlotFrame)cell.Frame).SlotId);
    }

    // ==================== ToolHarvest ====================

    [Fact]
    public void ToolHarvest_PreservesFrame()
    {
        // Arrange: create inner bag with framed cell containing a Rock item
        var innerFrame = new InputSlotFrame("in1");
        var innerGrid = Grid.Create(3, 1);
        innerGrid = innerGrid.SetCell(0, new Cell(new ItemStack(Rock, 2), Frame: innerFrame));
        var innerBag = new Bag(innerGrid);

        // Create root bag with bag-type item at cell 0 (containing innerBag)
        var rootGrid = Grid.Create(4, 2);
        rootGrid = rootGrid.SetCell(0, new Cell(new ItemStack(BagType, 1, ContainedBag: innerBag)));
        var state = new GameState(new Bag(rootGrid), new Cursor(new Position(0, 0)), AllTypes, GameState.CreateHandBag());

        // Enter the inner bag (cursor is at the bag cell)
        var enterResult = state.EnterBag();
        Assert.True(enterResult.Success, "EnterBag should succeed");
        state = enterResult.State;

        // Verify we're now inside the inner bag and cursor is at (0,0)
        Assert.True(state.IsNested);
        Assert.Equal(new Position(0, 0), state.Cursor.Position);

        // Act: harvest the item from the framed cell
        var harvestResult = state.ToolHarvest();

        // Assert: harvest succeeded
        Assert.True(harvestResult.Success);

        // The inner bag cell at (0,0) should be empty but still have the InputSlotFrame
        var innerBagAfter = harvestResult.State.RootBag.Grid.GetCell(0).Stack!.ContainedBag!;
        var framedCell = innerBagAfter.Grid.GetCell(0);
        Assert.True(framedCell.IsEmpty, "Inner bag cell should be empty after harvest");
        Assert.NotNull(framedCell.Frame);
        Assert.IsType<InputSlotFrame>(framedCell.Frame);
        Assert.Equal("in1", ((InputSlotFrame)framedCell.Frame).SlotId);

        // The harvested item should have been placed in the parent (root) bag
        var rootBagAfter = harvestResult.State.RootBag;
        var rootCells = rootBagAfter.Grid.Cells;
        var hasRock = rootCells.Any(c => !c.IsEmpty && c.Stack!.ItemType == Rock);
        Assert.True(hasRock, "Harvested Rock should be in the root bag");
    }
}
