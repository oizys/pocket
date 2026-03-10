using Terminal.Gui;
using Pockets.Core.Models;

namespace Pockets.App.Views;

/// <summary>
/// Left panel containing breadcrumbs, the grid view, and item description.
/// </summary>
public class GridPanel : FrameView
{
    private readonly Label _breadcrumbs;
    private readonly GridView _gridView;
    private readonly ItemDescriptionView _descriptionView;
    private readonly Label _toolbar;
    private readonly Label _statusBar;

    public GridPanel(GameState state)
    {
        Title = "Inventory";
        X = 0;
        Y = 0;
        Width = Dim.Percent(70);
        Height = Dim.Fill();

        _breadcrumbs = new Label("Root Bag")
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = 1
        };

        _gridView = new GridView(state)
        {
            X = 0,
            Y = 1
        };

        var gridHeight = Rendering.CellRenderer.CellHeight * state.RootBag.Grid.Rows;
        _descriptionView = new ItemDescriptionView()
        {
            X = 0,
            Y = 1 + gridHeight
        };
        _descriptionView.UpdateState(state);

        _toolbar = new Label("[1/E/LClick:Action] [2/RClick:Half] [#:Modal] [4:Sort] [Q:Back] [^Z:Undo]")
        {
            X = 0,
            Y = Pos.AnchorEnd(2),
            Width = Dim.Fill(),
            Height = 1
        };

        _statusBar = new Label("")
        {
            X = 0,
            Y = Pos.AnchorEnd(1),
            Width = Dim.Fill(),
            Height = 1
        };

        Add(_breadcrumbs, _gridView, _descriptionView, _toolbar, _statusBar);
    }

    public GridView GetGridView() => _gridView;

    public void UpdateState(GameState state)
    {
        _gridView.UpdateState(state);
        _descriptionView.UpdateState(state);

        // Breadcrumb trail
        _breadcrumbs.Text = string.Join(" > ", state.BreadcrumbPath);

        // Status bar: hand contents
        _statusBar.Text = state.HasItemsInHand
            ? $"Hand: {string.Join(", ", state.HandItems.Select(i => $"{i.Count} {i.ItemType.Name}"))}"
            : "";
    }
}
