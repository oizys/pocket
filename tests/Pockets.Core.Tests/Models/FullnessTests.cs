using Pockets.Core.Models;

namespace Pockets.Core.Tests.Models;

/// <summary>
/// Slice-5 fullness: Core computes the empty/partial/full pip state per bag by cell occupancy, the
/// single source both frontends render from.
/// </summary>
public class FullnessTests
{
    private static readonly ItemType RockType = new("Rock", Category.Material, IsStackable: true);

    private static Bag BagWith(int rows, int cols, int occupied)
    {
        var grid = Grid.Create(cols, rows);
        for (int i = 0; i < occupied; i++)
            grid = grid.SetCell(i, new Cell(new ItemStack(RockType, 1)));
        return new Bag(grid);
    }

    [Fact]
    public void EmptyBag_IsEmpty()
    {
        Assert.Equal(FullnessPip.Empty, Fullness.Of(BagWith(2, 2, 0)));
    }

    [Fact]
    public void PartiallyFilledBag_IsPartial()
    {
        Assert.Equal(FullnessPip.Partial, Fullness.Of(BagWith(2, 2, 2)));
    }

    [Fact]
    public void FullBag_IsFull()
    {
        Assert.Equal(FullnessPip.Full, Fullness.Of(BagWith(2, 2, 4)));
    }

    [Fact]
    public void Of_NonBagStack_IsNull()
    {
        var store = BagStore.Empty;
        Assert.Null(Fullness.Of(store, new ItemStack(RockType, 3)));
    }

    [Fact]
    public void Of_BagStack_ResolvesContainedBag()
    {
        var inner = BagWith(2, 2, 4);
        var store = BagStore.Empty.Add(inner);
        var bagItemType = new ItemType("Pouch", Category.Bag, IsStackable: false);
        var stack = new ItemStack(bagItemType, 1, ContainedBagId: inner.Id);
        Assert.Equal(FullnessPip.Full, Fullness.Of(store, stack));
    }

    [Fact]
    public void Names_AreStableCrossDriverSpellings()
    {
        Assert.Equal("empty", Fullness.Name(FullnessPip.Empty));
        Assert.Equal("partial", Fullness.Name(FullnessPip.Partial));
        Assert.Equal("full", Fullness.Name(FullnessPip.Full));
    }
}
