using Pockets.Core.Models;

namespace Pockets.Core.Sim;

/// <summary>
/// The L1 simulation state <see cref="Simulation.Step"/> maps to its successor (plan §SimState boundary
/// inventory): the world (<see cref="Game"/>) plus the session-level L1 fields. <see cref="Content"/> is
/// the read-only L1 content Step reads but never writes.
/// <para>
/// Slice 1.2 <i>projects</i> this from <see cref="GameSession"/> (<see cref="GameSession.ToSimState"/>)
/// rather than storing it there, so the session's public shape (and every test written against it)
/// is unchanged. <see cref="SplitMode"/>, <see cref="RecipeMenu"/> and <see cref="Elapsed"/> ride along
/// untouched until Slice 1.3 routes their intents through Step. The VM keeps its exact projection:
/// <c>recipeMenu</c> serialized, <c>SplitMode</c> not. <c>SimRng</c> joins in Slice 1.1.
/// </para>
/// </summary>
public sealed record SimState(
    GameState Game,
    int TickCount,
    TimeSpan Elapsed,
    SplitModeState? SplitMode,
    RecipeMenuState? RecipeMenu,
    SimContent Content);

/// <summary>
/// L1 content: definitions loaded at startup that <see cref="Simulation.Step"/> reads and no step ever
/// changes: the recipe set, the facility→recipe map and the dialogue beat book.
/// </summary>
public sealed record SimContent(
    ImmutableArray<Recipe> Recipes,
    ImmutableDictionary<string, ImmutableArray<string>>? FacilityRecipeMap,
    DialogueBook Beats)
{
    /// <summary>
    /// The recipes a facility of <paramref name="environmentType"/> can build, given the recipes the
    /// player knows. The Crafting Table is a generic assembler (KnownRecipes ∩ the loaded set); other
    /// facilities use <see cref="FacilityRecipeMap"/> when present, else the legacy registry prefixes.
    /// (Moved verbatim from <c>GameSession.GetRecipesForFacility</c>, with the known set made explicit:
    /// callers pass the <i>pre-step</i> state's <see cref="GameState.KnownRecipes"/>, as the session did.)
    /// </summary>
    public IReadOnlyList<Recipe> RecipesForFacility(string environmentType, ImmutableHashSet<string> knownRecipes)
    {
        // The Crafting Table (Slice 7) is a generic assembler: its craftable set is exactly the
        // recipes the player KNOWS (KnownRecipes ∩ the loaded recipe set) — the Slice-6 deferred
        // wiring. Learn a recipe and the table can build it the moment its inputs match; an unknown
        // recipe (or a known one with no table inputs, e.g. the Iron-Ore-gated demo axe) never crafts.
        if (environmentType == GameState.CraftingTableEnvironment)
            return Recipes.Where(r => knownRecipes.Contains(r.Id)).ToList();

        if (FacilityRecipeMap is not null &&
            FacilityRecipeMap.TryGetValue(environmentType, out var recipeIds))
        {
            var recipesById = Recipes.ToDictionary(r => r.Id);
            return recipeIds
                .Where(id => recipesById.ContainsKey(id))
                .Select(id => recipesById[id])
                .ToList();
        }

        // Legacy fallback
        return Data.RecipeRegistry.GetRecipesForFacility(environmentType, Recipes);
    }
}
