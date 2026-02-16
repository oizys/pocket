using Pockets.Core.Models;

namespace Pockets.Core.Tests.Models;

public class CellTests
{
    private static readonly ItemType Ore = new("Iron Ore", Category.Material, IsStackable: true);
    private static readonly ItemType Sword = new("Magic Sword", Category.Weapon, IsStackable: false);

    [Fact]
    public void IsEmpty_NullStack_ReturnsTrue()
    {
        var cell = new Cell();
        Assert.True(cell.IsEmpty);
    }

    [Fact]
    public void IsEmpty_WithStack_ReturnsFalse()
    {
        var cell = new Cell(Stack: new ItemStack(Ore, 5));
        Assert.False(cell.IsEmpty);
    }

    [Fact]
    public void Accepts_NoFilter_AlwaysTrue()
    {
        var cell = new Cell();
        Assert.True(cell.Accepts(Ore));
        Assert.True(cell.Accepts(Sword));
    }

    [Fact]
    public void Accepts_MatchingFilter_ReturnsTrue()
    {
        var cell = new Cell(CategoryFilter: Category.Material);
        Assert.True(cell.Accepts(Ore));
    }

    [Fact]
    public void Accepts_NonMatchingFilter_ReturnsFalse()
    {
        var cell = new Cell(CategoryFilter: Category.Material);
        Assert.False(cell.Accepts(Sword));
    }
}
