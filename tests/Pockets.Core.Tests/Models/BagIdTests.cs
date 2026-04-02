using Pockets.Core.Models;

namespace Pockets.Core.Tests.Models;

public class BagIdTests
{
    private static readonly ItemType Rck = new("Rck", Category.Material, IsStackable: true, MaxStackSize: 9);
    private static readonly ItemType BagType = new("Pouch", Category.Bag, IsStackable: false);

    [Fact]
    public void Bag_HasStableId()
    {
        var bag = new Bag(Grid.Create(4, 3));
        Assert.NotEqual(Guid.Empty, bag.Id);
    }

    [Fact]
    public void Bag_IdPreservedThroughWith()
    {
        var bag = new Bag(Grid.Create(4, 3));
        var modified = bag with { EnvironmentType = "Forest" };
        Assert.Equal(bag.Id, modified.Id);
    }

    [Fact]
    public void Bag_IdPreservedThroughAcquireItems()
    {
        var bag = new Bag(Grid.Create(4, 3));
        var (updated, _) = bag.AcquireItems(new[] { new ItemStack(Rck, 5) });
        Assert.Equal(bag.Id, updated.Id);
    }

    [Fact]
    public void Bag_DifferentBags_HaveDifferentIds()
    {
        var bag1 = new Bag(Grid.Create(4, 3));
        var bag2 = new Bag(Grid.Create(4, 3));
        Assert.NotEqual(bag1.Id, bag2.Id);
    }

    [Fact]
    public void BagStore_FindsSelf()
    {
        var bag = new Bag(Grid.Create(4, 3));
        var store = BagStore.Empty.Add(bag);
        var found = store.GetById(bag.Id);
        Assert.NotNull(found);
        Assert.Equal(bag.Id, found!.Id);
    }

    [Fact]
    public void BagStore_FindsDirectChild()
    {
        var innerBag = new Bag(Grid.Create(3, 2), "Cave");
        var rootGrid = Grid.Create(4, 3);
        var bagCell = new Cell(new ItemStack(BagType, 1, ContainedBagId: innerBag.Id));
        rootGrid = rootGrid.SetCell(0, bagCell);
        var rootBag = new Bag(rootGrid);

        var store = BagStore.Empty.Add(rootBag).Add(innerBag);
        var found = store.GetById(innerBag.Id);
        Assert.NotNull(found);
        Assert.Equal(innerBag.Id, found!.Id);
        Assert.Equal("Cave", found.EnvironmentType);
    }

    [Fact]
    public void BagStore_FindsDeeplyNested()
    {
        // Create a 3-level deep structure: root > mid > deep
        var deepBag = new Bag(Grid.Create(2, 2), "Deep");
        var midGrid = Grid.Create(3, 2);
        midGrid = midGrid.SetCell(0, new Cell(new ItemStack(BagType, 1, ContainedBagId: deepBag.Id)));
        var midBag = new Bag(midGrid, "Mid");

        var rootGrid = Grid.Create(4, 3);
        rootGrid = rootGrid.SetCell(0, new Cell(new ItemStack(BagType, 1, ContainedBagId: midBag.Id)));
        var rootBag = new Bag(rootGrid);

        var store = BagStore.Empty.Add(rootBag).Add(midBag).Add(deepBag);
        var found = store.GetById(deepBag.Id);
        Assert.NotNull(found);
        Assert.Equal("Deep", found!.EnvironmentType);
    }

    [Fact]
    public void BagStore_ReturnsNull_WhenNotFound()
    {
        var bag = new Bag(Grid.Create(4, 3));
        var store = BagStore.Empty.Add(bag);
        var found = store.GetById(Guid.NewGuid());
        Assert.Null(found);
    }

    [Fact]
    public void BagStore_MultipleBags_FindsCorrectOne()
    {
        var bag1 = new Bag(Grid.Create(2, 2), "First");
        var bag2 = new Bag(Grid.Create(2, 2), "Second");

        var rootGrid = Grid.Create(4, 3);
        rootGrid = rootGrid.SetCell(0, new Cell(new ItemStack(BagType, 1, ContainedBagId: bag1.Id)));
        rootGrid = rootGrid.SetCell(1, new Cell(new ItemStack(BagType, 1, ContainedBagId: bag2.Id)));
        var rootBag = new Bag(rootGrid);

        var store = BagStore.Empty.Add(rootBag).Add(bag1).Add(bag2);
        var found = store.GetById(bag2.Id);
        Assert.NotNull(found);
        Assert.Equal("Second", found!.EnvironmentType);
    }
}
