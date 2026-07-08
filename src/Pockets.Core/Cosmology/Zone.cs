namespace Pockets.Core.Cosmology;

/// <summary>
/// The four entropy quadrants surrounding the Core, clockwise from bottom-right
/// (per the cosmology capture). Each quadrant owns a screen corner: the "+" aspect's
/// primary reading direction points toward that corner.
/// </summary>
public enum Quadrant
{
    /// <summary>Decay / Stillness. Bottom-right corner.</summary>
    Quiet,
    /// <summary>Haze / Doubt. Bottom-left corner.</summary>
    Gloam,
    /// <summary>Sand / Change. Top-left corner.</summary>
    Flux,
    /// <summary>Blur / Chaos. Top-right corner.</summary>
    Jitter
}

/// <summary>
/// A quadrant splits into a positive and a negative aspect as one goes deeper.
/// The negative transposes the chirality of the positive (reading pair reversed).
/// </summary>
public enum Aspect
{
    Positive,
    Negative
}

/// <summary>
/// One of the 8 sub-zones of the entropy cosmology (Quadrant × Aspect).
/// </summary>
public enum Zone
{
    QuietPositive,
    QuietNegative,
    GloamPositive,
    GloamNegative,
    FluxPositive,
    FluxNegative,
    JitterPositive,
    JitterNegative
}
