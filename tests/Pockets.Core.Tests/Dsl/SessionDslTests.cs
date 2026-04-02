using System.Collections.Immutable;
using Pockets.Core.Dsl;
using Pockets.Core.Models;

namespace Pockets.Core.Tests.Dsl;

/// <summary>
/// Tests that GameSession.Execute(string dsl) correctly dispatches DSL programs
/// with undo, logging, and facility ticking integration.
/// </summary>
public class SessionDslTests
{
    private static readonly ItemType Rock = new("Rock", Category.Material, IsStackable: true, MaxStackSize: 20);
    private static readonly ItemType Sword = new("Sword", Category.Weapon, IsStackable: false);
    private static readonly ImmutableArray<ItemType> Types = ImmutableArray.Create(Rock, Sword);

    private static GameSession MakeSession(params (int index, ItemStack stack)[] items)
    {
        var grid = Grid.Create(4, 2);
        foreach (var (index, stack) in items)
            grid = grid.SetCell(index, new Cell(stack));
        var rootBag = new Bag(grid);
        var handBag = GameState.CreateHandBag();
        var store = BagStore.Empty.Add(rootBag).Add(handBag);
        var state = new GameState(store, LocationMap.Create(handBag.Id, rootBag.Id), Types);
        return GameSession.New(state);
    }

    [Fact]
    public void Execute_GrabDrop_UpdatesState()
    {
        var session = MakeSession((0, new ItemStack(Rock, 5)));

        session = session.Execute("grab right drop");

        Assert.True(session.Current.RootBag.Grid.GetCell(0).IsEmpty);
        Assert.Equal(5, session.Current.RootBag.Grid.GetCell(1).Stack!.Count);
        Assert.False(session.Current.HasItemsInHand);
    }

    [Fact]
    public void Execute_IsUndoable()
    {
        var session = MakeSession((0, new ItemStack(Rock, 5)));
        var original = session.Current;

        session = session.Execute("grab");
        Assert.True(session.CanUndo);

        session = session.Undo()!;
        Assert.Equal(5, session.Current.RootBag.Grid.GetCell(0).Stack!.Count);
        Assert.False(session.Current.HasItemsInHand);
    }

    [Fact]
    public void Execute_LogsDslExpression()
    {
        var session = MakeSession((0, new ItemStack(Rock, 5)));

        session = session.Execute("grab");

        Assert.Contains("grab", session.ActionLog[^1]);
    }

    [Fact]
    public void Execute_NoOp_DoesNotPushUndo()
    {
        var session = MakeSession(); // empty grid

        session = session.Execute("sort"); // sorting empty grid is a no-op

        Assert.False(session.CanUndo);
    }

    [Fact]
    public void Execute_InvalidOpcode_LogsError()
    {
        var session = MakeSession();

        session = session.Execute("nonexistent-opcode");

        Assert.Contains("FAILED", session.ActionLog[^1]);
    }

    [Fact]
    public void Execute_Sort_MatchesDirectApi()
    {
        var session = MakeSession(
            (0, new ItemStack(Sword, 1)),
            (1, new ItemStack(Rock, 5)),
            (2, new ItemStack(Rock, 3)));

        var directSession = MakeSession(
            (0, new ItemStack(Sword, 1)),
            (1, new ItemStack(Rock, 5)),
            (2, new ItemStack(Rock, 3)));

        session = session.Execute("sort");
        directSession = directSession.ExecuteSort();

        for (int i = 0; i < 8; i++)
        {
            var dslCell = session.Current.RootBag.Grid.GetCell(i);
            var directCell = directSession.Current.RootBag.Grid.GetCell(i);
            Assert.Equal(directCell.IsEmpty, dslCell.IsEmpty);
            if (!directCell.IsEmpty)
            {
                Assert.Equal(directCell.Stack!.ItemType.Name, dslCell.Stack!.ItemType.Name);
                Assert.Equal(directCell.Stack.Count, dslCell.Stack.Count);
            }
        }
    }

    [Fact]
    public void Execute_Navigation_ThenGrab()
    {
        var session = MakeSession((2, new ItemStack(Rock, 5)));

        session = session.Execute("right right grab");

        Assert.Equal(new Position(0, 2), session.Current.Cursor.Position);
        Assert.True(session.Current.HasItemsInHand);
        Assert.True(session.Current.RootBag.Grid.GetCell(2).IsEmpty);
    }

    [Fact]
    public void HandleDsl_OnController()
    {
        var session = MakeSession((0, new ItemStack(Rock, 5)));
        var controller = new GameController(session);

        var result = controller.HandleDsl("grab right drop");

        Assert.True(result.Handled);
        Assert.True(controller.Session.Current.RootBag.Grid.GetCell(0).IsEmpty);
        Assert.Equal(5, controller.Session.Current.RootBag.Grid.GetCell(1).Stack!.Count);
    }
}
