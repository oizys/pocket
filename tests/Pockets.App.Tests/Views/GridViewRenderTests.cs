using System.Collections.Immutable;
using Pockets.Core.Models;
using Pockets.App.Views;
using Pockets.App.Rendering;

namespace Pockets.App.Tests.Views;

/// <summary>
/// Tests that GridView renders the new 3×2 glyph cells into the FakeDriver buffer.
/// Each cell is 3 cols × 2 rows with no per-cell borders. Row 1 = glyph + count,
/// row 2 = frame pattern. FakeDriver buffer is 80×25.
/// </summary>
public class GridViewRenderTests : IDisposable
{
    private TuiTestHarness? _harness;

    private static readonly ItemType Rock = new("Rock", Category.Material, IsStackable: true, MaxStackSize: 20);
    private static readonly ItemType Sword = new("Sword", Category.Weapon, IsStackable: false);
    private static readonly ImmutableArray<ItemType> AllTypes = ImmutableArray.Create(Rock, Sword);

    private static GameState MakeState(Grid grid, Position cursor)
    {
        var rootBag = new Bag(grid);
        var handBag = GameState.CreateHandBag();
        var store = BagStore.Empty.Add(rootBag).Add(handBag);
        return new GameState(store, LocationMap.Create(handBag.Id, rootBag.Id, new Cursor(cursor)), AllTypes);
    }

    private GridView SetupGridView(GameState state)
    {
        _harness = TuiTestHarness.Create();
        var gridView = new GridView(state) { X = 0, Y = 0 };
        _harness.AddView(gridView);
        _harness.Render();
        return gridView;
    }

    public void Dispose()
    {
        _harness?.Dispose();
    }

    // ==================== 3×2 Glyph Cells ====================

    [Fact]
    public void EmptyCell_RendersThreeSpaces_TwoRows()
    {
        var grid = Grid.Create(1, 1);
        var state = MakeState(grid, new Position(0, 0));
        SetupGridView(state);

        // Cell width = 3, cell height = 2; entire 3x2 area is spaces for an empty cell.
        Assert.Equal("   ", _harness!.GetText(0, 0, 3));
        Assert.Equal("   ", _harness.GetText(0, 1, 3));
    }

    [Fact]
    public void StackableItem_RendersGlyphPlusCount()
    {
        var cells = new Cell[] { new(new ItemStack(Rock, 5)) };
        var grid = new Grid(1, 1, cells.ToImmutableArray());
        var state = MakeState(grid, new Position(0, 0));
        SetupGridView(state);

        // Row 1: glyph (R) + right-aligned count (" 5") = "R 5"
        Assert.Equal("R 5", _harness!.GetText(0, 0, 3));
        // Row 2: no frame, so 3 spaces
        Assert.Equal("   ", _harness.GetText(0, 1, 3));
    }

    [Fact]
    public void StackableItem_TwoDigitCount_RendersGlyphAndDigits()
    {
        var cells = new Cell[] { new(new ItemStack(Rock, 12)) };
        var grid = new Grid(1, 1, cells.ToImmutableArray());
        var state = MakeState(grid, new Position(0, 0));
        SetupGridView(state);

        Assert.Equal("R12", _harness!.GetText(0, 0, 3));
    }

    [Fact]
    public void UniqueItem_RendersGlyphAndTwoSpaces()
    {
        var cells = new Cell[] { new(new ItemStack(Sword, 1)) };
        var grid = new Grid(1, 1, cells.ToImmutableArray());
        var state = MakeState(grid, new Position(0, 0));
        SetupGridView(state);

        // Unique items don't show a count
        Assert.Equal("S  ", _harness!.GetText(0, 0, 3));
    }

    // ==================== Multi-Cell Grid ====================

    [Fact]
    public void TwoColumnGrid_CellsAreAdjacentHorizontally()
    {
        var cells = new Cell[]
        {
            new(new ItemStack(Rock, 3)),
            new(new ItemStack(Sword, 1))
        };
        var grid = new Grid(2, 1, cells.ToImmutableArray());
        var state = MakeState(grid, new Position(0, 0));
        SetupGridView(state);

        // Cell 0 at cols 0-2, cell 1 at cols 3-5
        Assert.Equal("R 3", _harness!.GetText(0, 0, 3));
        Assert.Equal("S  ", _harness.GetText(CellRenderer.CellWidth, 0, 3));
    }

    [Fact]
    public void TwoRowGrid_CellsAreAdjacentVertically()
    {
        var cells = new Cell[]
        {
            new(new ItemStack(Rock, 7)),
            new(new ItemStack(Sword, 1))
        };
        var grid = new Grid(1, 2, cells.ToImmutableArray());
        var state = MakeState(grid, new Position(0, 0));
        SetupGridView(state);

        // Cell 0 at rows 0-1, cell 1 at rows 2-3
        Assert.Equal("R 7", _harness!.GetText(0, 0, 3));
        Assert.Equal("S  ", _harness.GetText(0, CellRenderer.CellHeight, 3));
    }

    // ==================== Cursor Rendering ====================

    [Fact]
    public void CursorCell_HasDifferentAttributeThanNonCursor()
    {
        var cells = new Cell[]
        {
            new(new ItemStack(Rock, 1)),
            new(new ItemStack(Rock, 2))
        };
        var grid = new Grid(2, 1, cells.ToImmutableArray());
        var state = MakeState(grid, new Position(0, 0));
        SetupGridView(state);

        var cursorAttr = _harness!.GetAttribute(0, 0);
        var normalAttr = _harness.GetAttribute(CellRenderer.CellWidth, 0);
        Assert.NotEqual(cursorAttr, normalAttr);
    }

    // ==================== State Update ====================

    [Fact]
    public void UpdateState_ChangesRenderedContent()
    {
        var cells1 = new Cell[] { new(new ItemStack(Rock, 3)) };
        var grid1 = new Grid(1, 1, cells1.ToImmutableArray());
        var state1 = MakeState(grid1, new Position(0, 0));
        var gridView = SetupGridView(state1);

        Assert.Equal("R 3", _harness!.GetText(0, 0, 3));

        var cells2 = new Cell[] { new(new ItemStack(Sword, 1)) };
        var grid2 = new Grid(1, 1, cells2.ToImmutableArray());
        var state2 = MakeState(grid2, new Position(0, 0));
        gridView.UpdateState(state2);
        _harness.Render();

        Assert.Equal("S  ", _harness.GetText(0, 0, 3));
    }
}
