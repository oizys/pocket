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
    /// and starter crafting materials.
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

        // Facility bags
        stacks.Add(new ItemStack(workbenchType, 1, ContainedBag: FacilityBuilder.CreateWorkbench()));
        stacks.Add(new ItemStack(tannerType, 1, ContainedBag: FacilityBuilder.CreateTanner()));
        stacks.Add(new ItemStack(seedlingPotType, 1, ContainedBag: FacilityBuilder.CreateSeedlingPot()));

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
}
