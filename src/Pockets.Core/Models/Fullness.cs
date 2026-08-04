namespace Pockets.Core.Models;

/// <summary>
/// A bag's carrying fullness at a glance (Slice 5, the eye-of-fullness unlock, journey 28:00):
/// the pip state rendered on every bag cell once the eye core is slotted at the Shrine.
/// </summary>
public enum FullnessPip
{
    /// <summary>The contained bag holds nothing.</summary>
    Empty,
    /// <summary>The contained bag has some items but at least one free cell.</summary>
    Partial,
    /// <summary>Every cell of the contained bag is occupied — no free space.</summary>
    Full
}

/// <summary>
/// Core-computed fullness-pip state (Slice 5). Both frontends render <i>only</i> from this — Core
/// decides empty/partial/full per bag, so the eye-core unlock rewires the pip rendering identically
/// everywhere (room, toolbar, nested) with zero per-frontend game logic. Fullness is measured by
/// cell occupancy of the contained bag (occupied / total), the same signal a glance gives.
/// </summary>
public static class Fullness
{
    /// <summary>
    /// The pip state of the bag a stack contains, or null when the stack is not a bag (or its bag
    /// reference doesn't resolve). Resolving the contained bag needs the store.
    /// </summary>
    public static FullnessPip? Of(BagStore store, ItemStack stack)
    {
        if (stack.ContainedBagId is not { } bagId)
            return null;
        var bag = store.GetById(bagId);
        return bag is null ? null : Of(bag);
    }

    /// <summary>
    /// The pip state of a bag by its cell occupancy: empty (0 occupied), full (all occupied),
    /// partial (in between).
    /// </summary>
    public static FullnessPip Of(Bag bag)
    {
        var cells = bag.Grid.Cells;
        var occupied = cells.Count(c => !c.IsEmpty);
        if (occupied == 0) return FullnessPip.Empty;
        if (occupied >= cells.Length) return FullnessPip.Full;
        return FullnessPip.Partial;
    }

    /// <summary>The canonical view-model spelling of a pip (stable, cross-driver).</summary>
    public static string Name(FullnessPip pip) => pip switch
    {
        FullnessPip.Empty => "empty",
        FullnessPip.Partial => "partial",
        FullnessPip.Full => "full",
        _ => "empty"
    };

    /// <summary>The single-character pip glyph both frontends draw (empty ○ / partial ◐ / full ●).</summary>
    public static char Glyph(FullnessPip pip) => pip switch
    {
        FullnessPip.Empty => '○',
        FullnessPip.Partial => '◐',
        FullnessPip.Full => '●',
        _ => '○'
    };
}
