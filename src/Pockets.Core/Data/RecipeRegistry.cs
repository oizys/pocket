using System.Collections.Immutable;
using Pockets.Core.Models;

namespace Pockets.Core.Data;

/// <summary>
/// Hardcoded recipe definitions for Stage 3. Looks up item types by name from the loaded set.
/// Each facility type has exactly one recipe. OutputFactory produces fresh instances on each call.
/// </summary>
public static class RecipeRegistry
{
    public const string WorkbenchFacilityType = "Workbench";
    public const string TannerFacilityType = "Tanner";
    public const string SeedlingPotFacilityType = "Seedling Pot";

    /// <summary>
    /// Builds all recipes from the loaded item types. Returns empty list if required types are missing.
    /// </summary>
    public static ImmutableArray<Recipe> BuildRecipes(ImmutableArray<ItemType> itemTypes)
    {
        var byName = itemTypes.ToDictionary(t => t.Name);
        var recipes = new List<Recipe>();

        // Workbench: Plain Rock ×5 + Rough Wood ×3 → Stone Axe
        if (byName.TryGetValue("Plain Rock", out var rock) &&
            byName.TryGetValue("Rough Wood", out var wood) &&
            byName.TryGetValue("Stone Axe", out var stoneAxe))
        {
            recipes.Add(new Recipe(
                "workbench_axe", "Stone Axe",
                new[] { new RecipeInput(rock, 5), new RecipeInput(wood, 3) },
                () => new[] { new ItemStack(stoneAxe, 1) },
                Duration: 3));
        }

        // Tanner: Tanned Leather ×3 + Woven Fiber ×2 → Belt Pouch (2×3 bag)
        if (byName.TryGetValue("Tanned Leather", out var leather) &&
            byName.TryGetValue("Woven Fiber", out var fiber))
        {
            var pouchType = byName.TryGetValue("Belt Pouch", out var pt)
                ? pt
                : new ItemType("Belt Pouch", Category.Bag, IsStackable: false);

            recipes.Add(new Recipe(
                "tanner_pouch", "Belt Pouch",
                new[] { new RecipeInput(leather, 3), new RecipeInput(fiber, 2) },
                () =>
                {
                    var bag = new Bag(Grid.Create(3, 2), "Pouch", "Brown");
                    return new[] { new ItemStack(pouchType, 1, ContainedBag: bag) };
                },
                Duration: 5));
        }

        // Seedling Pot: Forest Seed ×5 + Rich Soil ×3 → Forest Wilderness Bag
        if (byName.TryGetValue("Forest Seed", out var seed) &&
            byName.TryGetValue("Rich Soil", out var soil))
        {
            var forestBagType = byName.TryGetValue("Forest Bag", out var fbt)
                ? fbt
                : new ItemType("Forest Bag", Category.Bag, IsStackable: false);

            // Build loot table from all material items
            var materials = itemTypes.Where(t => t.Category == Category.Material).ToImmutableArray();
            var lootTable = materials.Select(m => (m, 1.0)).ToImmutableArray();
            var template = new WildernessTemplate("Forest", "Forest", "Green", 6, 4, 0.6, lootTable);

            recipes.Add(new Recipe(
                "seedling_forest", "Forest Bag",
                new[] { new RecipeInput(seed, 5), new RecipeInput(soil, 3) },
                () =>
                {
                    var rng = new Random();
                    var bag = WildernessGenerator.Generate(template, rng);
                    return new[] { new ItemStack(forestBagType, 1, ContainedBag: bag) };
                },
                Duration: 8));
        }

        return recipes.ToImmutableArray();
    }

    /// <summary>
    /// Returns recipes applicable to a given facility type.
    /// </summary>
    public static IReadOnlyList<Recipe> GetRecipesForFacility(
        string facilityType, ImmutableArray<Recipe> allRecipes) =>
        facilityType switch
        {
            WorkbenchFacilityType => allRecipes.Where(r => r.Id.StartsWith("workbench_")).ToList(),
            TannerFacilityType => allRecipes.Where(r => r.Id.StartsWith("tanner_")).ToList(),
            SeedlingPotFacilityType => allRecipes.Where(r => r.Id.StartsWith("seedling_")).ToList(),
            _ => Array.Empty<Recipe>()
        };
}
