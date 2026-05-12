using System.Collections.Immutable;
using Pockets.Core.Data;
using Pockets.Core.Models;

namespace Pockets.Core.Rendering;

/// <summary>
/// Produces the line-by-line text shown in the item description pane for the
/// focused panel's cursor cell. Pure function of GameState — no View references.
/// </summary>
public static class ItemDescriptionRenderer
{
    /// <summary>
    /// Builds the description lines for the cursor cell of the focused location.
    /// Falls back to the global ActiveBag/CurrentCell when the location is absent.
    /// </summary>
    public static IReadOnlyList<string> RenderLines(
        GameState state,
        LocationId focus,
        ImmutableArray<Recipe> recipes,
        ImmutableDictionary<string, ImmutableArray<string>>? facilityRecipeMap)
    {
        var (cell, activeBag) = ResolveFocusCell(state, focus);
        var lines = new List<string>();

        if (!cell.IsEmpty)
        {
            var stack = cell.Stack!;
            lines.Add(stack.ItemType.Name);
            lines.Add($"Category: {stack.ItemType.Category}");
            lines.Add(stack.ItemType.IsStackable
                ? $"Count: {stack.Count} / {stack.ItemType.EffectiveMaxStackSize}"
                : "Unique item");
            if (!string.IsNullOrEmpty(stack.ItemType.Description))
                lines.Add($"\n{stack.ItemType.Description}");
        }

        if (cell.Frame is InputSlotFrame input)
        {
            if (lines.Count > 0) lines.Add("");
            lines.Add("[Input Slot]");
            var acceptsLine = input.ItemTypeFilter is not null
                ? $"Accepts: {input.ItemTypeFilter.Name}"
                : input.Filter is not null
                    ? $"Accepts: {input.Filter}"
                    : "Accepts: any";

            var activeRecipe = GetActiveRecipe(activeBag, recipes);
            if (activeRecipe is not null && input.ItemTypeFilter is not null)
            {
                var recipeInput = activeRecipe.Inputs
                    .FirstOrDefault(i => i.ItemType == input.ItemTypeFilter);
                if (recipeInput is not null)
                    acceptsLine += $" (need {recipeInput.Count})";
            }
            lines.Add(acceptsLine);
        }
        else if (cell.Frame is OutputSlotFrame)
        {
            if (lines.Count > 0) lines.Add("");
            lines.Add("[Output Slot]");
            lines.Add("Collect crafted items here");
        }

        if (cell.CategoryFilter is not null && cell.Frame is null)
        {
            if (lines.Count > 0) lines.Add("");
            lines.Add($"Filter: {cell.CategoryFilter}");
        }

        if (activeBag.FacilityState is not null && !recipes.IsDefaultOrEmpty)
        {
            var facilityRecipes = GetRecipesForFacility(
                activeBag.EnvironmentType, recipes, facilityRecipeMap);
            if (facilityRecipes.Count > 0)
            {
                var facilityState = activeBag.FacilityState;
                lines.Add("");
                lines.Add($"--- {activeBag.EnvironmentType} ---");

                if (facilityState.RecipeId is not null)
                {
                    var craftingRecipe = facilityRecipes
                        .FirstOrDefault(r => r.Id == facilityState.RecipeId);
                    if (craftingRecipe is not null)
                    {
                        var progress = GetFacilityProgress(state, activeBag);
                        lines.Add($"Crafting: {craftingRecipe.Name} [{progress}/{craftingRecipe.Duration}]");
                    }
                }

                lines.Add("");
                lines.Add("Recipes (R to cycle):");
                foreach (var recipe in facilityRecipes)
                {
                    var marker = recipe.Id == facilityState.ActiveRecipeId ? "> " : "  ";
                    var inputs = string.Join(" + ",
                        recipe.Inputs.Select(i => $"{i.Count} {i.ItemType.Name}"));
                    lines.Add($"{marker}{recipe.Name}:");
                    lines.Add($"    {inputs} -> {recipe.Name}");
                }
            }
        }

        if (lines.Count == 0)
            lines.Add("(empty)");

        return lines;
    }

    /// <summary>
    /// Resolves (cell, activeBag) for the given focus location by walking the
    /// location's breadcrumb trail. Falls back to global ActiveBag when missing.
    /// </summary>
    private static (Cell Cell, Bag ActiveBag) ResolveFocusCell(GameState state, LocationId focus)
    {
        var loc = state.Locations.TryGet(focus);
        if (loc is null)
            return (state.CurrentCell, state.ActiveBag);

        var bagId = loc.BagId;
        foreach (var entry in loc.Breadcrumbs.Reverse())
        {
            var b = state.Store.GetById(bagId);
            if (b is null) break;
            var c = b.Grid.GetCell(entry.CellIndex);
            if (c.Stack?.ContainedBagId is not { } childId) break;
            bagId = childId;
        }
        var activeBag = state.Store.GetById(bagId) ?? state.ActiveBag;
        var cell = activeBag.Grid.GetCell(loc.Cursor.Position);
        return (cell, activeBag);
    }

    private static int GetFacilityProgress(GameState state, Bag facilityBag)
    {
        var ownerInfo = state.Store.GetOwnerOf(facilityBag.Id);
        if (ownerInfo is null) return 0;
        var parentBag = state.Store.GetById(ownerInfo.ParentBagId);
        var ownerStack = parentBag?.Grid.GetCell(ownerInfo.CellIndex).Stack;
        return ownerStack?.GetInt("Progress") ?? 0;
    }

    private static Recipe? GetActiveRecipe(Bag facility, ImmutableArray<Recipe> recipes)
    {
        if (facility.FacilityState?.ActiveRecipeId is null || recipes.IsDefaultOrEmpty)
            return null;

        return recipes.FirstOrDefault(r => r.Id == facility.FacilityState.ActiveRecipeId);
    }

    private static IReadOnlyList<Recipe> GetRecipesForFacility(
        string environmentType,
        ImmutableArray<Recipe> recipes,
        ImmutableDictionary<string, ImmutableArray<string>>? facilityRecipeMap)
    {
        if (facilityRecipeMap is not null &&
            facilityRecipeMap.TryGetValue(environmentType, out var recipeIds))
        {
            var recipesById = recipes.ToDictionary(r => r.Id);
            return recipeIds
                .Where(id => recipesById.ContainsKey(id))
                .Select(id => recipesById[id])
                .ToList();
        }

        return RecipeRegistry.GetRecipesForFacility(environmentType, recipes);
    }
}
