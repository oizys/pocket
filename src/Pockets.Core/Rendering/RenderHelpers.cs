using Pockets.Core.Models;

namespace Pockets.Core.Rendering;

/// <summary>
/// Shared pure helpers for text-based state rendering.
/// </summary>
public static class RenderHelpers
{
    /// <summary>
    /// Maps each Category to a single-width character for grid display.
    /// Uses ASCII-safe chars to avoid variable-width Unicode rendering issues.
    /// </summary>
    public static char CategorySymbol(Category category) => category switch
    {
        Category.Material   => 'm',
        Category.Weapon     => 'w',
        Category.Structure  => 's',
        Category.Medicine   => '+',
        Category.Tool       => 't',
        Category.Bag        => 'b',
        Category.Consumable => 'c',
        _                   => '.',
    };

    /// <summary>
    /// Abbreviates an item name: multi-word takes first letter of each word uppercased,
    /// single word takes first N chars uppercased (default 5). Use maxLength to cap for tight layouts.
    /// </summary>
    public static string AbbreviateName(string name, int maxLength = 5)
    {
        var words = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 1)
            return name.Length <= maxLength ? name.ToUpper() : name[..maxLength].ToUpper();
        var initials = string.Concat(words.Select(w => char.ToUpper(w[0])));
        return initials.Length <= maxLength ? initials : initials[..maxLength];
    }

    /// <summary>
    /// Formats an ItemStack as a compact string: category symbol + abbreviation + ×count.
    /// Unique items omit the count. Null returns empty string.
    /// </summary>
    public static string FormatStack(ItemStack? stack)
    {
        if (stack is null) return "";
        var sym = CategorySymbol(stack.ItemType.Category);
        var abbr = AbbreviateName(stack.ItemType.Name);
        return stack.ItemType.IsStackable
            ? $"{sym}{abbr}×{stack.Count}"
            : $"{sym}{abbr}";
    }

    /// <summary>
    /// Returns a short cell content string with optional cursor/hand markers prepended.
    /// </summary>
    public static string FormatCell(ItemStack? stack, bool isCursor, bool isHand)
    {
        var marker = isCursor ? ">" : isHand ? "#" : "";
        var content = FormatStack(stack);
        return $"{marker}{content}";
    }

    /// <summary>
    /// Describes the item at the cursor position. Returns "(empty)" for empty cells.
    /// </summary>
    public static string DescribeCursorItem(GameState state)
    {
        var cell = state.CurrentCell;
        if (cell.IsEmpty) return "(empty)";

        var stack = cell.Stack!;
        var type = stack.ItemType;
        var sym = CategorySymbol(type.Category);
        var kind = type.IsStackable
            ? $"Stackable {stack.Count}/{type.EffectiveMaxStackSize}"
            : "Unique";
        var desc = string.IsNullOrEmpty(type.Description) ? "" : $"\n  {type.Description}";
        return $"{type.Name} {sym} {type.Category} | {kind}{desc}";
    }

    /// <summary>
    /// Summarizes the hand state as a single line.
    /// </summary>
    public static string FormatHandSummary(GameState state)
    {
        if (!state.HasItemsInHand) return "Hand: empty";

        var items = state.Hand!
            .Select(pos => (pos, stack: state.RootBag.Grid.GetCell(pos).Stack))
            .Where(x => x.stack is not null)
            .Select(x => $"{FormatStack(x.stack)} @({x.pos.Row},{x.pos.Col})")
            .ToList();

        return items.Count > 0
            ? $"Hand: {string.Join(", ", items)}"
            : "Hand: (marked but empty)";
    }
}
