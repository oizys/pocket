using System.Collections.Immutable;
using Terminal.Gui;
using Pockets.Core.Models;

namespace Pockets.App.Views;

/// <summary>
/// Left panel containing breadcrumbs, back button, grid view, hand cell, and item description.
/// </summary>
public class GridPanel : FrameView
{
    private readonly Label _breadcrumbs;
    private readonly BackButtonView _backButton;
    private readonly GridView _gridView;
    private readonly HandCellView _handCell;
    private readonly Label _handLabel;
    private readonly ItemDescriptionView _descriptionView;
    private readonly Label _statusBar;

    /// <summary>X offset where the grid starts (after back button + gap).</summary>
    private const int GridXOffset = Rendering.CellRenderer.CellWidth + 2;

    public GridPanel(GameState state)
    {
        Title = "Inventory";
        X = 0;
        Y = 0;
        Width = Dim.Percent(70);
        Height = Dim.Fill();

        _breadcrumbs = new Label("Root Bag")
        {
            X = GridXOffset,
            Y = 0,
            Width = Dim.Fill(),
            Height = 1
        };

        _backButton = new BackButtonView()
        {
            X = 0,
            Y = 1
        };
        _backButton.SetEnabled(state.IsNested);

        _gridView = new GridView(state)
        {
            X = GridXOffset,
            Y = 1
        };

        var gridWidth = Rendering.CellRenderer.CellWidth * state.ActiveBag.Grid.Columns;

        _handLabel = new Label("Hand")
        {
            X = GridXOffset + gridWidth + 2,
            Y = 0,
            Width = Rendering.CellRenderer.CellWidth,
            Height = 1
        };

        _handCell = new HandCellView(state)
        {
            X = GridXOffset + gridWidth + 2,
            Y = 1
        };

        var gridHeight = Rendering.CellRenderer.CellHeight * state.ActiveBag.Grid.Rows;
        _descriptionView = new ItemDescriptionView()
        {
            X = GridXOffset,
            Y = 1 + gridHeight
        };
        _descriptionView.UpdateState(state);

        // Hotkey hints now live in the global CommandStripView at GameView level.
        _statusBar = new Label("")
        {
            X = 0,
            Y = Pos.AnchorEnd(1),
            Width = Dim.Fill(),
            Height = 1
        };

        Add(_breadcrumbs, _backButton, _gridView, _handLabel, _handCell,
            _descriptionView, _statusBar);
    }

    public GridView GetGridView() => _gridView;
    public BackButtonView GetBackButton() => _backButton;

    /// <summary>
    /// Passes recipe data to the item description view for facility slot display.
    /// </summary>
    public void SetRecipes(ImmutableArray<Recipe> recipes) => _descriptionView.SetRecipes(recipes);

    /// <summary>
    /// Passes facility→recipe mapping for proper recipe lookup.
    /// </summary>
    public void SetFacilityRecipeMap(ImmutableDictionary<string, ImmutableArray<string>>? map) =>
        _descriptionView.SetFacilityRecipeMap(map);

    public void UpdateState(GameState state, LocationId focus = LocationId.B)
    {
        _gridView.UpdateState(state);
        _handCell.UpdateState(state);
        _backButton.SetEnabled(state.IsNested);
        _descriptionView.UpdateState(state, focus);

        // Reposition hand cell and description view in case grid dimensions changed
        var gridWidth = Rendering.CellRenderer.CellWidth * state.ActiveBag.Grid.Columns;
        _handLabel.X = GridXOffset + gridWidth + 2;
        _handCell.X = GridXOffset + gridWidth + 2;
        var gridHeight = Rendering.CellRenderer.CellHeight * state.ActiveBag.Grid.Rows;
        _descriptionView.Y = 1 + gridHeight;

        // Breadcrumb trail
        _breadcrumbs.Text = string.Join(" > ", state.BreadcrumbPath);

        // Status bar: hand contents (detailed text below the hand cell)
        _statusBar.Text = state.HasItemsInHand
            ? $"Hand: {string.Join(", ", state.HandItems.Select(i => $"{i.Count} {i.ItemType.Name}"))}"
            : "";
    }
}
