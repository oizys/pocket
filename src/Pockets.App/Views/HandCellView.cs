using Terminal.Gui;
using Pockets.Core.Models;
using Pockets.App.Rendering;

namespace Pockets.App.Views;

/// <summary>
/// Displays the current hand contents as a single 3×2 glyph cell next to the grid.
/// </summary>
public class HandCellView : View
{
    private GameState _state;

    public HandCellView(GameState state)
    {
        _state = state;
        Width = CellRenderer.CellWidth + CellRenderer.GapRight;
        Height = CellRenderer.CellHeight + CellRenderer.GapBottom;
    }

    public void UpdateState(GameState state)
    {
        _state = state;
        SetNeedsDisplay();
    }

    public override void Redraw(Rect bounds)
    {
        // Pre-fill with gap color so the trailing right/bottom edges show the
        // same moat as the per-cell gap.
        CellDrawing.FillGap(this, 0, 0, bounds.Width, bounds.Height);

        var stack = _state.HasItemsInHand ? _state.HandItems[0] : null;
        var cell = stack is null ? new Cell() : new Cell(stack);
        CellDrawing.Draw(this, 0, 0, cell, isCursor: false);
    }
}
