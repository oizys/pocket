using System.Collections.Immutable;
using Pockets.Core.Models;

namespace Pockets.Core.Tests.Models;

public class BagStoreTests
{
    private static readonly ItemType Rock = new("Rock", Category.Material, IsStackable: true);
    private static readonly ItemType BagType = new("Small Bag", Category.Bag, IsStackable: false);

    [Fact]
    public void Build_RootOnly_ContainsRootAndHand()
    {
        var root = new Bag(Grid.Create(4, 3));
        var hand = GameState.CreateHandBag();
        var store = BagStore.Empty.Add(root).Add(hand);

        Assert.Equal(2, store.Count);
        Assert.NotNull(store.GetById(root.Id));
        Assert.NotNull(store.GetById(hand.Id));
    }

    [Fact]
    public void Build_NestedBag_FoundInStore()
    {
        var inner = new Bag(Grid.Create(2, 2), "Cave");
        var rootGrid = Grid.Create(4, 3);
        rootGrid = rootGrid.SetCell(0, new Cell(new ItemStack(BagType, 1, ContainedBagId: inner.Id)));
        var root = new Bag(rootGrid);
        var hand = GameState.CreateHandBag();

        var store = BagStore.Empty.Add(root).Add(hand).Add(inner);

        Assert.Equal(3, store.Count);
        Assert.NotNull(store.GetById(inner.Id));
    }

    [Fact]
    public void Build_DeeplyNested_AllFound()
    {
        var deepest = new Bag(Grid.Create(1, 1), "Deep");
        var middle = new Bag(Grid.Create(2, 2), "Middle");
        var middleGrid = middle.Grid.SetCell(0, new Cell(new ItemStack(BagType, 1, ContainedBagId: deepest.Id)));
        middle = middle with { Grid = middleGrid };

        var rootGrid = Grid.Create(4, 3);
        rootGrid = rootGrid.SetCell(0, new Cell(new ItemStack(BagType, 1, ContainedBagId: middle.Id)));
        var root = new Bag(rootGrid);
        var hand = GameState.CreateHandBag();

        var store = BagStore.Empty.Add(root).Add(hand).Add(middle).Add(deepest);

        Assert.Equal(4, store.Count); // root, middle, deepest, hand
        Assert.NotNull(store.GetById(deepest.Id));
    }

    [Fact]
    public void GetById_NotFound_ReturnsNull()
    {
        var root = new Bag(Grid.Create(4, 3));
        var hand = GameState.CreateHandBag();
        var store = BagStore.Empty.Add(root).Add(hand);

        Assert.Null(store.GetById(Guid.NewGuid()));
    }

    [Fact]
    public void Contains_Existing_ReturnsTrue()
    {
        var root = new Bag(Grid.Create(4, 3));
        var hand = GameState.CreateHandBag();
        var store = BagStore.Empty.Add(root).Add(hand);

        Assert.True(store.Contains(root.Id));
        Assert.False(store.Contains(Guid.NewGuid()));
    }

    [Fact]
    public void Facilities_ReturnsBagsWithFacilityState()
    {
        var facility = new Bag(Grid.Create(1, 3), FacilityState: new FacilityState());
        var normalBag = new Bag(Grid.Create(2, 2));

        var rootGrid = Grid.Create(4, 3);
        rootGrid = rootGrid.SetCell(0, new Cell(new ItemStack(BagType, 1, ContainedBagId: facility.Id)));
        rootGrid = rootGrid.SetCell(1, new Cell(new ItemStack(BagType, 1, ContainedBagId: normalBag.Id)));
        var root = new Bag(rootGrid);
        var hand = GameState.CreateHandBag();

        var store = BagStore.Empty.Add(root).Add(hand).Add(facility).Add(normalBag);

        var facilities = store.Facilities.ToList();
        Assert.Single(facilities);
        Assert.Equal(facility.Id, facilities[0].Id);
    }

    [Fact]
    public void Facilities_NoFacilities_ReturnsEmpty()
    {
        var root = new Bag(Grid.Create(4, 3));
        var hand = GameState.CreateHandBag();
        var store = BagStore.Empty.Add(root).Add(hand);

        Assert.Empty(store.Facilities);
    }

    [Fact]
    public void GameState_Store_AccessibleAndCorrect()
    {
        var inner = new Bag(Grid.Create(2, 2), "Cave");
        var rootGrid = Grid.Create(4, 3);
        rootGrid = rootGrid.SetCell(0, new Cell(new ItemStack(BagType, 1, ContainedBagId: inner.Id)));
        var root = new Bag(rootGrid);
        var handBag = GameState.CreateHandBag();
        var store = BagStore.Empty.Add(root).Add(handBag).Add(inner);
        var state = new GameState(store, LocationMap.Create(handBag.Id, root.Id), ImmutableArray<ItemType>.Empty);

        Assert.NotNull(state.Store.GetById(inner.Id));
        Assert.Equal(3, state.Store.Count);
    }

    [Fact]
    public void GameState_Store_RebuildAfterMutation()
    {
        var root = new Bag(Grid.Create(4, 3));
        var handBag = GameState.CreateHandBag();
        var store = BagStore.Empty.Add(root).Add(handBag);
        var state = new GameState(store, LocationMap.Create(handBag.Id, root.Id), ImmutableArray<ItemType>.Empty);

        Assert.Equal(2, state.Store.Count);

        // Mutate: add a nested bag
        var inner = new Bag(Grid.Create(2, 2));
        var newGrid = root.Grid.SetCell(0, new Cell(new ItemStack(BagType, 1, ContainedBagId: inner.Id)));
        var updatedRoot = root with { Grid = newGrid };
        var newStore = state.Store.Set(root.Id, updatedRoot).Add(inner);
        var state2 = state with { Store = newStore };

        // New state should have the updated store
        Assert.Equal(3, state2.Store.Count);
        Assert.NotNull(state2.Store.GetById(inner.Id));
    }

    [Fact]
    public void All_ReturnsAllBags()
    {
        var inner = new Bag(Grid.Create(2, 2));
        var rootGrid = Grid.Create(4, 3);
        rootGrid = rootGrid.SetCell(0, new Cell(new ItemStack(BagType, 1, ContainedBagId: inner.Id)));
        var root = new Bag(rootGrid);
        var hand = GameState.CreateHandBag();

        var store = BagStore.Empty.Add(root).Add(hand).Add(inner);
        var allIds = store.All.Select(b => b.Id).ToHashSet();

        Assert.Contains(root.Id, allIds);
        Assert.Contains(inner.Id, allIds);
        Assert.Contains(hand.Id, allIds);
    }
}
