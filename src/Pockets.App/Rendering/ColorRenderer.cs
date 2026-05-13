using Terminal.Gui;

namespace Pockets.App.Rendering;

/// <summary>
/// Bridges PaletteColor (RGB) to Terminal.Gui's color system. Today we quantize
/// to the nearest ANSI 16 color because Terminal.Gui 1.x's `Color` enum has no
/// truecolor surface; upgrading to 2.x (or doing a raw-ANSI bypass) is a
/// single-file change limited to this class.
/// </summary>
public static class ColorRenderer
{
    /// <summary>
    /// Builds a Terminal.Gui Attribute by quantizing each PaletteColor to the
    /// nearest ANSI 16.
    /// </summary>
    public static Terminal.Gui.Attribute MakeAttribute(PaletteColor fg, PaletteColor bg) =>
        Application.Driver.MakeAttribute(ToAnsi16(fg), ToAnsi16(bg));

    /// <summary>
    /// Quantizes a PaletteColor to the nearest Terminal.Gui Color (ANSI 16) by
    /// Euclidean distance in RGB space. Crude but effective for category
    /// palettes; replace this when truecolor lands.
    /// </summary>
    public static Color ToAnsi16(PaletteColor c)
    {
        var best = Ansi16Table[0];
        var bestDist = SquaredDistance(c, best.Rgb);
        for (int i = 1; i < Ansi16Table.Length; i++)
        {
            var entry = Ansi16Table[i];
            var d = SquaredDistance(c, entry.Rgb);
            if (d < bestDist)
            {
                bestDist = d;
                best = entry;
            }
        }
        return best.Color;
    }

    private static int SquaredDistance(PaletteColor a, PaletteColor b)
    {
        int dr = a.R - b.R;
        int dg = a.G - b.G;
        int db = a.B - b.B;
        return dr * dr + dg * dg + db * db;
    }

    private readonly record struct Ansi16Entry(Color Color, PaletteColor Rgb);

    // Standard xterm-16 RGB approximations.
    private static readonly Ansi16Entry[] Ansi16Table =
    {
        new(Color.Black,         new(0, 0, 0)),
        new(Color.Blue,          new(0, 0, 170)),
        new(Color.Green,         new(0, 170, 0)),
        new(Color.Cyan,          new(0, 170, 170)),
        new(Color.Red,           new(170, 0, 0)),
        new(Color.Magenta,       new(170, 0, 170)),
        new(Color.Brown,         new(170, 85, 0)),
        new(Color.Gray,          new(170, 170, 170)),
        new(Color.DarkGray,      new(85, 85, 85)),
        new(Color.BrightBlue,    new(85, 85, 255)),
        new(Color.BrightGreen,   new(85, 255, 85)),
        new(Color.BrightCyan,    new(85, 255, 255)),
        new(Color.BrightRed,     new(255, 85, 85)),
        new(Color.BrightMagenta, new(255, 85, 255)),
        new(Color.BrightYellow,  new(255, 255, 85)),
        new(Color.White,         new(255, 255, 255)),
    };
}
