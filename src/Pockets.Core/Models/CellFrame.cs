namespace Pockets.Core.Models;

/// <summary>
/// Base type for cell frames — optional behavior/chrome attached to a cell.
/// Separates what a cell DOES from what it HOLDS (ItemStack).
/// Sealed subtypes form a closed discriminated union for pattern matching.
/// </summary>
public abstract record CellFrame(bool IsLocked = false);

/// <summary>
/// Marks a cell as an input slot for a facility. Optionally filters by category or specific item type.
/// ItemTypeFilter takes priority: if set, only that exact item type is accepted.
/// </summary>
public sealed record InputSlotFrame(
    string SlotId,
    Category? Filter = null,
    bool IsLocked = true,
    ItemType? ItemTypeFilter = null) : CellFrame(IsLocked)
{
    /// <summary>
    /// Returns true if the given item type is accepted by this input slot.
    /// </summary>
    public bool Accepts(ItemType itemType) =>
        ItemTypeFilter is not null
            ? itemType == ItemTypeFilter
            : Filter is null || Filter == itemType.Category;
}

/// <summary>
/// Marks a cell as an output slot for a facility. Player can only grab from it.
/// </summary>
public sealed record OutputSlotFrame(
    string SlotId,
    bool IsLocked = true) : CellFrame(IsLocked);

/// <summary>
/// Marks a cell as a planter slot for growing plants. Plants with Duration properties
/// grow over ticks and can be harvested when fully grown.
/// </summary>
public sealed record PlanterFrame(bool IsLocked = false) : CellFrame(IsLocked);

/// <summary>
/// A Shrine feature slot (Slice 5, journey 12:00): a glyph-keyed slot that accepts only the
/// matching-glyph core item and, once slotted, <b>locks irreversibly</b> (the core can never be
/// removed). The glyph-filter variant of the Stage-3 <see cref="InputSlotFrame"/> pattern.
///
/// Matching: an item fits when its <see cref="ItemStack"/> carries a <c>Glyph</c> property equal to
/// this slot's <see cref="Glyph"/> (data-driven — cores are "glyph-tagged"), unless an
/// <see cref="ItemTypeFilter"/> pins an exact type instead. Feature slots default UNLOCKED (the empty
/// eye slot the player fills); the pre-slotted Menu/Toolbar/Clock slots are constructed locked.
/// Locking is enforced by the tool layer (grab/harvest from a locked feature slot is refused).
/// </summary>
public sealed record FeatureSlotFrame(
    string Glyph,
    bool IsLocked = false,
    ItemType? ItemTypeFilter = null) : CellFrame(IsLocked)
{
    /// <summary>The per-item property key a core carries to declare which slot glyph it matches.</summary>
    public const string GlyphProperty = "Glyph";

    // Well-known demo glyph keys (the four Shrine slots).
    public const string MenuGlyph = "menu";
    public const string ToolbarGlyph = "toolbar";
    public const string ClockGlyph = "clock";
    public const string EyeGlyph = "eye";

    /// <summary>
    /// Returns true if the given stack's core-glyph matches this slot. An <see cref="ItemTypeFilter"/>,
    /// when set, takes priority (exact-type match); otherwise the stack's <c>Glyph</c> property must
    /// equal <see cref="Glyph"/>. An item with no glyph property never matches a glyph-keyed slot.
    /// </summary>
    public bool Accepts(ItemStack stack) =>
        ItemTypeFilter is not null
            ? stack.ItemType == ItemTypeFilter
            : stack.GetString(GlyphProperty) == Glyph;
}
