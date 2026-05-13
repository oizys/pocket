using Terminal.Gui;
using Pockets.Core.Models;
using Pockets.Core.Rendering;

namespace Pockets.App.Rendering;

/// <summary>
/// Shared cell-drawing routine for the 5×3 envelope (3×2 content + 2-col left
/// gap + 1-row top gap). The gap is reserved for future cursor / selection /
/// frame badges; its chars and attrs must never be reused for cell content.
///
/// Colors flow through PaletteColor (RGB) and are quantized to the current
/// driver's ANSI 16 via ColorRenderer at attribute-build time. The whole
/// cell-rendering pipeline never touches Terminal.Gui's Color enum directly,
/// so swapping to truecolor later is a single-file change in ColorRenderer.
/// </summary>
public static class CellDrawing
{
    /// <summary>
    /// Renders a single cell envelope at the given view-local coordinates.
    /// </summary>
    public static void Draw(View target, int x, int y, Cell cell, bool isCursor)
    {
        var driver = Application.Driver;

        // Gap: slightly-off-black moat reserved for future overlays.
        driver.SetAttribute(GapAttr());
        for (int gy = 0; gy < CellRenderer.GapTop; gy++)
        {
            target.Move(x, y + gy);
            for (int gx = 0; gx < CellRenderer.CellWidth; gx++)
                driver.AddRune(' ');
        }
        for (int row = CellRenderer.GapTop; row < CellRenderer.CellHeight; row++)
        {
            target.Move(x, y + row);
            for (int gx = 0; gx < CellRenderer.GapLeft; gx++)
                driver.AddRune(' ');
        }

        // Content area: category-colored bg, white fg (or inverted for cursor).
        var bg = cell.IsEmpty
            ? PaletteColor.Black
            : Palette.CategoryBackground(cell.Stack!.ItemType.Category);
        var fg = cell.IsEmpty
            ? PaletteColor.White
            : Palette.CellForeground(cell.Stack!.ItemType.Category);
        var contentAttr = isCursor
            ? ColorRenderer.MakeAttribute(Palette.CursorForeground, Palette.CursorBackground)
            : ColorRenderer.MakeAttribute(fg, bg);

        var cx = x + CellRenderer.GapLeft;
        var cy = y + CellRenderer.GapTop;

        // Row 1: glyph + count
        driver.SetAttribute(contentAttr);
        target.Move(cx, cy);
        foreach (var ch in GlyphRenderer.Row1(cell))
            driver.AddRune(ch);

        // Row 2: frame pattern (frame-specific fg when present and not cursor)
        var row2Attr = contentAttr;
        if (cell.HasFrame && !isCursor)
            row2Attr = ColorRenderer.MakeAttribute(Palette.FrameForeground(cell.Frame), bg);
        driver.SetAttribute(row2Attr);
        target.Move(cx, cy + 1);
        foreach (var ch in GlyphRenderer.Row2(cell))
            driver.AddRune(ch);
    }

    /// <summary>Attribute used for every gap pixel.</summary>
    public static Terminal.Gui.Attribute GapAttr() =>
        ColorRenderer.MakeAttribute(Palette.GapForeground, Palette.GapBackground);

    /// <summary>
    /// Fills a rectangular region of the view with the gap attribute (spaces).
    /// Used to paint the trailing right/bottom gap around the grid.
    /// </summary>
    public static void FillGap(View target, int x, int y, int width, int height)
    {
        var driver = Application.Driver;
        driver.SetAttribute(GapAttr());
        for (int row = 0; row < height; row++)
        {
            target.Move(x, y + row);
            for (int col = 0; col < width; col++)
                driver.AddRune(' ');
        }
    }
}
