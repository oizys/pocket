using System.Collections.Immutable;
using Pockets.Core.Models;

namespace Pockets.Core.Tests.Models;

public class BagRegistryTests
{
    private static readonly ItemType Rock = new("Rock", Category.Material, IsStackable: true);
    private static readonly ItemType BagType = new("Small Bag", Category.Bag, IsStackable: false);

    [Fact]
    public void Build_RootOnly_ContainsRootAndHand()
    {
        var root = new Bag(Grid.Create(4, 3));
        var hand = GameState.CreateHandBag();
        var registry = BagRegistry.Build(root, hand);

        Assert.Equal(2, registry.Count);
        Assert.NotNull(registry.GetById(root.Id));
        Assert.NotNull(registry.GetById(hand.Id));
    }

    [Fact]
    public void Build_NestedBag_FoundInRegistry()
    {
        var inner = new Bag(Grid.Create(2, 2), "Cave");
        var rootGrid = Grid.Create(4, 3);
        rootGrid = rootGrid.SetCell(0, new Cell(new ItemStack(BagType, 1, ContainedBag: inner)));
        var root = new Bag(rootGrid);
        var hand = GameState.CreateHandBag();

        var registry = BagRegistry.Build(root, hand);

        Assert.Equal(3, registry.Count);
        Assert.NotNull(registry.GetById(inner.Id));
    }

    [Fact]
    public void Build_DeeplyNested_AllFound()
    {
        var deepest = new Bag(Grid.Create(1, 1), "Deep");
        var middle = new Bag(Grid.Create(2, 2), "Middle");
        var middleGrid = middle.Grid.SetCell(0, new Cell(new ItemStack(BagType, 1, ContainedBag: deepest)));
        middle = middle with { Grid = middleGrid };

        var rootGrid = Grid.Create(4, 3);
        rootGrid = rootGrid.SetCell(0, new Cell(new ItemStack(BagType, 1, ContainedBag: middle)));
        var root = new Bag(rootGrid);
        var hand = GameState.CreateHandBag();

        var registry = BagRegistry.Build(root, hand);

        Assert.Equal(4, registry.Count); // root, middle, deepest, hand
        Assert.NotNull(registry.GetById(deepest.Id));
    }

    [Fact]
    public void GetById_NotFound_ReturnsNull()
    {
        var root = new Bag(Grid.Create(4, 3));
        var hand = GameState.CreateHandBag();
        var registry = BagRegistry.Build(root, hand);

        Assert.Null(registry.GetById(Guid.NewGuid()));
    }

    [Fact]
    public void Contains_Existing_ReturnsTrue()
    {
        var root = new Bag(Grid.Create(4, 3));
        var hand = GameState.CreateHandBag();
        var registry = BagRegistry.Build(root, hand);

        Assert.True(registry.Contains(root.Id));
        Assert.False(registry.Contains(Guid.NewGuid()));
    }

    [Fact]
    public void Facilities_ReturnsBagsWithFacilityState()
    {
        var facility = new Bag(Grid.Create(1, 3), FacilityState: new FacilityState());
        var normalBag = new Bag(Grid.Create(2, 2));

        var rootGrid = Grid.Create(4, 3);
        rootGrid = rootGrid.SetCell(0, new Cell(new ItemStack(BagType, 1, ContainedBag: facility)));
        rootGrid = rootGrid.SetCell(1, new Cell(new ItemStack(BagType, 1, ContainedBag: normalBag)));
        var root = new Bag(rootGrid);
        var hand = GameState.CreateHandBag();

        var registry = BagRegistry.Build(root, hand);

        var facilities = registry.Facilities.ToList();
        Assert.Single(facilities);
        Assert.Equal(facility.Id, facilities[0].Id);
    }

    [Fact]
    public void Facilities_NoFacilities_ReturnsEmpty()
    {
        var root = new Bag(Grid.Create(4, 3));
        var hand = GameState.CreateHandBag();
        var registry = BagRegistry.Build(root, hand);

        Assert.Empty(registry.Facilities);
    }

    [Fact]
    public void GameState_Registry_AccessibleAndCorrect()
    {
        var inner = new Bag(Grid.Create(2, 2), "Cave");
        var rootGrid = Grid.Create(4, 3);
        rootGrid = rootGrid.SetCell(0, new Cell(new ItemStack(BagType, 1, ContainedBag: inner)));
        var root = new Bag(rootGrid);
        var state = new GameState(root, new Cursor(new Position(0, 0)),
            ImmutableArray<ItemType>.Empty, GameState.CreateHandBag());

        Assert.NotNull(state.Registry.GetById(inner.Id));
        Assert.Equal(3, state.Registry.Count);
    }

    [Fact]
    public void GameState_Registry_RebuildAfterMutation()
    {
        var root = new Bag(Grid.Create(4, 3));
        var state = new GameState(root, new Cursor(new Position(0, 0)),
            ImmutableArray<ItemType>.Empty, GameState.CreateHandBag());

        var registry1 = state.Registry;
        Assert.Equal(2, registry1.Count);

        // Mutate: add a nested bag
        var inner = new Bag(Grid.Create(2, 2));
        var newGrid = root.Grid.SetCell(0, new Cell(new ItemStack(BagType, 1, ContainedBag: inner)));
        var state2 = state with { RootBag = root with { Grid = newGrid } };

        // New state should have a fresh registry
        Assert.Equal(3, state2.Registry.Count);
        Assert.NotNull(state2.Registry.GetById(inner.Id));
    }

    [Fact]
    public void All_ReturnsAllBags()
    {
        var inner = new Bag(Grid.Create(2, 2));
        var rootGrid = Grid.Create(4, 3);
        rootGrid = rootGrid.SetCell(0, new Cell(new ItemStack(BagType, 1, ContainedBag: inner)));
        var root = new Bag(rootGrid);
        var hand = GameState.CreateHandBag();

        var registry = BagRegistry.Build(root, hand);
        var allIds = registry.All.Select(b => b.Id).ToHashSet();

        Assert.Contains(root.Id, allIds);
        Assert.Contains(inner.Id, allIds);
        Assert.Contains(hand.Id, allIds);
    }
}
