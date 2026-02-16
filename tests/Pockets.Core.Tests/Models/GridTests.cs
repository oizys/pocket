using System.Collections.Immutable;
using Pockets.Core.Models;
using Xunit;

namespace Pockets.Core.Tests.Models;

public class GridTests
{
    private static readonly ItemType Ore = new("Iron Ore", Category.Material, IsStackable: true, MaxStackSize: 20);
    private static readonly ItemType Gem = new("Ruby Gem", Category.Material, IsStackable: true, MaxStackSize: 20);
    private static readonly ItemType Sword = new("Magic Sword", Category.Weapon, IsStackable: false);

    [Fact]
    public void Create_ReturnsCorrectDimensions()
    {
        var grid = Grid.Create(8, 4);

        Assert.Equal(8, grid.Columns);
        Assert.Equal(4, grid.Rows);
        Assert.Equal(32, grid.Cells.Length);
    }

    [Fact]
    public void Create_AllCellsAreEmpty()
    {
        var grid = Grid.Create(8, 4);

        Assert.All(grid.Cells, cell => Assert.True(cell.IsEmpty));
    }

    [Fact]
    public void GetCell_ByPosition_ReturnsCorrectCell()
    {
        var grid = Grid.Create(10, 4);
        var expectedCell = new Cell(new ItemStack(Ore, 5));
        var pos = new Position(1, 2); // row 1, col 2 = index 12
        grid = grid.SetCell(pos, expectedCell);

        var actualCell = grid.GetCell(pos);

        Assert.Equal(expectedCell.Stack, actualCell.Stack);
    }

    [Fact]
    public void GetCell_ByIndex_ReturnsCorrectCell()
    {
        var grid = Grid.Create(8, 4);
        var expectedCell = new Cell(new ItemStack(Ore, 10));
        grid = grid.SetCell(5, expectedCell);

        var actualCell = grid.GetCell(5);

        Assert.Equal(expectedCell.Stack, actualCell.Stack);
    }

    [Fact]
    public void SetCell_ByPosition_ReturnsNewGridWithUpdatedCell()
    {
        var grid = Grid.Create(8, 4);
        var newCell = new Cell(new ItemStack(Ore, 7));
        var pos = new Position(2, 3);

        var updatedGrid = grid.SetCell(pos, newCell);

        Assert.NotSame(grid, updatedGrid);
        Assert.Equal(newCell.Stack, updatedGrid.GetCell(pos).Stack);
        Assert.True(grid.GetCell(pos).IsEmpty); // Original grid unchanged
    }

    [Fact]
    public void AcquireItems_SingleStackFitsInEmptyGrid()
    {
        var grid = Grid.Create(8, 4);
        var stacks = new[] { new ItemStack(Ore, 10) };

        var (updatedGrid, unplaced) = grid.AcquireItems(stacks);

        Assert.Empty(unplaced);
        var firstCell = updatedGrid.GetCell(0);
        Assert.NotNull(firstCell.Stack);
        Assert.Equal(Ore, firstCell.Stack.ItemType);
        Assert.Equal(10, firstCell.Stack.Count);
    }

    [Fact]
    public void AcquireItems_MultipleStacksOfSameType_MergeIntoOne()
    {
        var grid = Grid.Create(8, 4);
        var stacks = new[] { new ItemStack(Ore, 10), new ItemStack(Ore, 5) };

        var (updatedGrid, unplaced) = grid.AcquireItems(stacks);

        Assert.Empty(unplaced);
        var firstCell = updatedGrid.GetCell(0);
        Assert.NotNull(firstCell.Stack);
        Assert.Equal(Ore, firstCell.Stack.ItemType);
        Assert.Equal(15, firstCell.Stack.Count);
    }

    [Fact]
    public void AcquireItems_StackExceedsMax_SpillsToNextCell()
    {
        var grid = Grid.Create(8, 4);
        var stacks = new[] { new ItemStack(Ore, 25) }; // Max is 20

        var (updatedGrid, unplaced) = grid.AcquireItems(stacks);

        Assert.Empty(unplaced);
        var firstCell = updatedGrid.GetCell(0);
        Assert.NotNull(firstCell.Stack);
        Assert.Equal(20, firstCell.Stack.Count);
        var secondCell = updatedGrid.GetCell(1);
        Assert.NotNull(secondCell.Stack);
        Assert.Equal(5, secondCell.Stack.Count);
    }

    [Fact]
    public void AcquireItems_SkipsFilteredCells()
    {
        var grid = Grid.Create(8, 4);
        // Set first cell to only accept Weapons
        grid = grid.SetCell(0, new Cell(CategoryFilter: Category.Weapon));
        var stacks = new[] { new ItemStack(Ore, 10) }; // Material type

        var (updatedGrid, unplaced) = grid.AcquireItems(stacks);

        Assert.Empty(unplaced);
        Assert.True(updatedGrid.GetCell(0).IsEmpty); // First cell still empty (filtered)
        var secondCell = updatedGrid.GetCell(1);
        Assert.NotNull(secondCell.Stack);
        Assert.Equal(Ore, secondCell.Stack.ItemType);
        Assert.Equal(10, secondCell.Stack.Count);
    }

    [Fact]
    public void AcquireItems_EmptyInput_ReturnsUnchanged()
    {
        var grid = Grid.Create(8, 4);
        var stacks = Array.Empty<ItemStack>();

        var (updatedGrid, unplaced) = grid.AcquireItems(stacks);

        Assert.Empty(unplaced);
        Assert.All(updatedGrid.Cells, cell => Assert.True(cell.IsEmpty));
    }

    [Fact]
    public void AcquireItems_FullGrid_ReturnsUnplaced()
    {
        var grid = Grid.Create(2, 1); // Only 2 cells
        // Fill both cells with Ore
        grid = grid.SetCell(0, new Cell(new ItemStack(Ore, 20)));
        grid = grid.SetCell(1, new Cell(new ItemStack(Ore, 20)));
        var stacks = new[] { new ItemStack(Ore, 10) };

        var (updatedGrid, unplaced) = grid.AcquireItems(stacks);

        Assert.Single(unplaced);
        Assert.Equal(Ore, unplaced[0].ItemType);
        Assert.Equal(10, unplaced[0].Count);
    }

    [Fact]
    public void AcquireItems_MergesIntoExistingStack()
    {
        var grid = Grid.Create(8, 4);
        grid = grid.SetCell(0, new Cell(new ItemStack(Ore, 10)));
        var stacks = new[] { new ItemStack(Ore, 5) };

        var (updatedGrid, unplaced) = grid.AcquireItems(stacks);

        Assert.Empty(unplaced);
        var firstCell = updatedGrid.GetCell(0);
        Assert.NotNull(firstCell.Stack);
        Assert.Equal(Ore, firstCell.Stack.ItemType);
        Assert.Equal(15, firstCell.Stack.Count);
    }

    [Fact]
    public void AcquireItems_MultipleItemTypes_PlacedSeparately()
    {
        var grid = Grid.Create(8, 4);
        var stacks = new[] { new ItemStack(Ore, 10), new ItemStack(Gem, 5) };

        var (updatedGrid, unplaced) = grid.AcquireItems(stacks);

        Assert.Empty(unplaced);
        var firstCell = updatedGrid.GetCell(0);
        Assert.NotNull(firstCell.Stack);
        Assert.Equal(Ore, firstCell.Stack.ItemType);
        Assert.Equal(10, firstCell.Stack.Count);
        var secondCell = updatedGrid.GetCell(1);
        Assert.NotNull(secondCell.Stack);
        Assert.Equal(Gem, secondCell.Stack.ItemType);
        Assert.Equal(5, secondCell.Stack.Count);
    }

    [Fact]
    public void AcquireItems_StackRestartsScanFromCellZero()
    {
        // 3-cell grid: cell 0 has Ore(15)
        var grid = Grid.Create(3, 1);
        grid = grid.SetCell(0, new Cell(new ItemStack(Ore, 15)));
        // Gem(5) scans from 0: cell 0 has Ore (different, skip), cell 1 empty → place Gem(5)
        // Ore(5) restarts from 0: cell 0 has Ore(15) matching → merge → 20
        var stacks = new[] { new ItemStack(Gem, 5), new ItemStack(Ore, 5) };

        var (updatedGrid, unplaced) = grid.AcquireItems(stacks);

        Assert.Empty(unplaced);
        var firstCell = updatedGrid.GetCell(0);
        Assert.NotNull(firstCell.Stack);
        Assert.Equal(Ore, firstCell.Stack.ItemType);
        Assert.Equal(20, firstCell.Stack.Count);
        var secondCell = updatedGrid.GetCell(1);
        Assert.NotNull(secondCell.Stack);
        Assert.Equal(Gem, secondCell.Stack.ItemType);
        Assert.Equal(5, secondCell.Stack.Count);
    }

    [Fact]
    public void AcquireItems_WithSkipIndices_SkipsSpecifiedCells()
    {
        var grid = Grid.Create(3, 1);
        var skipIndices = ImmutableHashSet.Create(0);
        var stacks = new[] { new ItemStack(Ore, 10) };

        var (updatedGrid, unplaced) = grid.AcquireItems(stacks, skipIndices);

        Assert.Empty(unplaced);
        Assert.True(updatedGrid.GetCell(0).IsEmpty);
        var secondCell = updatedGrid.GetCell(1);
        Assert.NotNull(secondCell.Stack);
        Assert.Equal(Ore, secondCell.Stack.ItemType);
        Assert.Equal(10, secondCell.Stack.Count);
    }
}
