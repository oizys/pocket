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

    // Each cell envelope is 5×3 (CellRenderer.CellWidth × CellHeight); the 3×2
    // content area sits at (+GapLeft, +GapTop) = (+2, +1) within each envelope.
    private const int ContentX0 = CellRenderer.GapLeft;
    private const int ContentY0 = CellRenderer.GapTop;
    private const int W = CellRenderer.ContentWidth;

    [Fact]
    public void EmptyCell_ContentArea_IsSpaces()
    {
        var grid = Grid.Create(1, 1);
        var state = MakeState(grid, new Position(0, 0));
        SetupGridView(state);

        Assert.Equal("   ", _harness!.GetText(ContentX0, ContentY0, W));
        Assert.Equal("   ", _harness.GetText(ContentX0, ContentY0 + 1, W));
    }

    [Fact]
    public void StackableItem_RendersGlyphPlusCount()
    {
        var cells = new Cell[] { new(new ItemStack(Rock, 5)) };
        var grid = new Grid(1, 1, cells.ToImmutableArray());
        var state = MakeState(grid, new Position(0, 0));
        SetupGridView(state);

        // Row 1: glyph (R) + right-aligned count (" 5") = "R 5"
        Assert.Equal("R 5", _harness!.GetText(ContentX0, ContentY0, W));
        // Row 2: no frame, so 3 spaces
        Assert.Equal("   ", _harness.GetText(ContentX0, ContentY0 + 1, W));
    }

    [Fact]
    public void StackableItem_TwoDigitCount_RendersGlyphAndDigits()
    {
        var cells = new Cell[] { new(new ItemStack(Rock, 12)) };
        var grid = new Grid(1, 1, cells.ToImmutableArray());
        var state = MakeState(grid, new Position(0, 0));
        SetupGridView(state);

        Assert.Equal("R12", _harness!.GetText(ContentX0, ContentY0, W));
    }

    [Fact]
    public void UniqueItem_RendersGlyphAndTwoSpaces()
    {
        var cells = new Cell[] { new(new ItemStack(Sword, 1)) };
        var grid = new Grid(1, 1, cells.ToImmutableArray());
        var state = MakeState(grid, new Position(0, 0));
        SetupGridView(state);

        Assert.Equal("S  ", _harness!.GetText(ContentX0, ContentY0, W));
    }

    [Fact]
    public void Gap_AroundCellContent_IsSpaces()
    {
        // The left gap (cols 0..GapLeft-1) and top gap (row 0) must be blank
        // even when the cell is occupied. This space is reserved for future
        // cursor / selection / frame badges.
        var cells = new Cell[] { new(new ItemStack(Rock, 5)) };
        var grid = new Grid(1, 1, cells.ToImmutableArray());
        var state = MakeState(grid, new Position(0, 0));
        SetupGridView(state);

        // Top gap row
        for (int x = 0; x < CellRenderer.CellWidth; x++)
            Assert.Equal(' ', _harness!.GetChar(x, 0));
        // Left gap cols at content rows
        for (int gx = 0; gx < CellRenderer.GapLeft; gx++)
        for (int y = ContentY0; y < ContentY0 + CellRenderer.ContentHeight; y++)
            Assert.Equal(' ', _harness!.GetChar(gx, y));
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

        Assert.Equal("R 3", _harness!.GetText(ContentX0, ContentY0, W));
        Assert.Equal("S  ", _harness.GetText(CellRenderer.CellWidth + ContentX0, ContentY0, W));
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

        Assert.Equal("R 7", _harness!.GetText(ContentX0, ContentY0, W));
        Assert.Equal("S  ", _harness.GetText(ContentX0, CellRenderer.CellHeight + ContentY0, W));
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

        var cursorAttr = _harness!.GetAttribute(ContentX0, ContentY0);
        var normalAttr = _harness.GetAttribute(CellRenderer.CellWidth + ContentX0, ContentY0);
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

        Assert.Equal("R 3", _harness!.GetText(ContentX0, ContentY0, W));

        var cells2 = new Cell[] { new(new ItemStack(Sword, 1)) };
        var grid2 = new Grid(1, 1, cells2.ToImmutableArray());
        var state2 = MakeState(grid2, new Position(0, 0));
        gridView.UpdateState(state2);
        _harness.Render();

        Assert.Equal("S  ", _harness.GetText(ContentX0, ContentY0, W));
    }
}
