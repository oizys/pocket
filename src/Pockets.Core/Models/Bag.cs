namespace Pockets.Core.Models;

/// <summary>
/// An openable item containing a Grid of cells. Bags nest inside other bags.
/// Each bag has a stable Id that persists through mutations (via `with` expressions).
/// Facility bags have an optional FacilityState for crafting progress tracking.
/// </summary>
public record Bag(
    Grid Grid,
    string EnvironmentType = "Default",
    string ColorScheme = "Default",
    FacilityState? FacilityState = null)
{
    /// <summary>
    /// Stable identity for this bag. Auto-generated if not set.
    /// Preserved through `with` expressions since it's a record property.
    /// </summary>
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>
    /// Enter-only bags (the wilderness posture, journey 15:30) refuse a look-in peek: the player can
    /// cross the threshold with <c>E</c> (breadcrumbs) but cannot arrange them from outside with <c>C</c>.
    /// A failed peek is the ONLY tell — there is deliberately NO visible glyph/frame marking an
    /// enter-only bag in the demo (RATIFIED 2026-08-03; the eyelid-shut glyph is a banked future Shrine
    /// unlock). Defaults false so every existing bag stays freely peekable. Travels with the bag through
    /// the store, so grab/drop/sort of the owning item preserve the property.
    /// </summary>
    public bool EnterOnly { get; init; } = false;

    /// <summary>
    /// Places item stacks into this bag's grid. Returns a new Bag with the updated grid
    /// and any stacks that couldn't be placed. Optional skipIndices excludes specific cells.
    /// </summary>
    public (Bag UpdatedBag, IReadOnlyList<ItemStack> Unplaced) AcquireItems(
        IEnumerable<ItemStack> stacks, ImmutableHashSet<int>? skipIndices = null)
    {
        var (updatedGrid, unplaced) = Grid.AcquireItems(stacks, skipIndices);
        return (this with { Grid = updatedGrid }, unplaced);
    }

}
