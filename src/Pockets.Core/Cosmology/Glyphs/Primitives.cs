namespace Pockets.Core.Cosmology.Glyphs;

/// <summary>A point in canonical glyph space (x-right / y-down).</summary>
public readonly record struct Pt(double X, double Y)
{
    /// <summary>
    /// Apply an orientation about a center. The orientation acts as a linear map
    /// on the center-relative offset, then the center is added back.
    /// </summary>
    public Pt Transform(Orientation o, double center)
    {
        var (x, y) = o.Apply(X - center, Y - center);
        return new Pt(x + center, y + center);
    }
}

/// <summary>A glyph stroke: either a straight line segment or a circular arc.</summary>
public abstract record Primitive
{
    /// <summary>Apply an orientation about <paramref name="center"/> to this primitive.</summary>
    public abstract Primitive Transform(Orientation o, double center);
}

/// <summary>A straight line segment between two points.</summary>
public sealed record Segment(Pt A, Pt B) : Primitive
{
    public override Primitive Transform(Orientation o, double center) =>
        new Segment(A.Transform(o, center), B.Transform(o, center));
}

/// <summary>
/// A circular arc, stored by its two endpoints plus a mid-arc point (so the sweep
/// direction survives reflections) and its radius (invariant under orthogonal maps).
/// </summary>
public sealed record Arc(Pt Start, Pt Mid, Pt End, double Radius) : Primitive
{
    public override Primitive Transform(Orientation o, double center) =>
        new Arc(
            Start.Transform(o, center),
            Mid.Transform(o, center),
            End.Transform(o, center),
            Radius);

    /// <summary>
    /// The SVG sweep-flag (1 = clockwise in y-down space) inferred from the winding
    /// of Start → Mid → End. Recomputed from the (possibly reflected) points so it
    /// is always correct after transformation.
    /// </summary>
    public int SweepFlag
    {
        get
        {
            double cross = (Mid.X - Start.X) * (End.Y - Start.Y)
                         - (Mid.Y - Start.Y) * (End.X - Start.X);
            // In y-down screen space a positive cross product is a clockwise turn.
            return cross > 0 ? 1 : 0;
        }
    }

    /// <summary>The SVG large-arc-flag; these glyph arcs are always the minor (≤180°) arc.</summary>
    public int LargeArcFlag => 0;
}
