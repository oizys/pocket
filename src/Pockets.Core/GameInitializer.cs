using System.Collections.Immutable;
using Pockets.Core.Data;
using Pockets.Core.Models;

namespace Pockets.Core;

/// <summary>
/// Creates initial game states with random item placement.
/// </summary>
public static class GameInitializer
{
    /// <summary>
    /// Creates a Stage 1 game with 4-10 random item stacks from the given item types.
    /// </summary>
    public static GameState CreateRandomStage1Game(ImmutableArray<ItemType> itemTypes, Random? random = null)
    {
        random ??= new Random();

        var stackCount = random.Next(4, 11);
        var stacks = Enumerable.Range(0, stackCount)
            .Select(_ =>
            {
                var itemType = itemTypes[random.Next(itemTypes.Length)];
                var count = itemType.IsStackable
                    ? random.Next(1, itemType.EffectiveMaxStackSize + 1)
                    : 1;
                return new ItemStack(itemType, count);
            })
            .ToList();

        return GameState.CreateStage1(itemTypes, stacks);
    }

    /// <summary>
    /// Creates a Stage 2 game: Stage 1 base plus a forest wilderness bag in the grid.
    /// </summary>
    public static GameState CreateRandomStage2Game(ImmutableArray<ItemType> itemTypes, Random? random = null)
    {
        random ??= new Random();

        var stackCount = random.Next(4, 11);
        var stacks = Enumerable.Range(0, stackCount)
            .Select(_ =>
            {
                var itemType = itemTypes[random.Next(itemTypes.Length)];
                var count = itemType.IsStackable
                    ? random.Next(1, itemType.EffectiveMaxStackSize + 1)
                    : 1;
                return new ItemStack(itemType, count);
            })
            .ToList();

        // Create forest wilderness bag from material-category items
        var materials = itemTypes.Where(t => t.Category == Category.Material).ToImmutableArray();
        if (materials.Length > 0)
        {
            var lootTable = materials.Select(m => (m, 1.0)).ToImmutableArray();
            var template = new WildernessTemplate("Forest", "Forest", "Green", 6, 4, 0.6, lootTable);
            var wildernessBag = WildernessGenerator.Generate(template, random);

            var forestBagType = new ItemType("Forest Bag", Category.Bag, IsStackable: false);
            itemTypes = itemTypes.Add(forestBagType);
            var forestStack = new ItemStack(forestBagType, 1, ContainedBag: wildernessBag);
            stacks.Add(forestStack);
        }

        return GameState.CreateStage1(itemTypes, stacks);
    }

    /// <summary>
    /// Creates a Stage 3 game: Stage 2 base plus 3 facility bags (Workbench, Tanner, Seedling Pot)
    /// and starter crafting materials. Legacy method — uses hardcoded RecipeRegistry/FacilityBuilder.
    /// </summary>
    public static GameState CreateRandomStage3Game(ImmutableArray<ItemType> itemTypes, Random? random = null)
    {
        random ??= new Random();
        var byName = itemTypes.ToDictionary(t => t.Name);

        // Ensure facility bag types exist
        var workbenchType = new ItemType("Workbench", Category.Structure, IsStackable: false);
        var tannerType = new ItemType("Tanner", Category.Structure, IsStackable: false);
        var seedlingPotType = new ItemType("Seedling Pot", Category.Structure, IsStackable: false);
        var forestBagType = new ItemType("Forest Bag", Category.Bag, IsStackable: false);
        var beltPouchType = new ItemType("Belt Pouch", Category.Bag, IsStackable: false);

        itemTypes = itemTypes
            .Add(workbenchType)
            .Add(tannerType)
            .Add(seedlingPotType)
            .Add(forestBagType)
            .Add(beltPouchType);

        var stacks = new List<ItemStack>();

        // Build recipes so facility input slots can be filtered to specific item types
        var recipes = RecipeRegistry.BuildRecipes(itemTypes);
        var workbenchRecipe = recipes.FirstOrDefault(r => r.Id.StartsWith("workbench_"));
        var tannerRecipe = recipes.FirstOrDefault(r => r.Id.StartsWith("tanner_"));
        var seedlingRecipe = recipes.FirstOrDefault(r => r.Id.StartsWith("seedling_"));

        // Facility bags with recipe-filtered input slots
        stacks.Add(new ItemStack(workbenchType, 1, ContainedBag: FacilityBuilder.CreateWorkbench(workbenchRecipe)));
        stacks.Add(new ItemStack(tannerType, 1, ContainedBag: FacilityBuilder.CreateTanner(tannerRecipe)));
        stacks.Add(new ItemStack(seedlingPotType, 1, ContainedBag: FacilityBuilder.CreateSeedlingPot(seedlingRecipe)));

        // Forest wilderness bag
        var materials = itemTypes.Where(t => t.Category == Category.Material).ToImmutableArray();
        if (materials.Length > 0)
        {
            var lootTable = materials.Select(m => (m, 1.0)).ToImmutableArray();
            var template = new WildernessTemplate("Forest", "Forest", "Green", 6, 4, 0.6, lootTable);
            var wildernessBag = WildernessGenerator.Generate(template, random);
            stacks.Add(new ItemStack(forestBagType, 1, ContainedBag: wildernessBag));
        }

        // Starter crafting materials (enough for 1-2 recipes)
        if (byName.TryGetValue("Plain Rock", out var rock))
            stacks.Add(new ItemStack(rock, 8));
        if (byName.TryGetValue("Rough Wood", out var wood))
            stacks.Add(new ItemStack(wood, 5));
        if (byName.TryGetValue("Tanned Leather", out var leather))
            stacks.Add(new ItemStack(leather, 4));
        if (byName.TryGetValue("Woven Fiber", out var fiber))
            stacks.Add(new ItemStack(fiber, 3));
        if (byName.TryGetValue("Forest Seed", out var seed))
            stacks.Add(new ItemStack(seed, 6));
        if (byName.TryGetValue("Rich Soil", out var soil))
            stacks.Add(new ItemStack(soil, 4));

        return GameState.CreateStage1(itemTypes, stacks);
    }

    /// <summary>
    /// Creates a game from a ContentRegistry. Builds facility bags from facility/recipe definitions,
    /// generates wilderness bags from templates, and places starter materials.
    /// </summary>
    public static (GameState State, ImmutableArray<Recipe> Recipes) CreateFromRegistry(
        ContentRegistry registry, Random? random = null)
    {
        random ??= new Random();
        var itemTypes = registry.Items.Values.ToImmutableArray();
        var byName = registry.Items;
        var stacks = new List<ItemStack>();

        // Build facility bags from data-driven definitions.
        // Only Workshop is placed directly — other facilities are crafted from Workshop.
        var workshopFacilities = new HashSet<string> { "Workshop" };
        var workshopCraftable = registry.Facilities
            .Where(kv => workshopFacilities.Contains(kv.Key))
            .SelectMany(kv => registry.Recipes
                .Where(r => kv.Value.RecipeIds.Contains(r.Key))
                .Select(r => r.Value.Name))
            .ToHashSet();

        foreach (var (facilityId, facility) in registry.Facilities)
        {
            if (!byName.TryGetValue(facilityId, out var facilityItemType))
                continue;

            // Skip facilities that are crafted from Workshop (player builds them)
            if (workshopCraftable.Contains(facilityId))
                continue;

            // Get the first recipe for this facility to set initial slot filters
            var firstRecipeId = facility.RecipeIds.FirstOrDefault();
            Recipe? firstRecipe = firstRecipeId is not null && registry.Recipes.TryGetValue(firstRecipeId, out var r) ? r : null;

            var facilityBag = BuildFacilityBag(facility, firstRecipe);
            stacks.Add(new ItemStack(facilityItemType, 1, ContainedBag: facilityBag));
        }

        // Build wilderness bags from grid + loot table templates
        foreach (var (templateId, gridTemplate) in registry.GridTemplates)
        {
            // Find an item type matching the template's environment + " Bag"
            var bagName = $"{gridTemplate.EnvironmentType} Bag";
            if (!byName.TryGetValue(bagName, out var bagItemType))
                continue;

            // Find a matching loot table template (convention: same prefix)
            var lootTemplate = registry.LootTableTemplates.Values
                .FirstOrDefault();

            if (lootTemplate is null)
                continue;

            var generators = GeneratorBuiltins.GetAll(byName);
            var wildernessBag = GeneratorBuiltins.Wilderness(null,
                new object[] { gridTemplate, lootTemplate, byName });

            if (wildernessBag is BagValue bv)
                stacks.Add(new ItemStack(bagItemType, 1, ContainedBag: bv.Bag));
        }

        // Starter crafting materials (enough to craft 1-2 facilities from Workshop)
        var starterMaterials = new[]
        {
            ("Plain Rock", 10), ("Rough Wood", 6), ("Tanned Leather", 6),
            ("Woven Fiber", 3), ("Forest Seed", 6), ("Rich Soil", 4)
        };
        foreach (var (name, count) in starterMaterials)
        {
            if (byName.TryGetValue(name, out var itemType))
                stacks.Add(new ItemStack(itemType, count));
        }

        var allRecipes = registry.Recipes.Values.ToImmutableArray();
        var state = GameState.CreateStage1(itemTypes, stacks);

        // Set up planter frames in bottom-right 4 cells (indices 28-31 of 8×4 grid)
        // and pre-plant Green Bean Plants in cells 28-29
        var rootGrid = state.RootBag.Grid;
        for (int i = 28; i <= 31; i++)
        {
            var cell = rootGrid.GetCell(i);
            rootGrid = rootGrid.SetCell(i, cell with { Frame = new PlanterFrame() });
        }

        if (byName.TryGetValue("Green Bean Plant", out var beanPlantType))
        {
            for (int i = 28; i <= 29; i++)
            {
                var plant = new ItemStack(beanPlantType, 1)
                    .WithProperty("Progress", new IntValue(0))
                    .WithProperty("Duration", new IntValue(6))
                    .WithProperty("Yield", new IntValue(3))
                    .WithProperty("Produce", new StringValue("Green Bean"));
                var cell = rootGrid.GetCell(i);
                rootGrid = rootGrid.SetCell(i, cell with { Stack = plant });
            }
        }

        state = state with { RootBag = state.RootBag with { Grid = rootGrid } };

        return (state, allRecipes);
    }

    /// <summary>
    /// Builds a facility bag from a FacilityDefinition and optional active recipe.
    /// Grid layout comes from the recipe; if no recipe, creates a default 3x1 grid.
    /// </summary>
    internal static Bag BuildFacilityBag(FacilityDefinition facility, Recipe? activeRecipe)
    {
        Grid grid;
        if (activeRecipe is not null)
        {
            grid = Grid.Create(activeRecipe.GridColumns, activeRecipe.GridRows);
            var builder = grid.Cells.ToBuilder();
            for (int i = 0; i < activeRecipe.Inputs.Count; i++)
            {
                builder[i] = new Cell(Frame: new InputSlotFrame(
                    $"in{i + 1}",
                    ItemTypeFilter: activeRecipe.Inputs[i].ItemType));
            }
            // Output slots fill remaining cells
            for (int i = activeRecipe.Inputs.Count; i < builder.Count; i++)
            {
                builder[i] = new Cell(Frame: new OutputSlotFrame($"out{i - activeRecipe.Inputs.Count + 1}"));
            }
            grid = grid with { Cells = builder.MoveToImmutable() };
        }
        else
        {
            grid = Grid.Create(3, 1);
        }

        return new Bag(grid, facility.EnvironmentType, facility.ColorScheme,
            FacilityState: new FacilityState(ActiveRecipeId: activeRecipe?.Id));
    }
}
