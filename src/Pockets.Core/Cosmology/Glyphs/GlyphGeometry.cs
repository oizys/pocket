namespace Pockets.Core.Cosmology.Glyphs;

/// <summary>
/// Builds the canonical stroke geometry for the two glyph families and applies a
/// zone/quadrant orientation. Canonical (identity) shapes are authored for the
/// Quiet quadrant; every other glyph is a flip of these via <see cref="Orientation"/>.
/// </summary>
public static class GlyphGeometry
{
    /// <summary>
    /// The 8 basis "sort by ascending" staircase, drawn for a zone. Canonical form
    /// (Quiet+, identity): three left-aligned horizontal lines, longest on top,
    /// shortest on the bottom — reading Right-Down. Flipped by the zone orientation.
    /// </summary>
    public static ImmutableArray<Primitive> Basis(Zone zone, GlyphParams p)
    {
        var orientation = EntropyMatrix.Info(zone).Orientation;
        return Basis(orientation, p);
    }

    /// <summary>The basis staircase under an explicit orientation.</summary>
    public static ImmutableArray<Primitive> Basis(Orientation orientation, GlyphParams p)
    {
        double center = p.ViewBox / 2;
        double longLen = p.BasisLongLength;
        double xLeft = center - longLen / 2;          // horizontally centered long line
        double yTop = center - p.BasisRowGap;         // three rows centered vertically
        double yMid = center;
        double yBot = center + p.BasisRowGap;

        var lines = ImmutableArray.Create<Primitive>(
            Row(xLeft, yTop, longLen),
            Row(xLeft, yMid, longLen * p.BasisMidRatio),
            Row(xLeft, yBot, longLen * p.BasisShortRatio));

        return lines.Select(s => s.Transform(orientation, center)).ToImmutableArray();

        static Segment Row(double x, double y, double len) =>
            new(new Pt(x, y), new Pt(x + len, y));
    }

    /// <summary>
    /// The 4 parent "wifi rainbow" glyphs for a quadrant. Canonical form (Quiet):
    /// concentric quarter arcs anchored at the quadrant corner, radiating inward
    /// toward the Core. Flipped by the quadrant's parent orientation.
    /// </summary>
    public static ImmutableArray<Primitive> Parent(Quadrant quadrant, GlyphParams p)
    {
        var orientation = EntropyMatrix.ParentOrientation(quadrant);
        return Parent(orientation, p);
    }

    /// <summary>The wifi-rainbow under an explicit orientation.</summary>
    public static ImmutableArray<Primitive> Parent(Orientation orientation, GlyphParams p)
    {
        double center = p.ViewBox / 2;
        double ax = center + p.ParentAnchorOffset;    // canonical anchor: bottom-right corner
        double ay = center + p.ParentAnchorOffset;
        const double invSqrt2 = 0.70710678118654752;

        var arcs = ImmutableArray.CreateBuilder<Primitive>();
        for (int i = 0; i < p.ArcCount; i++)
        {
            double r = p.ArcInnerRadius + i * p.ArcRadiusStep;
            // Quarter arc from left-of-anchor to above-anchor, opening toward the core.
            var start = new Pt(ax - r, ay);
            var mid = new Pt(ax - r * invSqrt2, ay - r * invSqrt2);
            var end = new Pt(ax, ay - r);
            arcs.Add(new Arc(start, mid, end, r));
        }

        return arcs.Select(a => a.Transform(orientation, center)).ToImmutableArray();
    }
}
