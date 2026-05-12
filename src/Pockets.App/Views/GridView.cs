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
        Width = CellRenderer.CellWidth * state.ActiveBag.Grid.Columns;
        Height = CellRenderer.CellHeight * state.ActiveBag.Grid.Rows;
        WantMousePositionReports = true;
    }

    public void UpdateState(GameState state)
    {
        _state = state;
        Width = CellRenderer.CellWidth * state.ActiveBag.Grid.Columns;
        Height = CellRenderer.CellHeight * state.ActiveBag.Grid.Rows;
        SetNeedsDisplay();
    }

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
        var driver = Application.Driver;
        var bg = driver.MakeAttribute(Color.White, Color.Black);

        driver.SetAttribute(bg);
        for (int row = 0; row < bounds.Height; row++)
        {
            Move(0, row);
            for (int col = 0; col < bounds.Width; col++)
                driver.AddRune(' ');
        }

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
