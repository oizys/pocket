using System.Collections.Immutable;
using Terminal.Gui;
using Pockets.Core.Models;
using Pockets.Core.Rendering;

namespace Pockets.App.Views;

/// <summary>
/// Displays name, category, count, description, frame info, and recipes for the
/// cursor cell of the focused panel. Pure renderer — all description logic lives
/// in Pockets.Core.Rendering.ItemDescriptionRenderer.
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

    public void SetRecipes(ImmutableArray<Recipe> recipes) => _recipes = recipes;

    public void SetFacilityRecipeMap(ImmutableDictionary<string, ImmutableArray<string>>? map) =>
        _facilityRecipeMap = map;

    public void UpdateState(GameState state) => UpdateState(state, LocationId.B);

    public void UpdateState(GameState state, LocationId focus)
    {
        var lines = ItemDescriptionRenderer.RenderLines(state, focus, _recipes, _facilityRecipeMap);
        _content.Text = string.Join("\n", lines);
        SetNeedsDisplay();
    }
}
