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

    // Compares doubles within an absolute tolerance, for set/sequence assertions.
    private sealed class DoubleComparer : IEqualityComparer<double>
    {
        private readonly double _eps;
        public DoubleComparer(double eps) => _eps = eps;
        public bool Equals(double a, double b) => Math.Abs(a - b) <= _eps;
        public int GetHashCode(double v) => 0; // force Equals-based comparison
    }

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
    public void Parent_HasOneArcPerStaircaseRow()
    {
        // The parent bridges its children row-for-row, so it carries exactly one arc
        // per basis staircase line (3), all arcs.
        var basisRows = GlyphGeometry.Basis(Orientation.Identity, P).Length;
        var arcs = GlyphGeometry.Parent(Quadrant.Quiet, P);
        Assert.Equal(basisRows, arcs.Length);
        Assert.All(arcs, prim => Assert.IsType<Arc>(prim));
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
    public void Parent_ArcRadii_DerivedFromChildRows()
    {
        // Each arc's radius is anchor − rowValue, so the radii are the corner-anchor
        // distance minus each staircase row position — nothing is a free knob.
        double center = P.ViewBox / 2;
        double anchor = center + P.ParentAnchorOffset;
        var expected = GlyphGeometry.BasisRows(center, P).Select(v => anchor - v).OrderBy(r => r);
        var actual = GlyphGeometry.Parent(Quadrant.Quiet, P).Cast<Arc>().Select(a => a.Radius).OrderBy(r => r);
        Assert.Equal(expected, actual, new DoubleComparer(1e-6));
    }

    // ---- Endpoint alignment: the parent's arc ends coincide with its children's
    // line geometry (Aaron's contact-sheet adjustment), for all four quadrants. ----

    // Perpendicular distance from a point to the infinite line carrying a segment.
    private static double DistanceToLine(Pt pt, Segment seg)
    {
        double dx = seg.B.X - seg.A.X, dy = seg.B.Y - seg.A.Y;
        double len = Math.Sqrt(dx * dx + dy * dy);
        // Signed area of the parallelogram / base length = perpendicular height.
        double cross = Math.Abs((pt.X - seg.A.X) * dy - (pt.Y - seg.A.Y) * dx);
        return cross / len;
    }

    // True when the arc ends can be matched one-to-one with the child lines such that
    // each chosen end lies on its matched line's supporting line (within epsilon).
    private static bool EndsAlignToChildLines(
        IEnumerable<Pt> arcEnds, IReadOnlyList<Segment> childLines, double eps)
    {
        var ends = arcEnds.ToList();
        if (ends.Count != childLines.Count) return false;
        var used = new bool[childLines.Count];
        foreach (var end in ends)
        {
            int match = Enumerable.Range(0, childLines.Count)
                .FirstOrDefault(j => !used[j] && DistanceToLine(end, childLines[j]) <= eps, -1);
            if (match < 0) return false;
            used[match] = true;
        }
        return true;
    }

    [Theory]
    [InlineData(Quadrant.Quiet)]
    [InlineData(Quadrant.Gloam)]
    [InlineData(Quadrant.Flux)]
    [InlineData(Quadrant.Jitter)]
    public void Parent_ArcEnds_AlignWithChildLineGeometry(Quadrant quadrant)
    {
        const double eps = 1e-6;
        var arcs = GlyphGeometry.Parent(quadrant, P).Cast<Arc>().ToList();

        // The quadrant's two children: the "+" staircase and its axis-transpose "−".
        var plusChild = GlyphGeometry.Basis(EntropyMatrix.Positive(quadrant).Zone, P)
            .Cast<Segment>().ToList();
        var minusChild = GlyphGeometry.Basis(EntropyMatrix.Negative(quadrant).Zone, P)
            .Cast<Segment>().ToList();

        // One arc end coincides with a "+" child line, the other with a "−" child line.
        Assert.True(
            EndsAlignToChildLines(arcs.Select(a => a.End), plusChild, eps),
            $"{quadrant}: arc ends do not align with the + child's lines");
        Assert.True(
            EndsAlignToChildLines(arcs.Select(a => a.Start), minusChild, eps),
            $"{quadrant}: arc starts do not align with the − child's lines");
    }

    [Fact]
    public void Parent_AlignmentIsExact_ForCanonicalQuiet()
    {
        // Concrete pin for the canonical quadrant: arc END y-values are the "+" child
        // line heights and arc START x-values are the "−" child line x-positions.
        var arcs = GlyphGeometry.Parent(Quadrant.Quiet, P).Cast<Arc>().ToList();
        var plusHeights = GlyphGeometry.Basis(EntropyMatrix.Positive(Quadrant.Quiet).Zone, P)
            .Cast<Segment>().Select(s => s.A.Y).OrderBy(v => v).ToList();
        var minusXs = GlyphGeometry.Basis(EntropyMatrix.Negative(Quadrant.Quiet).Zone, P)
            .Cast<Segment>().Select(s => s.A.X).OrderBy(v => v).ToList();

        Assert.Equal(plusHeights, arcs.Select(a => a.End.Y).OrderBy(v => v), new DoubleComparer(1e-6));
        Assert.Equal(minusXs, arcs.Select(a => a.Start.X).OrderBy(v => v), new DoubleComparer(1e-6));
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
