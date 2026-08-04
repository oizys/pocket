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
        // Invariant upper-casing: item abbreviations land in the TUI buffer goldens, and the default
        // ToUpper() would map 'i'→'İ' under a Turkish host locale (the classic Turkish-I bug) — drifting
        // every golden built on such a machine. Culture must never touch golden-bound text.
        var words = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 1)
            return name.Length <= maxLength ? name.ToUpperInvariant() : name[..maxLength].ToUpperInvariant();
        var initials = string.Concat(words.Select(w => char.ToUpperInvariant(w[0])));
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
    /// Returns a short cell content string with optional cursor marker prepended.
    /// </summary>
    public static string FormatCell(ItemStack? stack, bool isCursor)
    {
        var marker = isCursor ? ">" : "";
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
    /// The environment header suffix for a bag that carries an entropy glyph (Slice 6): the Quiet 1
    /// wilderness shows its palette + a text approximation of the Quiet+ staircase glyph, with a leading
    /// separator so callers can append the result to the breadcrumb line <b>unconditionally</b>. Empty
    /// (no separator) for ordinary bags. The ASCII staircase <c>=== == =</c> stands in for the real SVG
    /// (<c>assets/glyphs/basis-quiet-positive.svg</c>); the Godot SVG→Texture2D import is the deferred
    /// glyph pass (design/glyphs.md). Cross-driver the authoritative signal is the VM <c>env</c> fields —
    /// this string is the shallow "is it drawn" tell.
    /// </summary>
    public static string FormatEnvHeader(Bag bag)
    {
        if (string.IsNullOrEmpty(bag.Glyph)) return "";
        return $"   [{bag.EnvironmentType} · {GlyphArt(bag.Glyph)} · {bag.ColorScheme}]";
    }

    /// <summary>A compact ASCII approximation of an entropy glyph for the text frontends.</summary>
    private static string GlyphArt(string glyph) => glyph switch
    {
        // Quiet+ is the descending "sort by ascending" staircase (long / mid / short) — evoked as three
        // decreasing runs of '='.
        "quiet-positive" => "=== == =",
        _ => glyph
    };

    /// <summary>
    /// Summarizes the hand state as a single line.
    /// </summary>
    public static string FormatHandSummary(GameState state)
    {
        if (!state.HasItemsInHand) return "Hand: empty";

        var items = state.HandItems
            .Select(stack => FormatStack(stack))
            .ToList();

        return $"Hand: {string.Join(", ", items)}";
    }

    /// <summary>
    /// One-line summary of the toolbar's occupied slots (Slice 3): the fixed inventory as the bottom
    /// bar. A slot holding a carrying bag shows its nested count (e.g. "Coin Pouch[1]"). Depth-invariant
    /// — reads the T bag straight from the store, so it is identical no matter how deep B has navigated.
    /// Used by the thin Godot bottom bar to align it to the same model the TUI T panel renders from.
    /// </summary>
    public static string FormatToolbarSummary(GameState state)
    {
        if (state.ToolbarBagId is not { } id || state.Store.GetById(id) is not { } toolbar)
            return "Toolbar: —";

        var slots = new List<string>();
        foreach (var cell in toolbar.Grid.Cells)
        {
            if (cell.Stack is not { } stack) continue;
            var label = FormatStack(stack);
            if (stack.ContainedBagId is { } nestedId && state.Store.GetById(nestedId) is { } nested)
            {
                var used = nested.Grid.Cells.Count(c => !c.IsEmpty);
                label += $"[{used}/{nested.Grid.Cells.Length}]";
            }
            slots.Add(label);
        }

        return slots.Count == 0 ? "Toolbar: empty" : $"Toolbar: {string.Join(" | ", slots)}";
    }
}
