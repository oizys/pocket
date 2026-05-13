using Terminal.Gui;
using Pockets.Core.Models;
using Pockets.Core.Rendering;
using Pockets.App.Rendering;

namespace Pockets.App.Views;

/// <summary>
/// Custom View that draws the active bag's grid as 3×2 colored-glyph cells.
/// Handles mouse events and translates them to grid cell positions.
/// </summary>
public class GridView : View
{
    private GameState _state;

    /// <summary>Fired when a cell is left-clicked.</summary>
    public event Action<Position>? GridCellClicked;

    /// <summary>Fired when a cell is right-clicked.</summary>
    public event Action<Position>? GridCellRightClicked;

    public GridView(GameState state)
    {
        _state = state;
        Width = GridPixelWidth(state);
        Height = GridPixelHeight(state);
        WantMousePositionReports = true;
    }

    public void UpdateState(GameState state)
    {
        _state = state;
        Width = GridPixelWidth(state);
        Height = GridPixelHeight(state);
        SetNeedsDisplay();
    }

    /// <summary>Total grid width: per-cell envelopes + trailing right gap for symmetric padding.</summary>
    private static int GridPixelWidth(GameState state) =>
        CellRenderer.CellWidth * state.ActiveBag.Grid.Columns + CellRenderer.GapRight;

    private static int GridPixelHeight(GameState state) =>
        CellRenderer.CellHeight * state.ActiveBag.Grid.Rows + CellRenderer.GapBottom;

    private Position? MouseToGridPosition(int x, int y)
    {
        var col = x / CellRenderer.CellWidth;
        var row = y / CellRenderer.CellHeight;

        var grid = _state.ActiveBag.Grid;
        if (row < 0 || row >= grid.Rows || col < 0 || col >= grid.Columns)
            return null;

        return new Position(row, col);
    }

    public override bool MouseEvent(MouseEvent mouseEvent)
    {
        if (mouseEvent.Flags.HasFlag(MouseFlags.Button1Clicked))
        {
            var pos = MouseToGridPosition(mouseEvent.X, mouseEvent.Y);
            if (pos is not null)
                GridCellClicked?.Invoke(pos.Value);
            return true;
        }

        if (mouseEvent.Flags.HasFlag(MouseFlags.Button3Clicked) ||
            mouseEvent.Flags.HasFlag(MouseFlags.Button2Clicked))
        {
            var pos = MouseToGridPosition(mouseEvent.X, mouseEvent.Y);
            if (pos is not null)
                GridCellRightClicked?.Invoke(pos.Value);
            return true;
        }

        return true; // consume all mouse events on the grid
    }

    public override void Redraw(Rect bounds)
    {
        // Pre-fill the entire grid area with the gap attribute. The per-cell
        // gaps and the trailing right/bottom edges are then visible as the
        // uniform moat color; CellDrawing overwrites the content rectangle of
        // each cell. The result is symmetric padding around the whole grid.
        CellDrawing.FillGap(this, 0, 0, bounds.Width, bounds.Height);

        var grid = _state.ActiveBag.Grid;
        var cursorPos = _state.Cursor.Position;

        for (int row = 0; row < grid.Rows; row++)
        {
            for (int col = 0; col < grid.Columns; col++)
            {
                var pos = new Position(row, col);
                var cell = grid.GetCell(pos);
                var isCursor = row == cursorPos.Row && col == cursorPos.Col;
                CellDrawing.Draw(this, col * CellRenderer.CellWidth, row * CellRenderer.CellHeight, cell, isCursor);
            }
        }
    }
}
