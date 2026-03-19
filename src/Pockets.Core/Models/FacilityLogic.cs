using System.Collections.Immutable;

namespace Pockets.Core.Models;

/// <summary>
/// Pure functions for facility crafting: recipe matching, input consumption,
/// output production, and tick advancement. Operates on individual facility bags.
/// </summary>
public static class FacilityLogic
{
    /// <summary>
    /// Checks if a facility bag's input slots contain enough items to match a recipe.
    /// Returns the matching recipe, or null if no match.
    /// </summary>
    public static Recipe? FindMatchingRecipe(Bag facility, IReadOnlyList<Recipe> recipes)
    {
        var inputStacks = GetInputStacks(facility);
        return recipes.FirstOrDefault(r => RecipeMatches(r, inputStacks));
    }

    /// <summary>
    /// Cycles the facility's active recipe to the next one in the list.
    /// Dumps all items from input and output slots, rebuilds the grid for the new recipe,
    /// and resets crafting progress. Returns the updated facility and dumped items.
    /// </summary>
    public static (Bag Updated, ImmutableList<ItemStack> Dumped) CycleRecipe(
        Bag facility, IReadOnlyList<Recipe> recipes)
    {
        var state = facility.FacilityState!;
        var currentId = state.ActiveRecipeId;

        // Find next recipe (wrap around)
        var currentIndex = -1;
        for (int i = 0; i < recipes.Count; i++)
        {
            if (recipes[i].Id == currentId)
            {
                currentIndex = i;
                break;
            }
        }
        var nextIndex = (currentIndex + 1) % recipes.Count;
        var nextRecipe = recipes[nextIndex];

        // Collect all items from slots to dump
        var dumped = ImmutableList.CreateBuilder<ItemStack>();
        for (int i = 0; i < facility.Grid.Cells.Length; i++)
        {
            var cell = facility.Grid.GetCell(i);
            if (!cell.IsEmpty)
                dumped.Add(cell.Stack!);
        }

        // Rebuild grid for new recipe
        var grid = Grid.Create(nextRecipe.GridColumns, nextRecipe.GridRows);
        var builder = grid.Cells.ToBuilder();
        for (int i = 0; i < nextRecipe.Inputs.Count; i++)
        {
            builder[i] = new Cell(Frame: new InputSlotFrame(
                $"in{i + 1}", ItemTypeFilter: nextRecipe.Inputs[i].ItemType));
        }
        for (int i = nextRecipe.Inputs.Count; i < builder.Count; i++)
        {
            builder[i] = new Cell(Frame: new OutputSlotFrame($"out{i - nextRecipe.Inputs.Count + 1}"));
        }
        grid = grid with { Cells = builder.MoveToImmutable() };

        var updated = facility with
        {
            Grid = grid,
            FacilityState = state with
            {
                ActiveRecipeId = nextRecipe.Id,
                RecipeId = null
            }
        };

        return (updated, dumped.ToImmutable());
    }

    /// <summary>
    /// Returns true if the input stacks satisfy all recipe input requirements.
    /// </summary>
    public static bool RecipeMatches(Recipe recipe, IReadOnlyList<ItemStack> inputStacks)
    {
        foreach (var input in recipe.Inputs)
        {
            var available = inputStacks
                .Where(s => s.ItemType == input.ItemType)
                .Sum(s => s.Count);
            if (available < input.Count)
                return false;
        }
        return true;
    }

    /// <summary>
    /// Advances a facility by one tick. Progress is passed in from the owning ItemStack's
    /// properties and returned as part of the result. If inputs match a recipe:
    /// - Sets RecipeId and increments Progress if not already crafting
    /// - Increments Progress if already crafting the same recipe
    /// - On completion: consumes inputs, places output in output slot, resets state
    /// Returns the updated facility bag and new progress value.
    /// </summary>
    public static (Bag Facility, int Progress) Tick(Bag facility, int currentProgress, IReadOnlyList<Recipe> recipes)
    {
        if (facility.FacilityState is null || !facility.FacilityState.IsActive)
            return (facility, currentProgress);

        var state = facility.FacilityState;
        var inputStacks = GetInputStacks(facility);

        // If currently crafting, check if recipe still valid
        if (state.RecipeId is not null)
        {
            var activeRecipe = recipes.FirstOrDefault(r => r.Id == state.RecipeId);
            if (activeRecipe is null || !RecipeMatches(activeRecipe, inputStacks))
            {
                // Recipe no longer valid (inputs removed?), reset
                return (facility with { FacilityState = state with { RecipeId = null } }, 0);
            }

            var newProgress = currentProgress + 1;
            if (newProgress >= activeRecipe.Duration)
            {
                // Complete: consume inputs, produce output
                return (CompleteCraft(facility, activeRecipe), 0);
            }

            return (facility, newProgress);
        }

        // Not crafting: try to start using ActiveRecipeId if set, otherwise scan
        Recipe? match;
        if (state.ActiveRecipeId is not null)
        {
            var activeRecipe = recipes.FirstOrDefault(r => r.Id == state.ActiveRecipeId);
            match = activeRecipe is not null && RecipeMatches(activeRecipe, inputStacks)
                ? activeRecipe : null;
        }
        else
        {
            match = FindMatchingRecipe(facility, recipes);
        }

        if (match is null)
            return (facility, currentProgress);

        // Start crafting (progress 1 since this tick counts)
        return (facility with
        {
            FacilityState = state with { RecipeId = match.Id }
        }, 1);
    }

    /// <summary>
    /// Completes a craft: consumes input items, places output in output slots, resets state.
    /// </summary>
    private static Bag CompleteCraft(Bag facility, Recipe recipe)
    {
        var grid = facility.Grid;

        // Consume inputs
        var remaining = recipe.Inputs.ToDictionary(i => i.ItemType, i => i.Count);
        for (int i = 0; i < grid.Cells.Length; i++)
        {
            var cell = grid.GetCell(i);
            if (cell.Frame is not InputSlotFrame || cell.IsEmpty)
                continue;

            var stack = cell.Stack!;
            if (!remaining.TryGetValue(stack.ItemType, out var needed) || needed <= 0)
                continue;

            var consume = Math.Min(stack.Count, needed);
            remaining[stack.ItemType] = needed - consume;

            grid = consume >= stack.Count
                ? grid.SetCell(i, cell with { Stack = null })
                : grid.SetCell(i, cell with { Stack = stack with { Count = stack.Count - consume } });
        }

        // Produce outputs into output slots
        var outputs = recipe.OutputFactory();
        var outputIndex = 0;
        for (int i = 0; i < grid.Cells.Length && outputIndex < outputs.Count; i++)
        {
            var cell = grid.GetCell(i);
            if (cell.Frame is not OutputSlotFrame || !cell.IsEmpty)
                continue;

            grid = grid.SetCell(i, cell with { Stack = outputs[outputIndex] });
            outputIndex++;
        }

        return facility with
        {
            Grid = grid,
            FacilityState = facility.FacilityState! with { RecipeId = null }
        };
    }

    /// <summary>
    /// Returns all item stacks in input slot cells of a facility bag.
    /// </summary>
    public static IReadOnlyList<ItemStack> GetInputStacks(Bag facility)
    {
        var stacks = new List<ItemStack>();
        for (int i = 0; i < facility.Grid.Cells.Length; i++)
        {
            var cell = facility.Grid.GetCell(i);
            if (cell.Frame is InputSlotFrame && !cell.IsEmpty)
                stacks.Add(cell.Stack!);
        }
        return stacks;
    }

    /// <summary>
    /// Returns true if all output slots in a facility are empty (ready for production).
    /// </summary>
    public static bool OutputSlotsEmpty(Bag facility)
    {
        for (int i = 0; i < facility.Grid.Cells.Length; i++)
        {
            var cell = facility.Grid.GetCell(i);
            if (cell.Frame is OutputSlotFrame && !cell.IsEmpty)
                return false;
        }
        return true;
    }
}
