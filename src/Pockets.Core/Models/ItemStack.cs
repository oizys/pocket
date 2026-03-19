using System.Collections.Immutable;

namespace Pockets.Core.Models;

/// <summary>
/// An ItemType paired with a count. Supports merging same-type stacks and splitting.
/// Bag-type items carry their contents via ContainedBag, so the bag travels with the item.
/// Unique (non-stackable) items may carry per-instance Properties.
/// </summary>
public record ItemStack(
    ItemType ItemType,
    int Count,
    Bag? ContainedBag = null,
    ImmutableDictionary<string, PropertyValue>? Properties = null)
{
    /// <summary>
    /// Attempts to merge another stack into this one.
    /// Same type: merged up to EffectiveMaxStackSize, excess returned as remainder.
    /// Different type: returns (this, other) unchanged.
    /// </summary>
    public (ItemStack Merged, ItemStack? Remainder) TryMerge(ItemStack other)
    {
        if (other.ItemType != ItemType)
            return (this, other);

        var max = ItemType.EffectiveMaxStackSize;
        var total = Count + other.Count;
        if (total <= max)
            return (this with { Count = total }, null);

        var excess = total - max;
        return (this with { Count = max }, other with { Count = excess });
    }

    /// <summary>
    /// Splits this stack into two. Default leftCount is ceiling half: (Count + 1) / 2.
    /// Returns null if Count ≤ 1 or leftCount is out of valid range [1, Count-1].
    /// </summary>
    public (ItemStack Left, ItemStack Right)? Split(int? leftCount = null)
    {
        var left = leftCount ?? (Count + 1) / 2;

        if (Count <= 1 || left < 1 || left >= Count)
            return null;

        return (this with { Count = left }, this with { Count = Count - left });
    }

    /// <summary>
    /// Returns true if this item has any per-instance properties.
    /// </summary>
    public bool HasProperties => Properties is { Count: > 0 };

    /// <summary>
    /// Gets an integer property by name, or null if not found or wrong type.
    /// </summary>
    public int? GetInt(string name) =>
        Properties?.TryGetValue(name, out var val) == true && val is IntValue iv ? iv.Value : null;

    /// <summary>
    /// Gets a string property by name, or null if not found or wrong type.
    /// </summary>
    public string? GetString(string name) =>
        Properties?.TryGetValue(name, out var val) == true && val is StringValue sv ? sv.Value : null;

    /// <summary>
    /// Returns a new ItemStack with the given property set. Only valid for unique (non-stackable) items.
    /// Returns this unchanged if the item is stackable.
    /// </summary>
    public ItemStack WithProperty(string name, PropertyValue value) =>
        ItemType.IsStackable
            ? this
            : this with { Properties = (Properties ?? ImmutableDictionary<string, PropertyValue>.Empty).SetItem(name, value) };

    /// <summary>
    /// Returns a new ItemStack with the named property removed.
    /// Returns this unchanged if the property doesn't exist or item has no properties.
    /// </summary>
    public ItemStack WithoutProperty(string name) =>
        Properties is null || !Properties.ContainsKey(name)
            ? this
            : this with { Properties = Properties.Remove(name) };
}
