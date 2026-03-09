using System.Collections.Immutable;
using Pockets.Core.Models;

namespace Pockets.Core.Data;

/// <summary>
/// Template defining a wilderness bag's properties: grid size, fill ratio, and weighted loot table.
/// </summary>
public record WildernessTemplate(
    string Name,
    string EnvironmentType,
    string ColorScheme,
    int Columns,
    int Rows,
    double FillRatio,
    ImmutableArray<(ItemType ItemType, double Weight)> LootTable);

/// <summary>
/// Generates wilderness bags filled with random resources according to a template.
/// Each filled cell gets a single harvestable item (count 1) chosen by weighted random selection.
/// </summary>
public static class WildernessGenerator
{
    /// <summary>
    /// Creates a wilderness Bag from the given template, filling cells randomly
    /// according to the fill ratio and weighted loot table.
    /// </summary>
    public static Bag Generate(WildernessTemplate template, Random rng)
    {
        var grid = Grid.Create(template.Columns, template.Rows);
        var totalCells = template.Columns * template.Rows;
        var totalWeight = template.LootTable.Sum(e => e.Weight);

        // Determine which cells to fill
        var cellIndices = Enumerable.Range(0, totalCells)
            .Where(_ => rng.NextDouble() < template.FillRatio)
            .ToList();

        // Fill selected cells with weighted random items
        var stacks = cellIndices
            .Select(index => (Index: index, ItemType: PickWeighted(template.LootTable, totalWeight, rng)))
            .ToList();

        var builder = grid.Cells.ToBuilder();
        foreach (var (index, itemType) in stacks)
        {
            builder[index] = new Cell(new ItemStack(itemType, 1));
        }

        var filledGrid = grid with { Cells = builder.MoveToImmutable() };
        return new Bag(filledGrid, template.EnvironmentType, template.ColorScheme);
    }

    /// <summary>
    /// Picks an item type from the loot table using weighted random selection.
    /// </summary>
    private static ItemType PickWeighted(
        ImmutableArray<(ItemType ItemType, double Weight)> lootTable,
        double totalWeight,
        Random rng)
    {
        var roll = rng.NextDouble() * totalWeight;
        var cumulative = 0.0;
        foreach (var (itemType, weight) in lootTable)
        {
            cumulative += weight;
            if (roll < cumulative)
                return itemType;
        }
        return lootTable[^1].ItemType;
    }
}
