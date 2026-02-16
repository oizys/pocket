using Pockets.Core.Models;

namespace Pockets.Core.Tests.Models;

public class BagTests
{
    private static readonly ItemType Ore = new("Iron Ore", Category.Material, IsStackable: true, MaxStackSize: 20);

    [Fact]
    public void Create_HasCorrectGridDimensions()
    {
        var bag = new Bag(Grid.Create(8, 4));
        Assert.Equal(8, bag.Grid.Columns);
        Assert.Equal(4, bag.Grid.Rows);
    }

    [Fact]
    public void Defaults_EnvironmentAndColorScheme()
    {
        var bag = new Bag(Grid.Create(8, 4));
        Assert.Equal("Default", bag.EnvironmentType);
        Assert.Equal("Default", bag.ColorScheme);
    }

    [Fact]
    public void AcquireItems_DelegatesToGrid()
    {
        var bag = new Bag(Grid.Create(8, 4));
        var stacks = new[] { new ItemStack(Ore, 10) };

        var (updatedBag, unplaced) = bag.AcquireItems(stacks);

        Assert.Empty(unplaced);
        var cell = updatedBag.Grid.GetCell(0);
        Assert.NotNull(cell.Stack);
        Assert.Equal(Ore, cell.Stack.ItemType);
        Assert.Equal(10, cell.Stack.Count);
    }

    [Fact]
    public void AcquireItems_ReturnsNewBag_OriginalUnchanged()
    {
        var bag = new Bag(Grid.Create(8, 4));
        var stacks = new[] { new ItemStack(Ore, 10) };

        var (updatedBag, _) = bag.AcquireItems(stacks);

        Assert.True(bag.Grid.GetCell(0).IsEmpty);
        Assert.False(updatedBag.Grid.GetCell(0).IsEmpty);
    }
}
