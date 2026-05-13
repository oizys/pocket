using Terminal.Gui;
using Pockets.Core.Models;
using Pockets.Core.Rendering;

namespace Pockets.App.Rendering;

/// <summary>
/// Shared cell-drawing routine for the 5×3 envelope (3×2 content + 2-col left
/// gap + 1-row top gap). The gap is reserved for future cursor / selection /
/// frame badges; its chars and attrs must never be reused for cell content.
/// </summary>
public static class CellDrawing
{
    /// <summary>
    /// Renders a single cell envelope at the given view-local coordinates.
    /// The cell envelope is CellRenderer.CellWidth × CellRenderer.CellHeight
    /// (5×3) starting at (x, y). The gap rows/cols are cleared to bg=black; the
    /// cell content (glyph + count, frame pattern) is painted at the
    /// (+GapLeft, +GapTop) offset within the envelope.
    /// </summary>
    public static void Draw(View target, int x, int y, Cell cell, bool isCursor)
    {
        var driver = Application.Driver;

        // Gap: bg=black, fg=dim. Always neutral so cells of the same category
        // color don't blend into each other.
        var gapAttr = driver.MakeAttribute(Color.DarkGray, Color.Black);
        driver.SetAttribute(gapAttr);
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
            ? Color.Black
            : CategoryColors.GetBackground(cell.Stack!.ItemType.Category);
        var contentAttr = isCursor
            ? driver.MakeAttribute(Color.Black, Color.BrightYellow)
            : driver.MakeAttribute(Color.White, bg);

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
            row2Attr = driver.MakeAttribute(CategoryColors.GetFrameForeground(cell.Frame), bg);
        driver.SetAttribute(row2Attr);
        target.Move(cx, cy + 1);
        foreach (var ch in GlyphRenderer.Row2(cell))
            driver.AddRune(ch);
    }
}
