using System.Collections.Immutable;
using Pockets.Core.Dsl;
using Pockets.Core.Models;

namespace Pockets.Core.Tests.Dsl;

public class QueryTests
{
    private static readonly ItemType Rock = new("Rock", Category.Material, IsStackable: true, MaxStackSize: 20);
    private static readonly ItemType Sword = new("Sword", Category.Weapon, IsStackable: false);
    private static readonly ImmutableArray<ItemType> Types = ImmutableArray.Create(Rock, Sword);

    private static GameState MakeState(params (int index, ItemStack stack)[] items)
    {
        var grid = Grid.Create(4, 2);
        foreach (var (index, stack) in items)
            grid = grid.SetCell(index, new Cell(stack));
        var rootBag = new Bag(grid);
        var handBag = GameState.CreateHandBag();
        var store = BagStore.Empty.Add(rootBag).Add(handBag);
        return new GameState(store, LocationMap.Create(handBag.Id, rootBag.Id), Types);
    }

    private static ImmutableStack<object> RunStack(GameState game, string program)
    {
        var stack = ImmutableStack<object>.Empty.Push(OpResult.Initial(game));
        return DslInterpreter.Run(stack, program);
    }

    private static T TopValue<T>(ImmutableStack<object> stack)
    {
        Assert.False(stack.IsEmpty);
        Assert.IsType<T>(stack.Peek());
        return (T)stack.Peek();
    }

    // ==================== Query opcodes ====================

    [Fact]
    public void HandEmpty_True_WhenEmpty()
    {
        var stack = RunStack(MakeState(), "hand-empty?");
        Assert.True(TopValue<bool>(stack));
    }

    [Fact]
    public void HandEmpty_False_WhenHolding()
    {
        var state = MakeState((0, new ItemStack(Rock, 5)));
        var stack = RunStack(state, "grab hand-empty?");
        Assert.False(TopValue<bool>(stack));
    }

    [Fact]
    public void CellEmpty_True_WhenEmpty()
    {
        var stack = RunStack(MakeState(), "cell-empty?");
        Assert.True(TopValue<bool>(stack));
    }

    [Fact]
    public void CellEmpty_False_WhenOccupied()
    {
        var stack = RunStack(MakeState((0, new ItemStack(Rock, 5))), "cell-empty?");
        Assert.False(TopValue<bool>(stack));
    }

    [Fact]
    public void CellHasBag_True()
    {
        var innerBag = new Bag(Grid.Create(2, 2));
        var bagType = new ItemType("Pouch", Category.Bag, IsStackable: false);
        var state = MakeState((0, new ItemStack(bagType, 1, ContainedBagId: innerBag.Id)));
        // Add the inner bag to the store
        state = state with { Store = state.Store.Add(innerBag) };
        var stack = RunStack(state, "cell-has-bag?");
        Assert.True(TopValue<bool>(stack));
    }

    [Fact]
    public void CellHasBag_False()
    {
        var stack = RunStack(MakeState((0, new ItemStack(Rock, 5))), "cell-has-bag?");
        Assert.False(TopValue<bool>(stack));
    }

    [Fact]
    public void Nested_False_AtRoot()
    {
        var stack = RunStack(MakeState(), "nested?");
        Assert.False(TopValue<bool>(stack));
    }

    [Fact]
    public void SameType_True()
    {
        var state = MakeState((0, new ItemStack(Rock, 5)), (1, new ItemStack(Rock, 3)));
        // Grab from cell 0 (Rock into hand), move to cell 1 (also Rock)
        var stack = RunStack(state, "grab right same-type?");
        Assert.True(TopValue<bool>(stack));
    }

    [Fact]
    public void SameType_False_DifferentTypes()
    {
        var state = MakeState((0, new ItemStack(Rock, 5)), (1, new ItemStack(Sword, 1)));
        var stack = RunStack(state, "grab right same-type?");
        Assert.False(TopValue<bool>(stack));
    }

    [Fact]
    public void OutputSlot_False_NormalCell()
    {
        var stack = RunStack(MakeState(), "output-slot?");
        Assert.False(TopValue<bool>(stack));
    }

    [Fact]
    public void CellCount_ReturnsCount()
    {
        var stack = RunStack(MakeState((0, new ItemStack(Rock, 7))), "cell-count");
        Assert.Equal(7, TopValue<int>(stack));
    }

    [Fact]
    public void CellCount_Zero_WhenEmpty()
    {
        var stack = RunStack(MakeState(), "cell-count");
        Assert.Equal(0, TopValue<int>(stack));
    }

    // ==================== Logic opcodes ====================

    [Fact]
    public void And_TrueTrue()
    {
        var stack = RunStack(MakeState(), "true true and");
        Assert.True(TopValue<bool>(stack));
    }

    [Fact]
    public void And_TrueFalse()
    {
        var stack = RunStack(MakeState(), "true false and");
        Assert.False(TopValue<bool>(stack));
    }

    [Fact]
    public void Or_FalseTrue()
    {
        var stack = RunStack(MakeState(), "false true or");
        Assert.True(TopValue<bool>(stack));
    }

    [Fact]
    public void Not_True()
    {
        var stack = RunStack(MakeState(), "true not");
        Assert.False(TopValue<bool>(stack));
    }

    [Fact]
    public void Not_False()
    {
        var stack = RunStack(MakeState(), "false not");
        Assert.True(TopValue<bool>(stack));
    }

    // ==================== Combinators ====================

    [Fact]
    public void When_RunsBody_IfTrue()
    {
        var state = MakeState((0, new ItemStack(Rock, 5)));
        var result = DslInterpreter.RunProgram(state, "cell-empty? not [ grab ] when");
        Assert.True(result.State.HasItemsInHand);
    }

    [Fact]
    public void When_SkipsBody_IfFalse()
    {
        var state = MakeState(); // empty cell
        var result = DslInterpreter.RunProgram(state, "cell-empty? not [ grab ] when");
        Assert.False(result.State.HasItemsInHand);
    }

    [Fact]
    public void Unless_RunsBody_IfFalse()
    {
        var state = MakeState((0, new ItemStack(Rock, 5)));
        var result = DslInterpreter.RunProgram(state, "cell-empty? [ grab ] unless");
        Assert.True(result.State.HasItemsInHand);
    }

    [Fact]
    public void Cond_FirstMatch_Wins()
    {
        // cell-empty? is false (has item), so first test fails.
        // true always matches, so grab runs.
        var state = MakeState((0, new ItemStack(Rock, 5)));
        var result = DslInterpreter.RunProgram(state,
            "[ [ cell-empty? ] [ sort ] [ true ] [ grab ] ] cond");
        Assert.True(result.State.HasItemsInHand);
        Assert.True(result.State.CurrentCell.IsEmpty);
    }

    [Fact]
    public void Cond_NoMatch_NoOp()
    {
        var state = MakeState();
        var result = DslInterpreter.RunProgram(state,
            "[ [ cell-has-bag? ] [ enter ] ] cond");
        // No bag, so cond doesn't execute anything
        Assert.False(result.State.IsNested);
    }

    [Fact]
    public void Cond_MultipleTests_StopsAtFirst()
    {
        var state = MakeState((0, new ItemStack(Rock, 5)));
        // Both tests would match, but cond stops at first
        var result = DslInterpreter.RunProgram(state,
            "[ [ cell-empty? not ] [ grab ] [ true ] [ sort ] ] cond");
        // grab should have run, not sort
        Assert.True(result.State.HasItemsInHand);
    }

    // ==================== Compound queries ====================

    [Fact]
    public void CompoundQuery_HandEmptyAndCellNotEmpty()
    {
        var state = MakeState((0, new ItemStack(Rock, 5)));
        var stack = RunStack(state, "hand-empty? cell-empty? not and");
        Assert.True(TopValue<bool>(stack));
    }

    [Fact]
    public void Query_DoesNotMutateState()
    {
        var state = MakeState((0, new ItemStack(Rock, 5)));
        var result = DslInterpreter.RunProgram(state, "hand-empty? cell-empty? cell-count");
        // State should be unchanged
        Assert.Equal(state, result.State);
        Assert.Equal(5, result.State.CurrentCell.Stack!.Count);
    }
}
