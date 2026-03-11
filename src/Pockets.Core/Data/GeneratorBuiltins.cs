using System.Collections.Immutable;
using Pockets.Core.Models;

namespace Pockets.Core.Data;

/// <summary>
/// Provides built-in pipeline generator functions that can be registered by name
/// and invoked during recipe output pipeline execution.
/// </summary>
public static class GeneratorBuiltins
{
    /// <summary>
    /// Creates an empty bag from a GridTemplate.
    /// The template is taken from the input if it is a TemplateValue, otherwise from templateArgs[0].
    /// Returns a BagValue wrapping the new Bag.
    /// </summary>
    public static PipelineValue Bag(PipelineValue? input, IReadOnlyList<object> templateArgs)
    {
        var template = input is TemplateValue tv
            ? (GridTemplate)tv.Template
            : (GridTemplate)templateArgs[0];

        var grid = Grid.Create(template.Columns, template.Rows);
        var bag = new Bag(grid, template.EnvironmentType, template.ColorScheme);
        return new BagValue(bag);
    }

    /// <summary>
    /// Creates a wilderness bag populated with random loot according to a LootTableTemplate.
    /// templateArgs[0] = GridTemplate, templateArgs[1] = LootTableTemplate,
    /// templateArgs[2] = ImmutableDictionary&lt;string, ItemType&gt; (items by name).
    /// Fills each cell independently using FillRatio as the per-cell probability,
    /// choosing items by weighted random selection from the loot table entries.
    /// Returns a BagValue.
    /// </summary>
    public static PipelineValue Wilderness(PipelineValue? input, IReadOnlyList<object> templateArgs)
    {
        var gridTemplate = (GridTemplate)templateArgs[0];
        var lootTable = (LootTableTemplate)templateArgs[1];
        var items = (ImmutableDictionary<string, ItemType>)templateArgs[2];

        var rng = new Random();
        var totalWeight = lootTable.Entries.Sum(e => e.Weight);
        var cellCount = gridTemplate.Columns * gridTemplate.Rows;

        var grid = Grid.Create(gridTemplate.Columns, gridTemplate.Rows);

        grid = Enumerable.Range(0, cellCount)
            .Aggregate(grid, (g, i) =>
            {
                if (rng.NextDouble() >= lootTable.FillRatio)
                    return g;

                var roll = rng.NextDouble() * totalWeight;
                var cumulative = 0.0;
                var entry = lootTable.Entries.First(e =>
                {
                    cumulative += e.Weight;
                    return roll < cumulative;
                });

                if (!items.TryGetValue(entry.ItemName, out var itemType))
                    return g;

                return g.SetCell(i, new Cell(new ItemStack(itemType, 1)));
            });

        var bag = new Bag(grid, gridTemplate.EnvironmentType, gridTemplate.ColorScheme);
        return new BagValue(bag);
    }

    /// <summary>
    /// Creates a new bag from templateArgs[0] (GridTemplate) and attaches it as ContainedBag
    /// on each ItemStack in the input StacksValue. Returns a StacksValue with updated stacks.
    /// </summary>
    public static PipelineValue AttachBag(PipelineValue? input, IReadOnlyList<object> templateArgs)
    {
        var stacks = ((StacksValue)input!).Stacks;
        var template = (GridTemplate)templateArgs[0];

        var grid = Grid.Create(template.Columns, template.Rows);
        var bag = new Bag(grid, template.EnvironmentType, template.ColorScheme);

        var updatedStacks = stacks
            .Select(s => s with { ContainedBag = bag })
            .ToList();

        return new StacksValue(updatedStacks);
    }

    /// <summary>
    /// Randomizes the positions of all non-empty cells in the bag's grid, preserving all stacks.
    /// Returns a BagValue with the shuffled grid.
    /// </summary>
    public static PipelineValue Shuffle(PipelineValue? input, IReadOnlyList<object> templateArgs)
    {
        var bag = ((BagValue)input!).Bag;
        var cellCount = bag.Grid.Columns * bag.Grid.Rows;

        var nonEmptyStacks = Enumerable.Range(0, cellCount)
            .Select(i => bag.Grid.GetCell(i))
            .Where(c => !c.IsEmpty)
            .Select(c => c.Stack!)
            .ToList();

        var rng = new Random();
        var positions = Enumerable.Range(0, cellCount)
            .OrderBy(_ => rng.Next())
            .Take(nonEmptyStacks.Count)
            .ToList();

        var emptyGrid = Grid.Create(bag.Grid.Columns, bag.Grid.Rows);

        var shuffledGrid = nonEmptyStacks
            .Zip(positions, (stack, pos) => (stack, pos))
            .Aggregate(emptyGrid, (g, pair) =>
                g.SetCell(pair.pos, new Cell(pair.stack)));

        return new BagValue(bag with { Grid = shuffledGrid });
    }

    /// <summary>
    /// Returns an ImmutableDictionary of all built-in generators keyed by name.
    /// The "wilderness" generator is wrapped to automatically append the items dictionary
    /// as the third template argument, so callers only need to supply grid and loot templates.
    /// </summary>
    public static ImmutableDictionary<string, GeneratorFunc> GetAll(ImmutableDictionary<string, ItemType> items) =>
        ImmutableDictionary<string, GeneratorFunc>.Empty
            .Add("bag", Bag)
            .Add("wilderness", (input, args) => Wilderness(input, args.Append(items).ToList()))
            .Add("attach-bag", AttachBag)
            .Add("shuffle", Shuffle);
}
