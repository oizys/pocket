namespace Pockets.App.Rendering;

/// <summary>
/// A 24-bit RGB color. Stored as RGB internally so animations (Lerp) keep
/// full precision; the renderer (ColorRenderer) quantizes to whatever the
/// current driver supports (ANSI 16 today, truecolor later). The whole point
/// of this type is that the palette and animation layers never see the
/// driver-specific Color enum — only the renderer does.
/// </summary>
public readonly record struct PaletteColor(byte R, byte G, byte B)
{
    public static readonly PaletteColor Black = new(0, 0, 0);
    public static readonly PaletteColor White = new(255, 255, 255);

    /// <summary>
    /// Linearly interpolates between two colors. t is clamped to [0, 1].
    /// </summary>
    public static PaletteColor Lerp(PaletteColor a, PaletteColor b, float t)
    {
        if (t < 0f) t = 0f;
        if (t > 1f) t = 1f;
        return new PaletteColor(
            (byte)Math.Round(a.R + (b.R - a.R) * t),
            (byte)Math.Round(a.G + (b.G - a.G) * t),
            (byte)Math.Round(a.B + (b.B - a.B) * t));
    }
}
