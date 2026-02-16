namespace Pockets.Core.Models;

/// <summary>
/// Defines an item type. Stackable items share a stack count; unique items occupy one cell each.
/// </summary>
public record ItemType(
    string Name,
    Category Category,
    bool IsStackable,
    int MaxStackSize = 20,
    string Description = "")
{
    /// <summary>
    /// Returns MaxStackSize for stackable items, 1 for unique items.
    /// </summary>
    public int EffectiveMaxStackSize => IsStackable ? MaxStackSize : 1;
}
