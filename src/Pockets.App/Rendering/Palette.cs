using Pockets.Core.Models;

namespace Pockets.App.Rendering;

/// <summary>
/// Category, frame, and gap colors as 24-bit RGB. Values are tuned to read
/// cleanly when quantized to ANSI 16 today (so the relative palette stays
/// recognizable when truecolor lands later — same look, just sharper).
/// </summary>
public static class Palette
{
    /// <summary>Background color for a category-colored cell.</summary>
    /// <remarks>
    /// Values are picked to quantize cleanly to their intended ANSI 16 hue under
    /// the current ColorRenderer (Euclidean-distance quantization). When
    /// truecolor lands the renderer will honor the exact RGB and you can
    /// re-desaturate freely without losing the hue identity.
    /// </remarks>
    public static PaletteColor CategoryBackground(Category c) => c switch
    {
        Category.Material   => new(96, 96, 96),     // dim gray   → DarkGray
        Category.Weapon     => new(150, 40, 40),    // red        → Red
        Category.Structure  => new(130, 90, 40),    // brown      → Brown
        Category.Medicine   => new(30, 170, 60),    // green      → Green
        Category.Tool       => new(40, 60, 200),    // blue       → BrightBlue
        Category.Bag        => new(160, 40, 180),   // magenta    → Magenta
        Category.Consumable => new(40, 130, 140),   // teal       → Cyan
        Category.Misc       => new(90, 90, 90),     // medium gray → DarkGray
        _                   => new(90, 90, 90),
    };

    /// <summary>Foreground for the cell content (glyph + count) on a category background.</summary>
    public static PaletteColor CellForeground(Category _) => PaletteColor.White;

    /// <summary>Foreground for the frame-pattern row 2 of a cell.</summary>
    public static PaletteColor FrameForeground(CellFrame? frame) => frame switch
    {
        InputSlotFrame  => new(240, 220, 80),   // warm yellow
        OutputSlotFrame => new(120, 220, 120),  // bright green
        PlanterFrame    => new(110, 220, 220),  // bright cyan
        _               => PaletteColor.White,
    };

    /// <summary>
    /// Background color of the gap moat between/around cells. Tuned to quantize
    /// to DarkGray (distinct from the pure-black empty-cell background) so
    /// adjacent same-category cells don't blend.
    /// </summary>
    public static PaletteColor GapBackground => new(85, 85, 85);

    /// <summary>Foreground for gap chars (which are always spaces, so this rarely matters).</summary>
    public static PaletteColor GapForeground => new(85, 85, 85);

    /// <summary>Cursor cell background (bright accent so it's unmissable).</summary>
    public static PaletteColor CursorBackground => new(240, 220, 80); // bright yellow

    /// <summary>Cursor cell foreground.</summary>
    public static PaletteColor CursorForeground => PaletteColor.Black;
}
