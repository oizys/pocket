namespace Pockets.Core.Cosmology.Glyphs;

/// <summary>
/// The parameter knobs for glyph geometry. Everywhere Aaron's sketches are
/// ambiguous, the shape is driven by a knob here rather than a guess, so the
/// contact sheet can be re-tuned without touching the generator. All lengths are
/// in canonical viewBox units (see <see cref="ViewBox"/>).
/// </summary>
public sealed record GlyphParams
{
    /// <summary>Canonical square viewBox side; the drawing space is 0..ViewBox on both axes.</summary>
    public double ViewBox { get; init; } = 100;

    /// <summary>Stroke width for every glyph line and arc.</summary>
    public double StrokeWidth { get; init; } = 7;

    /// <summary>SVG stroke-linecap ("round", "butt", or "square").</summary>
    public string StrokeLineCap { get; init; } = "round";

    // ---- Basis "sort by ascending" staircase (8 flips) ----

    /// <summary>Length of the longest (top) line, before flipping.</summary>
    public double BasisLongLength { get; init; } = 60;

    /// <summary>Middle line length as a fraction of the long line.</summary>
    public double BasisMidRatio { get; init; } = 0.62;

    /// <summary>Short (bottom) line length as a fraction of the long line.</summary>
    public double BasisShortRatio { get; init; } = 0.30;

    /// <summary>Vertical gap between adjacent staircase lines, before flipping.</summary>
    public double BasisRowGap { get; init; } = 20;

    // ---- Parent "wifi rainbow" (4 flips) ----

    /// <summary>Number of concentric arcs in the wifi-rainbow, before flipping.</summary>
    public int ArcCount { get; init; } = 3;

    /// <summary>Radius of the innermost arc, measured from the quadrant corner anchor.</summary>
    public double ArcInnerRadius { get; init; } = 15;

    /// <summary>Radius increment between adjacent arcs.</summary>
    public double ArcRadiusStep { get; init; } = 13;

    /// <summary>
    /// Distance from the viewBox center to the quadrant corner anchor the arcs
    /// radiate from, along each axis. Larger values push the rainbow further into
    /// its corner.
    /// </summary>
    public double ParentAnchorOffset { get; init; } = 34;

    /// <summary>The default knob set.</summary>
    public static GlyphParams Default { get; } = new();
}
