using System.Collections.Immutable;
using Terminal.Gui;
using Pockets.Core.Models;
using Pockets.App.Views;
using Pockets.App.Rendering;

namespace Pockets.App.Tests.Views;

/// <summary>
/// Tests that GridView renders the correct characters into the FakeDriver buffer.
/// Verifies box-drawing borders, cell content text, and cursor inversion.
/// FakeDriver buffer is always 80×25.
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

    // ==================== Box-Drawing Borders ====================

    [Fact]
    public void EmptyCell_RendersBoxBorders()
    {
        var grid = Grid.Create(1, 1);
        var state = MakeState(grid, new Position(0, 0));
        SetupGridView(state);

        // CellWidth=10: ┌ at 0, ─ at 1-8, ┐ at 9
        Assert.Equal('\u250c', _harness!.GetChar(0, 0)); // ┌
        Assert.Equal('\u2500', _harness.GetChar(1, 0));   // ─
        Assert.Equal('\u2510', _harness.GetChar(9, 0));   // ┐
        Assert.Equal('\u2514', _harness.GetChar(0, 2));   // └
        Assert.Equal('\u2518', _harness.GetChar(9, 2));   // ┘
        Assert.Equal('\u2502', _harness.GetChar(0, 1));   // │ left
        Assert.Equal('\u2502', _harness.GetChar(9, 1));   // │ right
    }

    [Fact]
    public void EmptyCell_ContentIsSpaces()
    {
        var grid = Grid.Create(1, 1);
        var state = MakeState(grid, new Position(0, 0));
        SetupGridView(state);

        // Content at cols 1-8 on row 1
        var content = _harness!.GetText(1, 1, CellRenderer.ContentWidth);
        Assert.Equal(new string(' ', CellRenderer.ContentWidth), content);
    }

    // ==================== Cell Content Text ====================

    [Fact]
    public void StackableItem_RendersAbbrevAndCount()
    {
        var cells = new Cell[] { new(new ItemStack(Rock, 5)) };
        var grid = new Grid(1, 1, cells.ToImmutableArray());
        var state = MakeState(grid, new Position(0, 0));
        SetupGridView(state);

        var content = _harness!.GetText(1, 1, CellRenderer.ContentWidth);
        Assert.Contains("ROCK", content);
        Assert.Contains("5", content);
    }

    [Fact]
    public void UniqueItem_RendersAbbrevOnly()
    {
        var cells = new Cell[] { new(new ItemStack(Sword, 1)) };
        var grid = new Grid(1, 1, cells.ToImmutableArray());
        var state = MakeState(grid, new Position(0, 0));
        SetupGridView(state);

        var content = _harness!.GetText(1, 1, CellRenderer.ContentWidth);
        Assert.Contains("SWORD", content);
        Assert.DoesNotContain("\u00d71", content);
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

        var content1 = _harness!.GetText(1, 1, CellRenderer.ContentWidth);
        Assert.Contains("ROCK", content1);

        var content2 = _harness!.GetText(CellRenderer.CellWidth + 1, 1, CellRenderer.ContentWidth);
        Assert.Contains("SWORD", content2);
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

        var content1 = _harness!.GetText(1, 1, CellRenderer.ContentWidth);
        Assert.Contains("ROCK", content1);

        var content2 = _harness!.GetText(1, 1 + CellRenderer.CellHeight, CellRenderer.ContentWidth);
        Assert.Contains("SWORD", content2);
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

        var cursorAttr = _harness!.GetAttribute(1, 1);
        var normalAttr = _harness!.GetAttribute(CellRenderer.CellWidth + 1, 1);
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

        var content1 = _harness!.GetText(1, 1, CellRenderer.ContentWidth);
        Assert.Contains("ROCK", content1);

        // Update to show Sword instead
        var cells2 = new Cell[] { new(new ItemStack(Sword, 1)) };
        var grid2 = new Grid(1, 1, cells2.ToImmutableArray());
        var state2 = MakeState(grid2, new Position(0, 0));
        gridView.UpdateState(state2);
        _harness.Render();

        var content2 = _harness.GetText(1, 1, CellRenderer.ContentWidth);
        Assert.Contains("SWORD", content2);
        Assert.DoesNotContain("ROCK", content2);
    }
}
