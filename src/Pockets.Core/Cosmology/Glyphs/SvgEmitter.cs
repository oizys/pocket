using System.Globalization;
using System.Text;

namespace Pockets.Core.Cosmology.Glyphs;

/// <summary>
/// Deterministic SVG emission for a set of glyph primitives. Output is
/// byte-stable for a given (primitives, params, options) input: fixed decimal
/// formatting under the invariant culture, explicit "\n" line endings, and no
/// timestamps. Sized as a clean square viewBox with stroke-based, fill-none
/// paths so it imports cleanly into Godot (nanosvg/ThorVG).
/// </summary>
public static class SvgEmitter
{
    /// <summary>Rendering options that do not affect geometry.</summary>
    public sealed record Options
    {
        /// <summary>Stroke color for every path (a hex string such as "#101014").</summary>
        public string StrokeColor { get; init; } = "#000000";
    }

    /// <summary>Emit a full standalone SVG document for the given primitives.</summary>
    public static string Emit(
        IEnumerable<Primitive> primitives,
        GlyphParams p,
        Options? options = null)
    {
        options ??= new Options();
        string size = F(p.ViewBox);
        var sb = new StringBuilder();
        sb.Append("<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 ")
          .Append(size).Append(' ').Append(size)
          .Append("\" width=\"").Append(size).Append("\" height=\"").Append(size)
          .Append("\">\n");
        sb.Append("  <g fill=\"none\" stroke=\"").Append(options.StrokeColor)
          .Append("\" stroke-width=\"").Append(F(p.StrokeWidth))
          .Append("\" stroke-linecap=\"").Append(p.StrokeLineCap)
          .Append("\" stroke-linejoin=\"round\">\n");

        foreach (var prim in primitives)
            sb.Append("    <path d=\"").Append(PathData(prim)).Append("\"/>\n");

        sb.Append("  </g>\n");
        sb.Append("</svg>\n");
        return sb.ToString();
    }

    /// <summary>The SVG path "d" attribute for a single primitive.</summary>
    public static string PathData(Primitive prim) => prim switch
    {
        Segment s => $"M {F(s.A.X)} {F(s.A.Y)} L {F(s.B.X)} {F(s.B.Y)}",
        Arc a => $"M {F(a.Start.X)} {F(a.Start.Y)} A {F(a.Radius)} {F(a.Radius)} 0 "
                 + $"{a.LargeArcFlag} {a.SweepFlag} {F(a.End.X)} {F(a.End.Y)}",
        _ => throw new ArgumentOutOfRangeException(nameof(prim), prim.GetType().Name, "Unknown primitive.")
    };

    // Fixed 3-decimal formatting, invariant culture, with negative-zero normalized
    // so output is byte-identical across platforms and runs.
    private static string F(double value)
    {
        double rounded = Math.Round(value, 3, MidpointRounding.AwayFromZero);
        if (rounded == 0) rounded = 0; // collapse -0 to 0
        return rounded.ToString("0.###", CultureInfo.InvariantCulture);
    }
}
