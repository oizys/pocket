using System.Collections.Immutable;
using Pockets.Core.Dsl;
using Pockets.Core.Models;

namespace Pockets.Core.Tests.Dsl;

public class OpResultTests
{
    private static GameState MakeState()
    {
        var rootBag = new Bag(Grid.Create(4, 2));
        var handBag = GameState.CreateHandBag();
        var store = BagStore.Empty.Add(rootBag).Add(handBag);
        return new GameState(store, LocationMap.Create(handBag.Id, rootBag.Id), ImmutableArray<ItemType>.Empty);
    }

    [Fact]
    public void Initial_SetsBeforeAndState()
    {
        var state = MakeState();
        var result = OpResult.Initial(state);
        Assert.Equal(state, result.State);
        Assert.Equal(state, result.Before);
        Assert.True(result.IsOk);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Chain_UpdatesState_PreservesBefore()
    {
        var state1 = MakeState();
        var state2 = MakeState(); // different instance
        var result = OpResult.Initial(state1).Chain(state2);

        Assert.Equal(state2, result.State);
        Assert.Equal(state1, result.Before);
        Assert.True(result.IsOk);
    }

    [Fact]
    public void ChainError_AccumulatesErrors()
    {
        var state = MakeState();
        var result = OpResult.Initial(state)
            .ChainError("error 1")
            .ChainError("error 2");

        Assert.False(result.IsOk);
        Assert.Equal(2, result.Errors.Count);
        Assert.Equal("error 1", result.Errors[0]);
        Assert.Equal("error 2", result.Errors[1]);
    }

    [Fact]
    public void ChainError_PreservesState()
    {
        var state = MakeState();
        var result = OpResult.Initial(state).ChainError("oops");
        Assert.Equal(state, result.State);
    }

    [Fact]
    public void ClearErrors_RemovesAllErrors()
    {
        var state = MakeState();
        var result = OpResult.Initial(state)
            .ChainError("error 1")
            .ChainError("error 2")
            .ClearErrors();

        Assert.True(result.IsOk);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void RunProgram_ReturnsOpResult()
    {
        var state = MakeState();
        var result = DslInterpreter.RunProgram(state, "right");
        Assert.Equal(state, result.Before);
        Assert.NotEqual(state, result.State); // cursor moved
    }

    [Fact]
    public void RunProgram_EmptyProgram_NoChange()
    {
        var state = MakeState();
        var result = DslInterpreter.RunProgram(state, "");
        Assert.Equal(state, result.State);
        Assert.Equal(state, result.Before);
    }
}
