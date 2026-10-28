using Pockets.Core.Models;
using Pockets.Core.Sim;

namespace Pockets.Core.Tests.Sim;

/// <summary>
/// Slice 1.4: the <see cref="IBagView"/> / <see cref="IBagWriter"/> seam and its one Phase 1 implementation,
/// <see cref="ImmutableBagWriter"/> (spike B's a1 style: one builder, one MoveToImmutable per commit).
/// </summary>
public class ImmutableBagWriterTests
{
    private static readonly ItemType Rock = new("Rock", Category.Material, IsStackable: true, MaxStackSize: 20);

    /// <summary>A 3×2 grid with a Rock stack of <c>i + 1</c> in every even cell.</summary>
    private static Grid MakeGrid()
    {
        var grid = Grid.Create(3, 2);
        for (var i = 0; i < 6; i += 2)
            grid = grid.SetCell(i, new Cell(new ItemStack(Rock, i + 1)));
        return grid;
    }

    [Fact]
    public void View_ExposesDimensionsAndCells()
    {
        var grid = MakeGrid();
        IBagView view = new ImmutableBagWriter(grid);

        Assert.Equal(3, view.Columns);
        Assert.Equal(2, view.Rows);
        Assert.Equal(6, view.Count);
        Assert.All(Enumerable.Range(0, 6), i => Assert.Equal(grid.GetCell(i), view.Cell(i)));
    }

    [Fact]
    public void Commit_WithNoWrites_ReturnsTheSameGridInstance()
    {
        var grid = MakeGrid();
        var writer = new ImmutableBagWriter(grid);

        Assert.Same(grid, writer.Commit());
    }

    [Fact]
    public void Set_IsVisibleThroughCell_BeforeCommit()
    {
        var writer = new ImmutableBagWriter(MakeGrid());
        var cell = new Cell(new ItemStack(Rock, 9));

        writer.Set(1, cell);

        Assert.Equal(cell, writer.Cell(1));
    }

    [Fact]
    public void Commit_AfterWrites_EqualsTheSameSetCellChain()
    {
        var grid = MakeGrid();
        var a = new Cell(new ItemStack(Rock, 7));
        var b = new Cell();
        var writer = new ImmutableBagWriter(grid);

        writer.Set(1, a);
        writer.Set(4, b);
        writer.Set(1, a with { Stack = a.Stack! with { Count = 8 } });
        var committed = writer.Commit();

        var expected = grid.SetCell(1, a with { Stack = a.Stack! with { Count = 8 } }).SetCell(4, b);
        Assert.Equal(expected.Columns, committed.Columns);
        Assert.Equal(expected.Rows, committed.Rows);
        Assert.Equal(expected.Cells.ToArray(), committed.Cells.ToArray());
    }

    [Fact]
    public void Commit_DoesNotChangeTheSourceGrid()
    {
        var grid = MakeGrid();
        var before = grid.Cells.ToArray();
        var writer = new ImmutableBagWriter(grid);

        writer.Set(0, new Cell());
        writer.Commit();

        Assert.Equal(before, grid.Cells.ToArray());
    }

    [Fact]
    public void Commit_Twice_Throws()
    {
        var writer = new ImmutableBagWriter(MakeGrid());
        writer.Set(0, new Cell());
        writer.Commit();

        Assert.Throws<InvalidOperationException>(() => writer.Commit());
        Assert.Throws<InvalidOperationException>(() => writer.Set(0, new Cell()));
    }

    [Theory]
    [InlineData(0, new[] { 1, 3, 4 })]          // top-left corner
    [InlineData(1, new[] { 0, 2, 3, 4, 5 })]    // top edge
    [InlineData(4, new[] { 0, 1, 2, 3, 5 })]    // bottom middle
    [InlineData(5, new[] { 1, 2, 4 })]          // bottom-right corner
    public void Neighbourhood_IsMooreInBounds_NoWrap(int index, int[] expected)
    {
        IBagView view = new ImmutableBagWriter(MakeGrid());

        Assert.Equal(expected, view.Neighbourhood(index).OrderBy(i => i).ToArray());
    }
}
