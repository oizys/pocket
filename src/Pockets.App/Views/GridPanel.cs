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
    private readonly Label _toolbar;
    private readonly Label _statusBar;
    private readonly Label _inputStatus;

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

        var gridHeight = Rendering.CellRenderer.CellHeight * state.RootBag.Grid.Rows;
        _descriptionView = new ItemDescriptionView()
        {
            X = GridXOffset,
            Y = 1 + gridHeight
        };
        _descriptionView.UpdateState(state);

        _toolbar = new Label("[1/E/LClick:Action] [2/RClick:Half] [#:Modal] [4:Sort] [Q:Back] [^Z:Undo]")
        {
            X = 0,
            Y = Pos.AnchorEnd(3),
            Width = Dim.Fill(),
            Height = 1
        };

        _statusBar = new Label("")
        {
            X = 0,
            Y = Pos.AnchorEnd(2),
            Width = Dim.Fill(),
            Height = 1
        };

        _inputStatus = new Label("Input: -")
        {
            X = 0,
            Y = Pos.AnchorEnd(1),
            Width = Dim.Fill(),
            Height = 1
        };

        Add(_breadcrumbs, _backButton, _gridView, _handLabel, _handCell,
            _descriptionView, _toolbar, _statusBar, _inputStatus);
    }

    public GridView GetGridView() => _gridView;
    public BackButtonView GetBackButton() => _backButton;

    public void SetInputStatus(string status)
    {
        _inputStatus.Text = $"Input: {status}";
        _inputStatus.SetNeedsDisplay();
    }

    public void UpdateState(GameState state)
    {
        _gridView.UpdateState(state);
        _handCell.UpdateState(state);
        _backButton.SetEnabled(state.IsNested);
        _descriptionView.UpdateState(state);

        // Reposition hand cell in case grid dimensions changed
        var gridWidth = Rendering.CellRenderer.CellWidth * state.ActiveBag.Grid.Columns;
        _handLabel.X = GridXOffset + gridWidth + 2;
        _handCell.X = GridXOffset + gridWidth + 2;

        // Breadcrumb trail
        _breadcrumbs.Text = string.Join(" > ", state.BreadcrumbPath);

        // Status bar: hand contents (detailed text below the hand cell)
        _statusBar.Text = state.HasItemsInHand
            ? $"Hand: {string.Join(", ", state.HandItems.Select(i => $"{i.Count} {i.ItemType.Name}"))}"
            : "";
    }
}
