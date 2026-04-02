using System.Collections.Immutable;
using Terminal.Gui;
using Pockets.Core.Models;
using Pockets.Core.Data;

namespace Pockets.App.Views;

/// <summary>
/// Displays name, category, count, and description for the item at the cursor.
/// Shows cell frame info (slot type, filter, expected quantity).
/// Shows active recipe and recipe list when inside a facility bag.
/// </summary>
public class ItemDescriptionView : FrameView
{
    private readonly Label _content;
    private ImmutableArray<Recipe> _recipes;
    private ImmutableDictionary<string, ImmutableArray<string>>? _facilityRecipeMap;

    public ItemDescriptionView()
    {
        Title = "Item";
        X = 0;
        Width = Dim.Fill();
        Height = Dim.Fill();

        _content = new Label("")
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill()
        };
        Add(_content);
    }

    /// <summary>
    /// Sets the recipe list so facility slot descriptions can show recipe info.
    /// </summary>
    public void SetRecipes(ImmutableArray<Recipe> recipes) => _recipes = recipes;

    /// <summary>
    /// Sets the facility→recipe mapping for proper recipe lookup.
    /// </summary>
    public void SetFacilityRecipeMap(ImmutableDictionary<string, ImmutableArray<string>>? map) =>
        _facilityRecipeMap = map;

    public void UpdateState(GameState state)
    {
        var cell = state.CurrentCell;
        var activeBag = state.ActiveBag;
        var lines = new List<string>();

        // Item stack info
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

        // Frame info with expected quantity from active recipe
        if (cell.Frame is InputSlotFrame input)
        {
            if (lines.Count > 0) lines.Add("");
            lines.Add("[Input Slot]");
            var acceptsLine = input.ItemTypeFilter is not null
                ? $"Accepts: {input.ItemTypeFilter.Name}"
                : input.Filter is not null
                    ? $"Accepts: {input.Filter}"
                    : "Accepts: any";

            // Show expected quantity from the active recipe
            var activeRecipe = GetActiveRecipe(activeBag);
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

        // Category filter (non-frame cells)
        if (cell.CategoryFilter is not null && cell.Frame is null)
        {
            if (lines.Count > 0) lines.Add("");
            lines.Add($"Filter: {cell.CategoryFilter}");
        }

        // Facility info: show active recipe + recipe list when inside any facility
        if (activeBag.FacilityState is not null && !_recipes.IsDefaultOrEmpty)
        {
            var facilityRecipes = GetRecipesForFacility(activeBag.EnvironmentType);
            if (facilityRecipes.Count > 0)
            {
                var facilityState = activeBag.FacilityState;
                lines.Add("");
                lines.Add($"--- {activeBag.EnvironmentType} ---");

                // Show crafting progress if actively crafting
                if (facilityState.RecipeId is not null)
                {
                    var craftingRecipe = facilityRecipes.FirstOrDefault(r => r.Id == facilityState.RecipeId);
                    if (craftingRecipe is not null)
                    {
                        // Read progress from the owning ItemStack via breadcrumbs
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

        _content.Text = string.Join("\n", lines);
        SetNeedsDisplay();
    }

    /// <summary>
    /// Reads crafting progress from the owning ItemStack via BagStore.
    /// </summary>
    private static int GetFacilityProgress(GameState state, Bag facilityBag)
    {
        var ownerInfo = state.Store.GetOwnerOf(facilityBag.Id);
        if (ownerInfo is null) return 0;
        var parentBag = state.Store.GetById(ownerInfo.ParentBagId);
        var ownerStack = parentBag?.Grid.GetCell(ownerInfo.CellIndex).Stack;
        return ownerStack?.GetInt("Progress") ?? 0;
    }

    /// <summary>
    /// Returns the active recipe for the given facility bag, or null.
    /// </summary>
    private Recipe? GetActiveRecipe(Bag facility)
    {
        if (facility.FacilityState?.ActiveRecipeId is null || _recipes.IsDefaultOrEmpty)
            return null;

        return _recipes.FirstOrDefault(r => r.Id == facility.FacilityState.ActiveRecipeId);
    }

    /// <summary>
    /// Returns recipes for a facility type using FacilityRecipeMap if available,
    /// falling back to RecipeRegistry.
    /// </summary>
    private IReadOnlyList<Recipe> GetRecipesForFacility(string environmentType)
    {
        if (_facilityRecipeMap is not null &&
            _facilityRecipeMap.TryGetValue(environmentType, out var recipeIds))
        {
            var recipesById = _recipes.ToDictionary(r => r.Id);
            return recipeIds
                .Where(id => recipesById.ContainsKey(id))
                .Select(id => recipesById[id])
                .ToList();
        }

        return RecipeRegistry.GetRecipesForFacility(environmentType, _recipes);
    }
}
