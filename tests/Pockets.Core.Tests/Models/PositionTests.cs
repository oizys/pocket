using Pockets.Core.Models;

namespace Pockets.Core.Tests.Models;

public class PositionTests
{
    [Fact]
    public void ToIndex_ReturnsRowTimesColumnsPlusCol()
    {
        var pos = new Position(2, 3);
        Assert.Equal(23, pos.ToIndex(10));
    }

    [Fact]
    public void ToIndex_ZeroPosition_ReturnsZero()
    {
        var pos = new Position(0, 0);
        Assert.Equal(0, pos.ToIndex(8));
    }

    [Fact]
    public void FromIndex_ReturnsCorrectPosition()
    {
        var pos = Position.FromIndex(23, 10);
        Assert.Equal(2, pos.Row);
        Assert.Equal(3, pos.Col);
    }

    [Fact]
    public void FromIndex_ZeroIndex_ReturnsOrigin()
    {
        var pos = Position.FromIndex(0, 8);
        Assert.Equal(new Position(0, 0), pos);
    }

    [Fact]
    public void RoundTrip_ToIndex_FromIndex()
    {
        var original = new Position(1, 5);
        int columns = 8;
        var roundTripped = Position.FromIndex(original.ToIndex(columns), columns);
        Assert.Equal(original, roundTripped);
    }

    [Fact]
    public void RoundTrip_FromIndex_ToIndex()
    {
        int columns = 10;
        int index = 12;
        var pos = Position.FromIndex(index, columns);
        Assert.Equal(index, pos.ToIndex(columns));
    }

    [Fact]
    public void Equality_SameValues_AreEqual()
    {
        var a = new Position(3, 7);
        var b = new Position(3, 7);
        Assert.Equal(a, b);
    }

    [Fact]
    public void Equality_DifferentValues_AreNotEqual()
    {
        var a = new Position(3, 7);
        var b = new Position(7, 3);
        Assert.NotEqual(a, b);
    }
}
