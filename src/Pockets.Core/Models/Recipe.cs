namespace Pockets.Core.Models;

/// <summary>
/// A crafting recipe: consumes inputs over a duration and produces outputs via factory function.
/// OutputFactory is a Func so each invocation can produce unique items (e.g. bags with new Ids).
/// </summary>
public record Recipe(
    string Id,
    string Name,
    IReadOnlyList<RecipeInput> Inputs,
    Func<IReadOnlyList<ItemStack>> OutputFactory,
    int Duration);

/// <summary>
/// One input requirement for a recipe: an item type and the count needed.
/// </summary>
public record RecipeInput(ItemType ItemType, int Count);
