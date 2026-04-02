using System.Collections.Immutable;
using Pockets.Core.Dsl;
using Pockets.Core.Models;

namespace Pockets.Core.Tests.Dsl;

public class DslStateTests
{
    private static GameState MakeState()
    {
        var rootBag = new Bag(Grid.Create(4, 2));
        var handBag = GameState.CreateHandBag();
        var store = BagStore.Empty.Add(rootBag).Add(handBag);
        return new GameState(store, LocationMap.Create(handBag.Id, rootBag.Id), ImmutableArray<ItemType>.Empty);
    }

    [Fact]
    public void From_CreatesEmptyStack()
    {
        var state = DslState.From(MakeState());
        Assert.True(state.IsStackEmpty);
    }

    [Fact]
    public void Push_Pop_RoundTrips()
    {
        var state = DslState.From(MakeState());
        state = state.Push(42);
        var (value, newState) = state.Pop<int>();
        Assert.Equal(42, value);
        Assert.True(newState.IsStackEmpty);
    }

    [Fact]
    public void Push_Multiple_LIFO()
    {
        var state = DslState.From(MakeState())
            .Push(1)
            .Push(2)
            .Push(3);

        var (v1, s1) = state.Pop<int>();
        var (v2, s2) = s1.Pop<int>();
        var (v3, s3) = s2.Pop<int>();

        Assert.Equal(3, v1);
        Assert.Equal(2, v2);
        Assert.Equal(1, v3);
        Assert.True(s3.IsStackEmpty);
    }

    [Fact]
    public void Pop_EmptyStack_Throws()
    {
        var state = DslState.From(MakeState());
        Assert.Throws<InvalidOperationException>(() => state.Pop<int>());
    }

    [Fact]
    public void Pop_WrongType_Throws()
    {
        var state = DslState.From(MakeState()).Push("hello");
        Assert.Throws<InvalidOperationException>(() => state.Pop<int>());
    }

    [Fact]
    public void Peek_ReturnsTop()
    {
        var state = DslState.From(MakeState()).Push(42);
        Assert.Equal(42, state.Peek());
    }

    [Fact]
    public void Peek_EmptyStack_ReturnsNull()
    {
        var state = DslState.From(MakeState());
        Assert.Null(state.Peek());
    }

    [Fact]
    public void Push_LocationId_RoundTrips()
    {
        var state = DslState.From(MakeState()).Push(LocationId.W);
        var (value, _) = state.Pop<LocationId>();
        Assert.Equal(LocationId.W, value);
    }
}
