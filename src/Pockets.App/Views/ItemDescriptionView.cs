using System.Collections.Immutable;
using Terminal.Gui;
using Pockets.Core.Models;
using Pockets.Core.Data;

namespace Pockets.App.Views;

/// <summary>
/// Displays name, category, count, and description for the item at the cursor.
/// Also shows cell frame type, filter, and facility recipe info for slot cells.
/// </summary>
public class ItemDescriptionView : FrameView
{
    private readonly Label _content;
    private ImmutableArray<Recipe> _recipes;

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

    public void UpdateState(GameState state)
    {
        var cell = state.CurrentCell;
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

        // Frame info
        if (cell.Frame is InputSlotFrame input)
        {
            if (lines.Count > 0) lines.Add("");
            lines.Add("[Input Slot]");
            lines.Add(input.ItemTypeFilter is not null
                ? $"Accepts: {input.ItemTypeFilter.Name}"
                : input.Filter is not null
                    ? $"Accepts: {input.Filter}"
                    : "Accepts: any");
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

        // Recipe info when inside a facility bag
        if (cell.HasFrame && !_recipes.IsDefaultOrEmpty)
        {
            var facilityType = state.ActiveBag.EnvironmentType;
            var facilityRecipes = RecipeRegistry.GetRecipesForFacility(facilityType, _recipes);
            if (facilityRecipes.Count > 0)
            {
                lines.Add("");
                lines.Add("--- Recipes ---");
                foreach (var recipe in facilityRecipes)
                {
                    var inputs = string.Join(" + ",
                        recipe.Inputs.Select(i => $"{i.Count} {i.ItemType.Name}"));
                    lines.Add($"{recipe.Name}:");
                    lines.Add($"  {inputs} → {recipe.Name}");
                }
            }
        }

        if (lines.Count == 0)
            lines.Add("(empty)");

        _content.Text = string.Join("\n", lines);
        SetNeedsDisplay();
    }
}
