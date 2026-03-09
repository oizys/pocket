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
}
