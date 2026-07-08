namespace Pockets.Core.Cosmology.Glyphs;

/// <summary>Which glyph family a <see cref="GlyphSpec"/> belongs to.</summary>
public enum GlyphKind
{
    /// <summary>One of the 8 basis "sort by ascending" staircase flips.</summary>
    Basis,
    /// <summary>One of the 4 parent "wifi rainbow" quadrant flips.</summary>
    Parent
}

/// <summary>
/// A single fully-resolved glyph: a stable file-safe name, a human title for the
/// contact sheet, the orientation that produced it, and its stroke geometry.
/// </summary>
public sealed record GlyphSpec(
    string Name,
    string Title,
    GlyphKind Kind,
    Orientation Orientation,
    ImmutableArray<Primitive> Primitives)
{
    /// <summary>Emit this glyph as a standalone SVG document.</summary>
    public string ToSvg(GlyphParams p, SvgEmitter.Options? options = null) =>
        SvgEmitter.Emit(Primitives, p, options);
}

/// <summary>
/// Assembles the full 8+4 glyph set from the <see cref="EntropyMatrix"/> data.
/// This is the ordered, deterministic list every consumer (SVG files, contact
/// sheet, Godot import) draws from.
/// </summary>
public static class GlyphCatalog
{
    /// <summary>All 12 glyphs (8 basis, then 4 parent), in canonical order.</summary>
    public static ImmutableArray<GlyphSpec> All(GlyphParams p)
    {
        var basis = EntropyMatrix.Zones.Select(z => new GlyphSpec(
            Name: $"basis-{Slug(z.Zone)}",
            Title: $"{QuadrantSign(z)} · {z.Flavor} · {z.ChiralityLabel}",
            Kind: GlyphKind.Basis,
            Orientation: z.Orientation,
            Primitives: GlyphGeometry.Basis(z.Zone, p)));

        var parents = EntropyMatrix.Quadrants.Select(q => new GlyphSpec(
            Name: $"parent-{q.ToString().ToLowerInvariant()}",
            Title: $"{q} (parent)",
            Kind: GlyphKind.Parent,
            Orientation: EntropyMatrix.ParentOrientation(q),
            Primitives: GlyphGeometry.Parent(q, p)));

        return basis.Concat(parents).ToImmutableArray();
    }

    /// <summary>A file-safe slug for a zone, e.g. "quiet-positive".</summary>
    private static string Slug(Zone zone)
    {
        var info = EntropyMatrix.Info(zone);
        string aspect = info.Aspect == Aspect.Positive ? "positive" : "negative";
        return $"{info.Quadrant.ToString().ToLowerInvariant()}-{aspect}";
    }

    /// <summary>A short label like "Quiet+" / "Quiet−".</summary>
    private static string QuadrantSign(ZoneInfo z) =>
        $"{z.Quadrant}{(z.Aspect == Aspect.Positive ? "+" : "−")}";
}
