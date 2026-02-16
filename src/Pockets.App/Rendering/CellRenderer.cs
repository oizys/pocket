using Pockets.Core.Models;

namespace Pockets.App.Rendering;

/// <summary>
/// Static helpers for rendering grid cells as text.
/// </summary>
public static class CellRenderer
{
    public const int CellWidth = 10;
    public const int CellHeight = 3;
    public const int ContentWidth = 8;

    /// <summary>
    /// Abbreviates an item name by taking the first letter of each word, uppercased.
    /// Single-word names are truncated to 5 characters.
    /// </summary>
    public static string AbbreviateName(string name)
    {
        var words = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 1)
            return name.Length <= 5 ? name.ToUpper() : name[..5].ToUpper();
        return string.Concat(words.Select(w => char.ToUpper(w[0])));
    }

    /// <summary>
    /// Returns the 8-character content string for a cell.
    /// Empty cells return spaces. Unique items show abbreviation only.
    /// Stackable items show abbreviation + multiplication sign + count.
    /// </summary>
    public static string GetCellContent(Cell cell)
    {
        if (cell.IsEmpty)
            return new string(' ', ContentWidth);

        var stack = cell.Stack!;
        var abbrev = AbbreviateName(stack.ItemType.Name);

        if (!stack.ItemType.IsStackable)
            return abbrev.PadRight(ContentWidth);

        var content = $"{abbrev}\u00d7{stack.Count}";
        return content.Length <= ContentWidth
            ? content.PadRight(ContentWidth)
            : content[..ContentWidth];
    }
}
