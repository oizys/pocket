using System.Collections.Immutable;
using Pockets.Core.Dsl;
using Pockets.Core.Models;

namespace Pockets.Core.Tests.Dsl;

public class CoercionTests
{
    private static readonly ItemType Rock = new("Rock", Category.Material, IsStackable: true);
    private static readonly ImmutableArray<ItemType> Types = ImmutableArray.Create(Rock);

    private static GameState MakeState(Grid? grid = null, ItemStack? cursorItem = null)
    {
        grid ??= Grid.Create(4, 2);
        if (cursorItem is not null)
            grid = grid.SetCell(0, new Cell(cursorItem));
        var rootBag = new Bag(grid);
        var handBag = GameState.CreateHandBag();
        var store = BagStore.Empty.Add(rootBag).Add(handBag);
        return new GameState(store, LocationMap.Create(handBag.Id, rootBag.Id), Types);
    }

    [Fact]
    public void Resolve_LocationLevel_ReturnsLocation()
    {
        var state = MakeState();
        var resolved = Coercion.Resolve(LocationId.B, AccessLevel.Location, state);

        Assert.IsType<Location>(resolved.Value);
        Assert.Equal(LocationId.B, resolved.LocationId);
    }

    [Fact]
    public void Resolve_BagLevel_ReturnsBag()
    {
        var state = MakeState();
        var resolved = Coercion.Resolve(LocationId.B, AccessLevel.Bag, state);

        Assert.IsType<Bag>(resolved.Value);
        var bag = (Bag)resolved.Value;
        Assert.Equal(4, bag.Grid.Columns);
        Assert.Equal(2, bag.Grid.Rows);
    }

    [Fact]
    public void Resolve_CellLevel_ReturnsCell()
    {
        var state = MakeState(cursorItem: new ItemStack(Rock, 5));
        var resolved = Coercion.Resolve(LocationId.B, AccessLevel.Cell, state);

        Assert.IsType<Cell>(resolved.Value);
        var cell = (Cell)resolved.Value;
        Assert.False(cell.IsEmpty);
        Assert.Equal(5, cell.Stack!.Count);
    }

    [Fact]
    public void Resolve_StackLevel_ReturnsItemStack()
    {
        var state = MakeState(cursorItem: new ItemStack(Rock, 5));
        var resolved = Coercion.Resolve(LocationId.B, AccessLevel.Stack, state);

        Assert.IsType<ItemStack>(resolved.Value);
        var stack = (ItemStack)resolved.Value;
        Assert.Equal("Rock", stack.ItemType.Name);
        Assert.Equal(5, stack.Count);
    }

    [Fact]
    public void Resolve_StackLevel_EmptyCell_Throws()
    {
        var state = MakeState();
        var ex = Assert.Throws<InvalidOperationException>(
            () => Coercion.Resolve(LocationId.B, AccessLevel.Stack, state));
        Assert.Contains("empty", ex.Message);
    }

    [Fact]
    public void Resolve_IndexLevel_ReturnsPosition()
    {
        var state = MakeState();
        var resolved = Coercion.Resolve(LocationId.B, AccessLevel.Index, state);

        Assert.IsType<Position>(resolved.Value);
        Assert.Equal(new Position(0, 0), (Position)resolved.Value);
    }

    [Fact]
    public void Resolve_InactiveLocation_Throws()
    {
        var state = MakeState();
        var ex = Assert.Throws<InvalidOperationException>(
            () => Coercion.Resolve(LocationId.W, AccessLevel.Bag, state));
        Assert.Contains("not active", ex.Message);
    }

    [Fact]
    public void Resolve_HandLocation_ReturnsBag()
    {
        var state = MakeState();
        var resolved = Coercion.Resolve(LocationId.H, AccessLevel.Bag, state);

        Assert.IsType<Bag>(resolved.Value);
        var bag = (Bag)resolved.Value;
        Assert.Equal(1, bag.Grid.Columns); // hand is 1-slot
    }

    [Fact]
    public void Resolve_NestedLocation_FollowsBreadcrumbs()
    {
        var innerBag = new Bag(Grid.Create(3, 3), "Forest");
        var innerType = new ItemType("Forest Bag", Category.Bag, IsStackable: false);
        var rootGrid = Grid.Create(4, 2).SetCell(0, new Cell(new ItemStack(innerType, 1, ContainedBagId: innerBag.Id)));
        var rootBag = new Bag(rootGrid);
        var handBag = GameState.CreateHandBag();
        var store = BagStore.Empty.Add(rootBag).Add(handBag).Add(innerBag);

        // Create location with breadcrumb (entered bag at cell 0)
        var breadcrumbs = ImmutableStack<BreadcrumbEntry>.Empty
            .Push(new BreadcrumbEntry(0, new Cursor(new Position(0, 0))));
        var bLocation = new Location(rootBag.Id, new Cursor(new Position(1, 1)), breadcrumbs);
        var locations = LocationMap.Create(handBag.Id, rootBag.Id)
            .Set(LocationId.B, bLocation);

        var state = new GameState(store, locations, ImmutableArray.Create(innerType));

        var resolved = Coercion.Resolve(LocationId.B, AccessLevel.Bag, state);
        var bag = (Bag)resolved.Value;
        Assert.Equal("Forest", bag.EnvironmentType);
        Assert.Equal(3, bag.Grid.Columns);
    }

    [Fact]
    public void ResolvedParam_CarriesBagIdAndCellIndex()
    {
        var state = MakeState(cursorItem: new ItemStack(Rock, 5));
        var resolved = Coercion.Resolve(LocationId.B, AccessLevel.Cell, state);

        Assert.Equal(state.RootBagId, resolved.BagId);
        Assert.Equal(0, resolved.CellIndex); // cursor at (0,0) = index 0
    }
}
