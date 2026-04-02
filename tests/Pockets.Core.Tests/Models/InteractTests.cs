using System.Collections.Immutable;
using Pockets.Core.Models;

namespace Pockets.Core.Tests.Models;

public class InteractTests
{
    private static readonly ItemType Rck = new("Rck", Category.Material, IsStackable: true, MaxStackSize: 9);
    private static readonly ItemType Swd = new("Swd", Category.Weapon, IsStackable: false);
    private static readonly ItemType SmallBag = new("Small Bag", Category.Bag, IsStackable: false);

    private static readonly ImmutableArray<ItemType> AllTypes =
        ImmutableArray.Create(Rck, Swd, SmallBag);

    private static GameState CreateWithBag(
        IEnumerable<ItemStack>? innerContents = null,
        IEnumerable<ItemStack>? rootContents = null)
    {
        var innerGrid = Grid.Create(3, 2);
        var innerBag = new Bag(innerGrid, "Cave", "Dark");
        if (innerContents != null)
        {
            var (filled, _) = innerBag.AcquireItems(innerContents);
            innerBag = filled;
        }

        var rootGrid = Grid.Create(4, 3);
        var bagCell = new Cell(new ItemStack(SmallBag, 1, ContainedBagId: innerBag.Id));
        rootGrid = rootGrid.SetCell(0, bagCell);

        if (rootContents != null)
        {
            var (filledGrid, _) = rootGrid.AcquireItems(rootContents,
                ImmutableHashSet.Create(0));
            rootGrid = filledGrid;
        }

        var rootBag = new Bag(rootGrid);
        var handBag = GameState.CreateHandBag();
        var store = BagStore.Empty.Add(rootBag).Add(handBag).Add(innerBag);
        return new GameState(store, LocationMap.Create(handBag.Id, rootBag.Id), AllTypes);
    }

    [Fact]
    public void Interact_OnBag_EntersBag()
    {
        var state = CreateWithBag();
        var result = state.Interact();

        Assert.True(result.Success);
        Assert.True(result.State.IsNested);
        Assert.Equal(3, result.State.ActiveBag.Grid.Columns);
    }

    [Fact]
    public void Interact_OnEmptyCell_NoOp()
    {
        var state = CreateWithBag();
        state = state.MoveCursor(Direction.Right); // move to empty cell
        var result = state.Interact();

        Assert.True(result.Success);
        Assert.Equal(state, result.State);
    }

    [Fact]
    public void Interact_OnItemAtRoot_NoOp()
    {
        // Non-bag item at root — interact does nothing (harvest only works when nested)
        var rootItems = new[] { new ItemStack(Rck, 5) };
        var state = CreateWithBag(rootContents: rootItems);
        state = state.MoveCursor(Direction.Right); // move to Rck
        var result = state.Interact();

        Assert.True(result.Success);
        Assert.Equal(state, result.State);
    }

    [Fact]
    public void Interact_OnItemWhenNested_Harvests()
    {
        var innerItems = new[] { new ItemStack(Rck, 3) };
        var state = CreateWithBag(innerContents: innerItems);

        // Enter the bag
        var entered = state.Interact().State;
        Assert.True(entered.IsNested);

        // Interact on the rock — should harvest
        var harvested = entered.Interact();
        Assert.True(harvested.Success);

        // Item should be gone from inner bag
        Assert.True(harvested.State.ActiveBag.Grid.GetCell(0).IsEmpty);

        // Item should be in parent bag
        var rootCells = harvested.State.RootBag.Grid.Cells
            .Where(c => !c.IsEmpty && c.Stack!.ItemType == Rck)
            .ToList();
        Assert.NotEmpty(rootCells);
    }

    [Fact]
    public void Interact_OnBagWhenNested_EntersDeeperBag()
    {
        // Create a bag inside a bag inside the root
        var deepBag = new Bag(Grid.Create(2, 2), "Deep");
        var midGrid = Grid.Create(3, 2);
        midGrid = midGrid.SetCell(0, new Cell(new ItemStack(SmallBag, 1, ContainedBagId: deepBag.Id)));
        var midBag = new Bag(midGrid, "Mid");

        var rootGrid = Grid.Create(4, 3);
        rootGrid = rootGrid.SetCell(0, new Cell(new ItemStack(SmallBag, 1, ContainedBagId: midBag.Id)));
        var rootBag = new Bag(rootGrid);
        var handBag = GameState.CreateHandBag();
        var store = BagStore.Empty.Add(rootBag).Add(handBag).Add(midBag).Add(deepBag);
        var state = new GameState(store, LocationMap.Create(handBag.Id, rootBag.Id), AllTypes);

        // Interact enters mid bag
        var entered1 = state.Interact().State;
        Assert.True(entered1.IsNested);
        Assert.Equal(3, entered1.ActiveBag.Grid.Columns);

        // Interact again enters deep bag
        var entered2 = entered1.Interact().State;
        Assert.Equal(2, entered2.ActiveBag.Grid.Columns);
    }

    [Fact]
    public void Session_Interact_Undoable()
    {
        var innerItems = new[] { new ItemStack(Rck, 3) };
        var state = CreateWithBag(innerContents: innerItems);
        var session = GameSession.New(state);

        // Interact enters bag
        session = session.ExecuteInteract();
        Assert.True(session.Current.IsNested);
        Assert.Equal(1, session.UndoDepth);

        // Interact harvests
        session = session.ExecuteInteract();
        Assert.True(session.Current.ActiveBag.Grid.GetCell(0).IsEmpty);
        Assert.Equal(2, session.UndoDepth);

        // Undo harvest
        session = session.Undo()!;
        Assert.False(session.Current.ActiveBag.Grid.GetCell(0).IsEmpty);

        // Undo enter
        session = session.Undo()!;
        Assert.False(session.Current.IsNested);
    }
}
