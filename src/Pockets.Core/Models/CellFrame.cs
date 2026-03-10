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
