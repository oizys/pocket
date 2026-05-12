using Pockets.Core.Rendering;

namespace Pockets.App.Rendering;

/// <summary>
/// Terminal-cell layout constants for the App's grid views. Cells are 3×2 — three
/// terminal columns wide, two rows tall — with no per-cell borders. Content
/// (glyph + count, frame pattern) is produced by Pockets.Core.Rendering.GlyphRenderer.
/// Kept as a thin re-export so views don't have to take a transitive Core dependency
/// on Rendering directly.
/// </summary>
public static class CellRenderer
{
    public const int CellWidth = GlyphRenderer.CellWidth;   // 3
    public const int CellHeight = GlyphRenderer.CellHeight; // 2
}
