using Pockets.Core.Models;

namespace Pockets.Core.Rendering;

/// <summary>
/// Stage-4 cell renderer: returns the two 3-character rows for a Cell rendered
/// at 3x2 (visually-square at typical 2:1 terminal aspect). Pure function of
/// the cell — color/attributes are layered on top by the view.
///
/// Layout (Option B):
///   row 1: glyph + right-aligned count (or 2 spaces for unique/empty)
///   row 2: frame pattern: "   " (none), "▼▼▼" (InputSlot),
///           "▲▲▲" (OutputSlot), "≈≈≈" (Planter)
/// </summary>
public static class GlyphRenderer
{
    public const int CellWidth = 3;
    public const int CellHeight = 2;

    /// <summary>
    /// The 3-character row 1 for a cell: glyph + count (or padding for empty/unique).
    /// </summary>
    public static string Row1(Cell cell)
    {
        if (cell.IsEmpty)
            return "   ";

        var stack = cell.Stack!;
        var glyph = GlyphFor(stack.ItemType);

        if (!stack.ItemType.IsStackable)
            return glyph + "  ";

        // Stackable: right-align count in remaining 2 chars
        var count = stack.Count;
        var countText = count switch
        {
            <= 0 => "  ",
            < 10 => " " + count,
            < 100 => count.ToString(),
            _ => "9+"
        };
        return glyph + countText;
    }

    /// <summary>
    /// The 3-character row 2 for a cell: frame pattern, or 3 spaces if none.
    /// </summary>
    public static string Row2(Cell cell) => cell.Frame switch
    {
        InputSlotFrame  => "▼▼▼", // ▼▼▼
        OutputSlotFrame => "▲▲▲", // ▲▲▲
        PlanterFrame    => "≈≈≈", // ≈≈≈
        _               => "   "
    };

    /// <summary>
    /// Single-character glyph for an item type. Defaults to the first letter of
    /// the name, uppercased — item-type drives glyph identity; category drives
    /// color (handled by the view). Names with non-letter first chars fall back
    /// to '?'.
    /// </summary>
    public static string GlyphFor(ItemType itemType)
    {
        if (string.IsNullOrEmpty(itemType.Name))
            return "?";
        var ch = itemType.Name[0];
        if (!char.IsLetter(ch))
            return "?";
        return char.ToUpperInvariant(ch).ToString();
    }
}
