using Pockets.Core.Cosmology;

namespace Pockets.Core.Tests.Cosmology;

/// <summary>
/// The chirality / orientation math: the 3-bit encoding is a bijection onto the
/// 8 distinct D4 flips, reading labels are correct, and the transpose relation
/// (used by the negative sub-zones) is an involution that reverses the reading pair.
/// </summary>
public class OrientationTests
{
    [Fact]
    public void All_Produces8DistinctOrientations()
    {
        var all = Orientation.All.ToList();
        Assert.Equal(8, all.Count);
        Assert.Equal(8, all.Distinct().Count());
    }

    [Fact]
    public void CodeRoundTrips_ForAll8()
    {
        foreach (var o in Orientation.All)
            Assert.Equal(o, Orientation.FromCode(o.Code));

        Assert.Equal(Enumerable.Range(0, 8), Orientation.All.Select(o => o.Code));
    }

    [Fact]
    public void FromCode_RejectsOutOfRange()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Orientation.FromCode(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => Orientation.FromCode(8));
    }

    [Fact]
    public void EightOrientations_YieldEightDistinctReadingDirections()
    {
        // The 8 flips must map to the 8 distinct ordered perpendicular direction
        // pairs — no two chiralities read the same way.
        var readings = Orientation.All.Select(o => o.Reading()).ToList();
        Assert.Equal(8, readings.Distinct().Count());
        // Primary and secondary are always perpendicular (never the same axis).
        foreach (var (primary, secondary) in readings)
        {
            var (px, py) = primary.ToVector();
            var (sx, sy) = secondary.ToVector();
            Assert.Equal(0, px * sx + py * sy); // dot product zero
        }
    }

    [Fact]
    public void Identity_ReadsRightDown()
    {
        Assert.Equal("Right-Down", Orientation.Identity.ReadingLabel());
        Assert.False(Orientation.Identity.IsReflection);
    }

    [Theory]
    [InlineData(false, false, false, 1)]  // identity: rotation
    [InlineData(true, false, false, -1)]  // transpose: reflection
    [InlineData(false, true, false, -1)]  // x-invert: reflection
    [InlineData(false, false, true, -1)]  // y-invert: reflection
    [InlineData(false, true, true, 1)]    // 180°: rotation
    [InlineData(true, true, true, -1)]    // transpose+180: reflection
    public void Determinant_MatchesParity(bool t, bool x, bool y, int expected)
    {
        Assert.Equal(expected, new Orientation(t, x, y).Determinant);
    }

    [Fact]
    public void Transposed_IsInvolution()
    {
        foreach (var o in Orientation.All)
            Assert.Equal(o, o.Transposed().Transposed());
    }

    [Fact]
    public void Transposed_ReversesTheReadingPair()
    {
        foreach (var o in Orientation.All)
        {
            var (primary, secondary) = o.Reading();
            var (tPrimary, tSecondary) = o.Transposed().Reading();
            Assert.Equal(secondary, tPrimary);
            Assert.Equal(primary, tSecondary);
        }
    }

    [Fact]
    public void Apply_ComposesAsGroup_TransposedEqualsPostMultiplyBySwap()
    {
        // Transposed() must equal swapping the columns of the linear map, i.e.
        // applying the axis-swap after the orientation: (M·T)(v) = M(swap(v)).
        foreach (var o in Orientation.All)
        {
            var transposed = o.Transposed();
            foreach (var (vx, vy) in new[] { (1, 0), (0, 1), (1, 1), (2, -3) })
            {
                var viaTransposed = transposed.Apply(vx, vy);
                var viaSwap = o.Apply(vy, vx);
                Assert.Equal(viaSwap, viaTransposed);
            }
        }
    }
}
