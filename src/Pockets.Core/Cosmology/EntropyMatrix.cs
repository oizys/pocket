namespace Pockets.Core.Cosmology;

/// <summary>
/// The canonical record for one entropy sub-zone: its quadrant, aspect, flavor
/// name, human-readable chirality label from the cosmology capture, and the
/// geometric <see cref="Orientation"/> that realizes that chirality.
/// </summary>
public sealed record ZoneInfo(
    Zone Zone,
    Quadrant Quadrant,
    Aspect Aspect,
    string Flavor,
    string ChiralityLabel,
    Orientation Orientation);

/// <summary>
/// The 8+4 entropy symbol matrix as DATA — the single source of truth that
/// asset generation, narrative, and mechanics all consume. Maps each zone to its
/// chirality/orientation and each quadrant to its parent-glyph orientation.
///
/// <para>
/// Derivation (reconciled with the cosmology capture and entropy design §2.4):
/// the 8 basis glyphs are the 8 flips of the "sort by ascending" staircase; a
/// zone's chirality is the reading direction of that flip, encoded as the 3-bit
/// <see cref="Orientation"/>. Each "+" aspect points its primary stroke toward its
/// quadrant corner; each "−" aspect is the axis-transpose of its "+" sibling.
/// The 4 parent (wifi-rainbow) glyphs share their quadrant's "+" orientation.
/// </para>
/// </summary>
public static class EntropyMatrix
{
    private static readonly ImmutableArray<ZoneInfo> _zones = BuildZones();

    /// <summary>All 8 sub-zones, ordered by <see cref="Zone"/>.</summary>
    public static ImmutableArray<ZoneInfo> Zones => _zones;

    /// <summary>The 4 quadrants, clockwise from bottom-right.</summary>
    public static ImmutableArray<Quadrant> Quadrants { get; } =
        ImmutableArray.Create(Quadrant.Quiet, Quadrant.Gloam, Quadrant.Flux, Quadrant.Jitter);

    /// <summary>Look up a zone's canonical record.</summary>
    public static ZoneInfo Info(Zone zone) => _zones.First(z => z.Zone == zone);

    /// <summary>The positive sub-zone of a quadrant.</summary>
    public static ZoneInfo Positive(Quadrant quadrant) =>
        _zones.First(z => z.Quadrant == quadrant && z.Aspect == Aspect.Positive);

    /// <summary>The negative sub-zone of a quadrant.</summary>
    public static ZoneInfo Negative(Quadrant quadrant) =>
        _zones.First(z => z.Quadrant == quadrant && z.Aspect == Aspect.Negative);

    /// <summary>
    /// The parent (wifi-rainbow) glyph's orientation for a quadrant. It shares the
    /// quadrant's positive-aspect orientation, hugging the quadrant's screen corner.
    /// </summary>
    public static Orientation ParentOrientation(Quadrant quadrant) =>
        Positive(quadrant).Orientation;

    // The zone table. Orientations are written as the (Transpose, XInvert, YInvert)
    // triples that reproduce each cosmology chirality label; the tests assert that
    // Orientation.ReadingLabel() equals the ChiralityLabel here for every row, and
    // that each negative is the transpose of its positive sibling.
    private static ImmutableArray<ZoneInfo> BuildZones() => ImmutableArray.Create(
        Zone(Cosmology.Zone.QuietPositive, Quadrant.Quiet, Aspect.Positive,
            "Dust/Death", "Right-Down", t: false, x: false, y: false),
        Zone(Cosmology.Zone.QuietNegative, Quadrant.Quiet, Aspect.Negative,
            "Blight/Bloom", "Down-Right", t: true, x: false, y: false),
        Zone(Cosmology.Zone.GloamPositive, Quadrant.Gloam, Aspect.Positive,
            "Shadow", "Left-Down", t: false, x: true, y: false),
        Zone(Cosmology.Zone.GloamNegative, Quadrant.Gloam, Aspect.Negative,
            "Glow", "Down-Left", t: true, x: false, y: true),
        Zone(Cosmology.Zone.FluxPositive, Quadrant.Flux, Aspect.Positive,
            "Ash", "Left-Up", t: false, x: true, y: true),
        Zone(Cosmology.Zone.FluxNegative, Quadrant.Flux, Aspect.Negative,
            "Rime", "Up-Left", t: true, x: true, y: true),
        Zone(Cosmology.Zone.JitterPositive, Quadrant.Jitter, Aspect.Positive,
            "Static", "Right-Up", t: false, x: false, y: true),
        Zone(Cosmology.Zone.JitterNegative, Quadrant.Jitter, Aspect.Negative,
            "Void", "Up-Right", t: true, x: true, y: false));

    private static ZoneInfo Zone(
        Zone zone, Quadrant quadrant, Aspect aspect,
        string flavor, string chiralityLabel, bool t, bool x, bool y) =>
        new(zone, quadrant, aspect, flavor, chiralityLabel, new Orientation(t, x, y));
}
