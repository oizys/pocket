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

        var rows = BasisRows(center, p);              // the 3 canonical row heights
        var lines = ImmutableArray.Create<Primitive>(
            Row(xLeft, rows[0], longLen),
            Row(xLeft, rows[1], longLen * p.BasisMidRatio),
            Row(xLeft, rows[2], longLen * p.BasisShortRatio));

        return lines.Select(s => s.Transform(orientation, center)).ToImmutableArray();

        static Segment Row(double x, double y, double len) =>
            new(new Pt(x, y), new Pt(x + len, y));
    }

    /// <summary>
    /// The 3 canonical staircase row positions, centered on the viewBox. Under the
    /// identity these are the heights of the "+" child's horizontal lines; under the
    /// axis-transpose (its "−" sibling) the same values are the x-positions of the
    /// vertical lines. The parent arcs derive their radii from exactly these values
    /// so an arc end lands on each child line — see <see cref="Parent(Orientation, GlyphParams)"/>.
    /// </summary>
    public static double[] BasisRows(double center, GlyphParams p) =>
        new[] { center - p.BasisRowGap, center, center + p.BasisRowGap };

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

    /// <summary>
    /// The wifi-rainbow under an explicit orientation. Per Aaron's contact-sheet
    /// note, the parent is the literal bridge between its two children's strokes: it
    /// carries one concentric quarter-arc per staircase row, and each arc's two ends
    /// land on the row-matched lines of the quadrant's children.
    ///
    /// <para>
    /// Canonical (Quiet, identity): the anchor <c>A</c> is the bottom-right quadrant
    /// corner. For each staircase row value <c>v</c> (a <see cref="BasisRows"/> entry)
    /// the arc runs from <c>(v, A)</c> — on the bottom wall, so its x aligns with the
    /// "−" child's vertical line at <c>x = v</c> — round to <c>(A, v)</c> — on the
    /// right wall, so its y aligns with the "+" child's horizontal line at <c>y = v</c>.
    /// The radius <c>r = A − v</c> is therefore <b>derived from the child line
    /// geometry</b>, not a free knob, which is what makes the ends coincide. The four
    /// parents stay exact flips of this one construction via <paramref name="orientation"/>.
    /// </para>
    /// </summary>
    public static ImmutableArray<Primitive> Parent(Orientation orientation, GlyphParams p)
    {
        double center = p.ViewBox / 2;
        double anchor = center + p.ParentAnchorOffset;   // canonical anchor: bottom-right corner
        const double invSqrt2 = 0.70710678118654752;

        var arcs = ImmutableArray.CreateBuilder<Primitive>();
        foreach (double v in BasisRows(center, p))       // one arc per staircase row
        {
            double r = anchor - v;                       // radius derived from the child line position
            // Quarter arc from the bottom wall (x = v) round to the right wall (y = v),
            // opening toward the core. Its start.X and end.Y are the child line anchors.
            var start = new Pt(v, anchor);
            var mid = new Pt(anchor - r * invSqrt2, anchor - r * invSqrt2);
            var end = new Pt(anchor, v);
            arcs.Add(new Arc(start, mid, end, r));
        }

        return arcs.Select(a => a.Transform(orientation, center)).ToImmutableArray();
    }
}
