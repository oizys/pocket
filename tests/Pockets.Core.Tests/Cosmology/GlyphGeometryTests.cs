using Pockets.Core.Cosmology;
using Pockets.Core.Cosmology.Glyphs;

namespace Pockets.Core.Tests.Cosmology;

/// <summary>
/// Glyph stroke geometry: the basis staircase has the right shape under the
/// identity, the parent wifi-rainbow sits in its quadrant's corner, and the
/// parameter knobs actually drive the geometry.
/// </summary>
public class GlyphGeometryTests
{
    private static readonly GlyphParams P = GlyphParams.Default;

    private static IEnumerable<Pt> Points(IEnumerable<Primitive> prims) =>
        prims.SelectMany(prim => prim switch
        {
            Segment s => new[] { s.A, s.B },
            Arc a => new[] { a.Start, a.Mid, a.End },
            _ => Array.Empty<Pt>()
        });

    [Fact]
    public void Basis_HasThreeLeftAlignedDecreasingLines_UnderIdentity()
    {
        var segs = GlyphGeometry.Basis(Orientation.Identity, P).Cast<Segment>().ToList();
        Assert.Equal(3, segs.Count);

        // All three share the same left (min) x — the aligned edge.
        var lefts = segs.Select(s => Math.Min(s.A.X, s.B.X)).Distinct().ToList();
        Assert.Single(lefts);

        // Lengths strictly decrease top → bottom.
        var byRow = segs.OrderBy(s => s.A.Y).ToList();
        double LenOf(Segment s) => Math.Abs(s.B.X - s.A.X);
        Assert.True(LenOf(byRow[0]) > LenOf(byRow[1]));
        Assert.True(LenOf(byRow[1]) > LenOf(byRow[2]));
        Assert.Equal(P.BasisLongLength, LenOf(byRow[0]), 3);
    }

    [Fact]
    public void Basis_AllEightZones_ProduceThreeSegments()
    {
        foreach (var z in EntropyMatrix.Zones)
        {
            var prims = GlyphGeometry.Basis(z.Zone, P);
            Assert.Equal(3, prims.Length);
            Assert.All(prims, prim => Assert.IsType<Segment>(prim));
        }
    }

    [Fact]
    public void Basis_StaysWithinViewBox()
    {
        foreach (var z in EntropyMatrix.Zones)
            foreach (var pt in Points(GlyphGeometry.Basis(z.Zone, P)))
            {
                Assert.InRange(pt.X, 0, P.ViewBox);
                Assert.InRange(pt.Y, 0, P.ViewBox);
            }
    }

    [Fact]
    public void Parent_ArcCount_TracksKnob()
    {
        Assert.Equal(3, GlyphGeometry.Parent(Quadrant.Quiet, P with { ArcCount = 3 }).Length);
        Assert.Equal(5, GlyphGeometry.Parent(Quadrant.Quiet, P with { ArcCount = 5 }).Length);
        Assert.All(GlyphGeometry.Parent(Quadrant.Quiet, P), prim => Assert.IsType<Arc>(prim));
    }

    [Theory]
    // Each parent rainbow's centroid must sit in its quadrant's screen corner.
    [InlineData(Quadrant.Quiet, +1, +1)]   // bottom-right
    [InlineData(Quadrant.Gloam, -1, +1)]   // bottom-left
    [InlineData(Quadrant.Flux, -1, -1)]    // top-left
    [InlineData(Quadrant.Jitter, +1, -1)]  // top-right
    public void Parent_RainbowSitsInItsCorner(Quadrant quadrant, int signX, int signY)
    {
        double center = P.ViewBox / 2;
        var pts = Points(GlyphGeometry.Parent(quadrant, P)).ToList();
        double cx = pts.Average(p => p.X);
        double cy = pts.Average(p => p.Y);
        Assert.True(Math.Sign(cx - center) == signX, $"{quadrant} x on wrong side of center");
        Assert.True(Math.Sign(cy - center) == signY, $"{quadrant} y on wrong side of center");
    }

    [Fact]
    public void Parent_ArcsAreConcentric_RadiiFollowKnobs()
    {
        var arcs = GlyphGeometry.Parent(Quadrant.Quiet, P).Cast<Arc>().ToList();
        for (int i = 0; i < arcs.Count; i++)
            Assert.Equal(P.ArcInnerRadius + i * P.ArcRadiusStep, arcs[i].Radius, 6);
    }

    [Fact]
    public void Flips_ProduceDistinctGeometry_AcrossAllTwelve()
    {
        // No two of the 12 glyphs may share identical path geometry.
        var svgs = GlyphCatalog.All(P)
            .Select(g => string.Join("|", g.Primitives.Select(SvgEmitter.PathData)))
            .ToList();
        Assert.Equal(12, svgs.Distinct().Count());
    }
}
