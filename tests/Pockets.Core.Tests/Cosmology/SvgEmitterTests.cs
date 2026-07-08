using System.Globalization;
using System.Xml.Linq;
using Pockets.Core.Cosmology;
using Pockets.Core.Cosmology.Glyphs;

namespace Pockets.Core.Tests.Cosmology;

/// <summary>
/// SVG emission is valid, Godot-friendly, and byte-deterministic.
/// </summary>
public class SvgEmitterTests
{
    private static readonly GlyphParams P = GlyphParams.Default;

    [Fact]
    public void EveryGlyph_EmitsWellFormedSvg()
    {
        foreach (var g in GlyphCatalog.All(P))
        {
            var doc = XDocument.Parse(g.ToSvg(P)); // throws on malformed XML
            var root = doc.Root!;
            Assert.Equal("svg", root.Name.LocalName);
            Assert.Equal($"0 0 {P.ViewBox} {P.ViewBox}", root.Attribute("viewBox")!.Value);
        }
    }

    [Fact]
    public void Svg_IsStrokeBased_FillNone_ForGodot()
    {
        var svg = GlyphCatalog.All(P).First().ToSvg(P);
        Assert.Contains("fill=\"none\"", svg);
        Assert.Contains("stroke-width=", svg);
        Assert.DoesNotContain("<rect", svg); // no background fill to fight Godot modulation
    }

    [Fact]
    public void PathCount_MatchesPrimitiveCount()
    {
        foreach (var g in GlyphCatalog.All(P))
        {
            var doc = XDocument.Parse(g.ToSvg(P));
            var paths = doc.Descendants().Where(e => e.Name.LocalName == "path").ToList();
            Assert.Equal(g.Primitives.Length, paths.Count);
        }
    }

    [Fact]
    public void ParentGlyphs_EmitArcCommands()
    {
        var parent = GlyphCatalog.All(P).First(g => g.Kind == GlyphKind.Parent);
        Assert.Contains(" A ", parent.ToSvg(P));
    }

    [Fact]
    public void Emission_IsByteIdentical_AcrossRuns()
    {
        var a = GlyphCatalog.All(P).Select(g => g.ToSvg(P)).ToList();
        var b = GlyphCatalog.All(P).Select(g => g.ToSvg(P)).ToList();
        Assert.Equal(a, b);
    }

    [Fact]
    public void Emission_IsCultureInvariant()
    {
        // A comma-decimal culture must not leak into the emitted numbers.
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            var svg = GlyphCatalog.All(P).First().ToSvg(P);
            Assert.DoesNotContain(",", svg); // no "3,5" decimals or list separators
            Assert.Contains(".", svg);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void Emission_UsesUnixLineEndings()
    {
        var svg = GlyphCatalog.All(P).First().ToSvg(P);
        Assert.DoesNotContain("\r\n", svg);
        Assert.EndsWith("</svg>\n", svg);
    }

    [Fact]
    public void StrokeColorOption_IsHonored()
    {
        var svg = GlyphCatalog.All(P).First().ToSvg(P, new SvgEmitter.Options { StrokeColor = "#abcdef" });
        Assert.Contains("stroke=\"#abcdef\"", svg);
    }
}
