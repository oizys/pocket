using Pockets.Core.Models;

namespace Pockets.Core.Tests.Models;

public class ItemTypeTests
{
    [Fact]
    public void EffectiveMaxStackSize_Stackable_ReturnsMaxStackSize()
    {
        var item = new ItemType("Iron Ore", Category.Material, IsStackable: true, MaxStackSize: 20);
        Assert.Equal(20, item.EffectiveMaxStackSize);
    }

    [Fact]
    public void EffectiveMaxStackSize_Unique_ReturnsOne()
    {
        var item = new ItemType("Magic Sword", Category.Weapon, IsStackable: false, MaxStackSize: 20);
        Assert.Equal(1, item.EffectiveMaxStackSize);
    }

    [Fact]
    public void DefaultMaxStackSize_IsTwenty()
    {
        var item = new ItemType("Iron Ore", Category.Material, IsStackable: true);
        Assert.Equal(20, item.MaxStackSize);
    }

    [Fact]
    public void DefaultDescription_IsEmpty()
    {
        var item = new ItemType("Iron Ore", Category.Material, IsStackable: true);
        Assert.Equal(string.Empty, item.Description);
    }

    [Fact]
    public void Equality_SameValues_AreEqual()
    {
        var a = new ItemType("Iron Ore", Category.Material, IsStackable: true);
        var b = new ItemType("Iron Ore", Category.Material, IsStackable: true);
        Assert.Equal(a, b);
    }

    [Fact]
    public void Equality_DifferentNames_AreNotEqual()
    {
        var a = new ItemType("Iron Ore", Category.Material, IsStackable: true);
        var b = new ItemType("Gold Ore", Category.Material, IsStackable: true);
        Assert.NotEqual(a, b);
    }
}
