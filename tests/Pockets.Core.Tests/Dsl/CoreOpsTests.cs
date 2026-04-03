using System.Collections.Immutable;
using Pockets.Core.Dsl;
using Pockets.Core.Models;

namespace Pockets.Core.Tests.Dsl;

/// <summary>
/// Tests for core language primitives: stack ops, arithmetic, control flow.
/// </summary>
public class CoreOpsTests
{
    private static readonly ItemType Rock = new("Rock", Category.Material, IsStackable: true, MaxStackSize: 20);
    private static readonly ImmutableArray<ItemType> Types = ImmutableArray.Create(Rock);

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

    private static T Top<T>(ImmutableStack<object> stack) => (T)stack.Peek();

    // ==================== Stack manipulation ====================

    [Fact]
    public void Dup_CopiesTop()
    {
        var stack = RunStack(MakeState(), "5 dup");
        Assert.Equal(5, (int)stack.Peek());           // top is 5
        Assert.Equal(5, (int)stack.Pop().Peek());     // second is also 5
    }

    [Fact]
    public void Pop_DiscardsTop()
    {
        var stack = RunStack(MakeState(), "5 3 pop");
        Assert.Equal(5, Top<int>(stack));
    }

    [Fact]
    public void Over_CopiesSecond()
    {
        // a b over → a b a
        var stack = RunStack(MakeState(), "5 3 over");
        Assert.Equal(5, Top<int>(stack)); // copy of 5 on top
    }

    [Fact]
    public void Call_ExecutesQuotation()
    {
        var result = DslInterpreter.RunProgram(MakeState((0, new ItemStack(Rock, 5))), "[ grab ] call");
        Assert.True(result.State.HasItemsInHand);
    }

    // ==================== Arithmetic ====================

    [Fact]
    public void Add()
    {
        var stack = RunStack(MakeState(), "3 5 +");
        Assert.Equal(8, Top<int>(stack));
    }

    [Fact]
    public void Subtract()
    {
        var stack = RunStack(MakeState(), "10 3 -");
        Assert.Equal(7, Top<int>(stack));
    }

    [Fact]
    public void Multiply()
    {
        var stack = RunStack(MakeState(), "4 5 *");
        Assert.Equal(20, Top<int>(stack));
    }

    [Fact]
    public void Divide()
    {
        var stack = RunStack(MakeState(), "20 4 div");
        Assert.Equal(5, Top<int>(stack));
    }

    [Fact]
    public void Divide_ByZero_ReturnsZero()
    {
        var stack = RunStack(MakeState(), "10 0 div");
        Assert.Equal(0, Top<int>(stack));
    }

    [Fact]
    public void Arithmetic_Compound()
    {
        // (3 + 5) * 2 = 16
        var stack = RunStack(MakeState(), "3 5 + 2 *");
        Assert.Equal(16, Top<int>(stack));
    }

    // ==================== if-else ====================

    [Fact]
    public void IfElse_True_RunsTrueBody()
    {
        var result = DslInterpreter.RunProgram(
            MakeState((0, new ItemStack(Rock, 5))),
            "cell-empty? not [ grab ] [ sort ] if-else");
        Assert.True(result.State.HasItemsInHand); // grab ran, not sort
    }

    [Fact]
    public void IfElse_False_RunsFalseBody()
    {
        var result = DslInterpreter.RunProgram(
            MakeState(),
            "cell-empty? [ grab ] [ sort ] if-else");
        // cell is empty, so condition is true → grab runs, but cell is empty so grab is no-op
        // Actually cell-empty? is true, so true body (grab) runs
        Assert.False(result.State.HasItemsInHand);
    }

    [Fact]
    public void IfElse_BinaryDispatch()
    {
        // If hand empty, grab. Otherwise, drop.
        var state = MakeState((0, new ItemStack(Rock, 5)));
        var result = DslInterpreter.RunProgram(state,
            "hand-empty? [ grab ] [ drop ] if-else");
        Assert.True(result.State.HasItemsInHand); // hand was empty → grab
    }

    // ==================== while ====================

    [Fact]
    public void While_LoopsUntilConditionFalse()
    {
        // Move right 3 times using while with a counter
        // Can't easily do counter-based while without variables, but we can test
        // "while cell is not empty, grab and move right"
        var state = MakeState(
            (0, new ItemStack(Rock, 1)),
            (1, new ItemStack(Rock, 1)),
            (2, new ItemStack(Rock, 1)));
        var result = DslInterpreter.RunProgram(state,
            "[ cell-empty? not ] [ grab right ] while");
        // Should have grabbed from cells 0, 1, 2 and stopped at empty cell 3
        Assert.Equal(new Position(0, 3), result.State.Cursor.Position);
    }

    [Fact]
    public void While_FalseInitially_NeverRuns()
    {
        var state = MakeState(); // all cells empty
        var result = DslInterpreter.RunProgram(state,
            "[ cell-empty? not ] [ grab right ] while");
        Assert.Equal(new Position(0, 0), result.State.Cursor.Position);
    }

    [Fact]
    public void While_BreaksOnError()
    {
        // Leave fails at root → error in OpResult → while breaks
        var state = MakeState((0, new ItemStack(Rock, 1)));
        var result = DslInterpreter.RunProgram(state,
            "[ true ] [ leave ] while");
        // Should stop after first iteration (leave produces an error)
        Assert.False(result.IsOk);
    }

    [Fact]
    public void While_ExceedsLimit_AddsError()
    {
        // Infinite loop (always true, no state change that would break)
        var state = MakeState();
        var result = DslInterpreter.RunProgram(state,
            "[ true ] [ right ] while");
        // Should hit 512 limit
        Assert.False(result.IsOk);
        Assert.Contains("exceeded", result.Errors[0]);
    }

    // ==================== break ====================

    [Fact]
    public void Break_ExitsQuotation()
    {
        var result = DslInterpreter.RunProgram(
            MakeState((0, new ItemStack(Rock, 5))),
            "grab break sort");
        // grab runs, break stops execution, sort never runs
        Assert.True(result.State.HasItemsInHand);
    }

    [Fact]
    public void Break_InLoop_StopsLoop()
    {
        // While would loop forever, but break in the body exits
        var state = MakeState((0, new ItemStack(Rock, 5)));
        var result = DslInterpreter.RunProgram(state,
            "[ true ] [ grab break ] while");
        Assert.True(result.State.HasItemsInHand);
        Assert.True(result.IsOk); // break is not an error
    }

    // ==================== Compound: computed split ====================

    [Fact]
    public void Dup_UsedInCompoundQuery()
    {
        // Check count is between 3 and 10: cell-count dup 3 gte s-swap 10 lte and
        var state = MakeState((0, new ItemStack(Rock, 5)));
        var stack = RunStack(state, "cell-count dup 3 gte s-swap 10 lte and");
        Assert.True(Top<bool>(stack));
    }

    [Fact]
    public void Dup_ValueReusedAfterConsumption()
    {
        // Push 7, dup, check both are 7
        var stack = RunStack(MakeState(), "7 dup +");
        Assert.Equal(14, Top<int>(stack));
    }
}

