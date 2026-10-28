namespace Pockets.Core.Models;

/// <summary>
/// Result of invoking a recipe's output factory: the produced item stacks
/// and any newly created bags that need to be registered in the BagStore.
/// </summary>
public record RecipeOutput(IReadOnlyList<ItemStack> Stacks, IReadOnlyList<Bag> NewBags)
{
    /// <summary>
    /// Creates output from stacks only (no new bags).
    /// </summary>
    public static RecipeOutput FromStacks(IReadOnlyList<ItemStack> stacks) =>
        new(stacks, Array.Empty<Bag>());

    /// <summary>
    /// Creates output from stacks with associated new bags.
    /// </summary>
    public static RecipeOutput WithBags(IReadOnlyList<ItemStack> stacks, IReadOnlyList<Bag> bags) =>
        new(stacks, bags);
}

/// <summary>
/// A crafting recipe: consumes inputs over a duration and produces outputs via factory function.
/// OutputFactory is a Func so each invocation can produce unique items (e.g. bags with new Ids). It is
/// handed the step's RNG (a <see cref="Sim.SimRandom"/> over <see cref="Sim.SimStream.Craft"/> when run by a
/// facility tick) and must draw from it, never from its own <see cref="Random"/> (Slice 1.1, G2).
/// GridColumns/GridRows define the facility grid layout when this recipe is active.
/// </summary>
public record Recipe(
    string Id,
    string Name,
    IReadOnlyList<RecipeInput> Inputs,
    Func<Random, RecipeOutput> OutputFactory,
    int Duration,
    int GridColumns = 3,
    int GridRows = 1);

/// <summary>
/// One input requirement for a recipe: an item type and the count needed.
/// </summary>
public record RecipeInput(ItemType ItemType, int Count);
