using Terminal.Gui;
using Pockets.Core.Models;
using Pockets.Core.Rendering;

namespace Pockets.App.Rendering;

/// <summary>
/// Shared cell-drawing routine for the 3×2 glyph layout. Each cell renders as
/// two adjacent rows of three characters at (x, y) and (x, y+1). Category drives
/// the background color; cursor inverts to bright-yellow on black. Cells with
/// frames render row 2 in a frame-specific foreground.
/// </summary>
public static class CellDrawing
{
    /// <summary>
    /// Renders a single 3×2 cell at the given view-local coordinates.
    /// </summary>
    public static void Draw(View target, int x, int y, Cell cell, bool isCursor)
    {
        var driver = Application.Driver;
        var bg = cell.IsEmpty
            ? Color.Black
            : CategoryColors.GetBackground(cell.Stack!.ItemType.Category);
        var fg = isCursor ? Color.Black : Color.White;
        var cellAttr = isCursor
            ? driver.MakeAttribute(Color.Black, Color.BrightYellow)
            : driver.MakeAttribute(fg, bg);

        // Row 1: glyph + count (or padding)
        driver.SetAttribute(cellAttr);
        target.Move(x, y);
        foreach (var ch in GlyphRenderer.Row1(cell))
            driver.AddRune(ch);

        // Row 2: frame pattern; use frame color when present and not under the cursor
        var row2Attr = cellAttr;
        if (cell.HasFrame && !isCursor)
            row2Attr = driver.MakeAttribute(CategoryColors.GetFrameForeground(cell.Frame), bg);
        driver.SetAttribute(row2Attr);
        target.Move(x, y + 1);
        foreach (var ch in GlyphRenderer.Row2(cell))
            driver.AddRune(ch);
    }
}
