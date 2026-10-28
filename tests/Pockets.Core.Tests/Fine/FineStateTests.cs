using Pockets.Core.Fine;
using Pockets.Core.Models;
using Pockets.Core.Sim;

namespace Pockets.Core.Tests.Fine;

/// <summary>
/// Slice 2.6: the L2a store. <see cref="FineState"/> holds the bodies' sub-cell positions and velocities and the
/// stacks' offsets, in 1/16ths, ints only; it is sparse (a body at rest and a zero offset are not stored), compares by
/// content, and clamps offsets to ±<see cref="FineState.MaxOffset"/>. <see cref="FineText"/> is its canonical text.
/// </summary>
public class FineStateTests
{
    private static readonly Guid BagA = Guid.NewGuid();
    private static readonly Guid BagB = Guid.NewGuid();

    [Fact]
    public void Empty_IsEmpty_AndReadsZero()
    {
        var fine = FineState.Empty;
        Assert.True(fine.IsEmpty);
        Assert.Empty(fine.Offsets);
        Assert.Empty(fine.Bodies);
        Assert.Equal((0, 0), fine.OffsetAt(new CellRef(BagA, 3)));
        Assert.Equal(FineBody.Rest, fine.BodyAt(LocationId.B, BagA));
        Assert.True(FineBody.Rest.IsRest);
    }

    [Fact]
    public void WithOffset_StoresIt_AndClampsToMaxOffset()
    {
        var fine = FineState.Empty
            .WithOffset(new CellRef(BagA, 1), 2, -3)
            .WithOffset(new CellRef(BagA, 2), 40, -9);

        Assert.Equal(6, FineState.MaxOffset);
        Assert.Equal((2, -3), fine.OffsetAt(new CellRef(BagA, 1)));
        Assert.Equal((6, -6), fine.OffsetAt(new CellRef(BagA, 2)));
        Assert.False(fine.IsEmpty);
    }

    [Fact]
    public void WithOffset_Zero_RemovesIt()
    {
        var fine = FineState.Empty.WithOffset(new CellRef(BagA, 1), 2, 2).WithOffset(new CellRef(BagA, 1), 0, 0);
        Assert.True(fine.IsEmpty);
        Assert.Equal(FineState.Empty, fine);
    }

    [Fact]
    public void WithOffset_Unchanged_ReturnsTheSameInstance()
    {
        var fine = FineState.Empty.WithOffset(new CellRef(BagA, 1), 2, 2);
        Assert.Same(fine, fine.WithOffset(new CellRef(BagA, 1), 2, 2));
        Assert.Same(FineState.Empty, FineState.Empty.WithOffset(new CellRef(BagA, 1), 0, 0));
    }

    [Fact]
    public void WithBody_AtRest_IsNotStored()
    {
        var moved = FineState.Empty.WithBody(LocationId.B, BagA, new FineBody(3, -2, 0, 0, Direction8.E));
        Assert.Equal(new FineBody(3, -2, 0, 0, Direction8.E), moved.BodyAt(LocationId.B, BagA));
        Assert.Equal(FineBody.Rest, moved.BodyAt(LocationId.B, BagB));

        var rested = moved.WithBody(LocationId.B, BagA, FineBody.Rest);
        Assert.True(rested.IsEmpty);
        Assert.Same(FineState.Empty, FineState.Empty.WithBody(LocationId.B, BagA, FineBody.Rest));
    }

    [Fact]
    public void Equality_IsByContent_NotInsertionOrder()
    {
        var a = FineState.Empty.WithOffset(new CellRef(BagA, 1), 1, 1).WithOffset(new CellRef(BagB, 0), -1, 2)
            .WithBody(LocationId.B, BagA, new FineBody(1, 0, 0, 0, null));
        var b = FineState.Empty.WithBody(LocationId.B, BagA, new FineBody(1, 0, 0, 0, null))
            .WithOffset(new CellRef(BagB, 0), -1, 2).WithOffset(new CellRef(BagA, 1), 1, 1);

        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.NotEqual(a, b.WithOffset(new CellRef(BagA, 1), 1, 2));
    }

    [Fact]
    public void Direction8_HasEightCases_AndNoZeroMember()
    {
        Assert.Equal(new[] { "N", "NE", "E", "SE", "S", "SW", "W", "NW" }, Enum.GetNames<Direction8>());
        Assert.DoesNotContain(0, Enum.GetValues<Direction8>().Cast<int>());
    }

    [Fact]
    public void FineText_IsEmptyForEmpty()
    {
        Assert.Empty(FineText.Lines(FineState.Empty, new BagNames()));
    }

    [Fact]
    public void FineText_BodiesThenOffsets_NamedThroughTheStream()
    {
        var names = new BagNames();
        Assert.Equal("bag#1", names.Name(BagB)); // B was seen first in this stream
        Assert.Equal("bag#2", names.Name(BagA));
        var fine = FineState.Empty
            .WithOffset(new CellRef(BagA, 4), 2, -1)
            .WithOffset(new CellRef(BagB, 7), 0, 3)
            .WithOffset(new CellRef(BagB, 2), -6, 0)
            .WithBody(LocationId.B, BagA, new FineBody(3, -2, 4, 0, Direction8.E))
            .WithBody(LocationId.H, BagB, new FineBody(0, 1, 0, 0, null));

        Assert.Equal(
            new[]
            {
                "body loc=H bag=bag#1 px=0 py=1 vx=0 vy=0 held=-",
                "body loc=B bag=bag#2 px=3 py=-2 vx=4 vy=0 held=E",
                "offset cell=bag#1[2] dx=-6 dy=0",
                "offset cell=bag#1[7] dx=0 dy=3",
                "offset cell=bag#2[4] dx=2 dy=-1",
            },
            FineText.Lines(fine, names));
    }
}
