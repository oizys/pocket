using System.Collections.Immutable;
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
}
