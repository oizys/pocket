using Pockets.Core.Rendering;

namespace Pockets.App.Rendering;

/// <summary>
/// Terminal-cell layout constants for the App's grid views. Each logical cell
/// occupies a 5×3 envelope: a 2-char gap on the left and a 1-row gap on top,
/// then the 3×2 glyph content area at offset (+2, +1). The 2-col vs 1-row gap
/// keeps the moat visually equal at a typical 2:1 cell aspect (a row is about
/// twice as tall as a char is wide).
///
/// The gap is rendered with a neutral (bg=black) attribute so adjacent cells of
/// the same category color remain visually distinguishable — and it's reserved
/// for future cursor / selection / frame badges, so its chars and attrs must
/// never be reused for cell content.
/// </summary>
public static class CellRenderer
{
    /// <summary>Total terminal columns per cell (gap + content).</summary>
    public const int CellWidth = ContentWidth + GapLeft;       // 5

    /// <summary>Total terminal rows per cell (gap + content).</summary>
    public const int CellHeight = ContentHeight + GapTop;      // 3

    /// <summary>Width of the cell content area (glyph + count, frame pattern).</summary>
    public const int ContentWidth = GlyphRenderer.CellWidth;   // 3

    /// <summary>Height of the cell content area.</summary>
    public const int ContentHeight = GlyphRenderer.CellHeight; // 2

    /// <summary>Left gap (preceding the cell content).</summary>
    public const int GapLeft = 2;

    /// <summary>Top gap (preceding the cell content).</summary>
    public const int GapTop = 1;
}
