using Pockets.Core.Cosmology;

namespace Pockets.Core.Tests.Cosmology;

/// <summary>
/// The zone↔flip data table is internally consistent and matches the cosmology
/// capture: every chirality label is realized by its orientation, negatives are
/// the transpose of their positive sibling, and the four quadrants cover the four
/// screen corners with the "+" aspect pointing into its corner.
/// </summary>
public class EntropyMatrixTests
{
    [Fact]
    public void Contains8Zones_And4Quadrants()
    {
        Assert.Equal(8, EntropyMatrix.Zones.Length);
        Assert.Equal(8, EntropyMatrix.Zones.Select(z => z.Zone).Distinct().Count());
        Assert.Equal(4, EntropyMatrix.Quadrants.Length);
    }

    [Fact]
    public void EveryZone_HasDistinctOrientation()
    {
        var orientations = EntropyMatrix.Zones.Select(z => z.Orientation).ToList();
        Assert.Equal(8, orientations.Distinct().Count());
    }

    [Fact]
    public void OrientationReproducesChiralityLabel_ForEveryZone()
    {
        // The load-bearing reconciliation: the (Transpose,XInvert,YInvert) triple
        // authored for each row must actually read as the cosmology's chirality.
        foreach (var z in EntropyMatrix.Zones)
            Assert.Equal(z.ChiralityLabel, z.Orientation.ReadingLabel());
    }

    [Theory]
    [InlineData(Zone.QuietPositive, "Right-Down")]
    [InlineData(Zone.QuietNegative, "Down-Right")]
    [InlineData(Zone.GloamPositive, "Left-Down")]
    [InlineData(Zone.GloamNegative, "Down-Left")]
    [InlineData(Zone.FluxPositive, "Left-Up")]
    [InlineData(Zone.FluxNegative, "Up-Left")]
    [InlineData(Zone.JitterPositive, "Right-Up")]
    [InlineData(Zone.JitterNegative, "Up-Right")]
    public void ChiralityLabels_MatchCosmologyTable(Zone zone, string expected)
    {
        Assert.Equal(expected, EntropyMatrix.Info(zone).ChiralityLabel);
    }

    [Fact]
    public void NegativeAspect_TransposesPositiveChirality()
    {
        foreach (var quadrant in EntropyMatrix.Quadrants)
        {
            var positive = EntropyMatrix.Positive(quadrant).Orientation;
            var negative = EntropyMatrix.Negative(quadrant).Orientation;
            Assert.Equal(positive.Transposed(), negative);
        }
    }

    [Fact]
    public void ParentOrientation_EqualsPositiveAspectOrientation()
    {
        foreach (var quadrant in EntropyMatrix.Quadrants)
            Assert.Equal(
                EntropyMatrix.Positive(quadrant).Orientation,
                EntropyMatrix.ParentOrientation(quadrant));
    }

    [Fact]
    public void FourParents_CoverFourDistinctCorners()
    {
        // Each parent's orientation, applied to the outward diagonal (+1,+1),
        // must land in a distinct quadrant-corner sign pair.
        var corners = EntropyMatrix.Quadrants
            .Select(q => EntropyMatrix.ParentOrientation(q).Apply(1, 1))
            .ToList();
        Assert.Equal(4, corners.Distinct().Count());
    }

    [Theory]
    // The "+" aspect's primary reading direction points toward its quadrant corner.
    [InlineData(Quadrant.Quiet, Direction.Right, Direction.Down)]   // bottom-right
    [InlineData(Quadrant.Gloam, Direction.Left, Direction.Down)]    // bottom-left
    [InlineData(Quadrant.Flux, Direction.Left, Direction.Up)]       // top-left
    [InlineData(Quadrant.Jitter, Direction.Right, Direction.Up)]    // top-right
    public void PositiveAspect_PointsIntoItsCorner(Quadrant quadrant, Direction primary, Direction secondary)
    {
        var reading = EntropyMatrix.Positive(quadrant).Orientation.Reading();
        Assert.Equal((primary, secondary), reading);
    }
}
