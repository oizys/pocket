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

        _toolbar = new Label("[1:Grab] [2:Drop] [3:Split] [4:Sort] [5:Random]")
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

    public void UpdateState(GameState state)
    {
        _gridView.UpdateState(state);
        _descriptionView.UpdateState(state);
        _statusBar.Text = state.HasItemsInHand
            ? $"Hand: {state.ActiveHand.Count} item(s)"
            : "";
    }
}
