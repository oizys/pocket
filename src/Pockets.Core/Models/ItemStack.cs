namespace Pockets.Core.Models;

/// <summary>
/// An ItemType paired with a count. Supports merging same-type stacks and splitting.
/// </summary>
public record ItemStack(ItemType ItemType, int Count)
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
}
