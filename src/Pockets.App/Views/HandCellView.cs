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
        Width = CellRenderer.CellWidth;
        Height = CellRenderer.CellHeight;
    }

    public void UpdateState(GameState state)
    {
        _state = state;
        SetNeedsDisplay();
    }

    public override void Redraw(Rect bounds)
    {
        var hand = _state.HandBag;
        // Map the hand's first slot to a Cell for CellDrawing.
        var stack = _state.HasItemsInHand ? _state.HandItems[0] : null;
        var cell = stack is null ? new Cell() : new Cell(stack);
        CellDrawing.Draw(this, 0, 0, cell, isCursor: false);
    }
}
