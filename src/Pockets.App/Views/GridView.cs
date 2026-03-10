using Terminal.Gui;
using Pockets.Core.Models;
using Pockets.App.Rendering;

namespace Pockets.App.Views;

/// <summary>
/// Custom View that draws the inventory grid with box-drawing borders.
/// Handles mouse events and translates them to grid cell positions.
/// </summary>
public class GridView : View
{
    private GameState _state;
    private Position? _dragStart;

    /// <summary>Fired when a cell is left-clicked (on button release).</summary>
    public event Action<Position>? GridCellClicked;

    /// <summary>Fired when a cell is right-clicked.</summary>
    public event Action<Position>? GridCellRightClicked;

    /// <summary>Fired when a drag from one cell to another completes.</summary>
    public event Action<Position, Position>? GridCellDragged;

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

    /// <summary>
    /// Converts view-local mouse coordinates to a grid Position.
    /// Returns null if outside the grid bounds.
    /// </summary>
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
        // Left button pressed — record drag start
        if (mouseEvent.Flags.HasFlag(MouseFlags.Button1Pressed))
        {
            _dragStart = MouseToGridPosition(mouseEvent.X, mouseEvent.Y);
            return true;
        }

        // Left button released — click or complete drag
        if (mouseEvent.Flags.HasFlag(MouseFlags.Button1Released))
        {
            var releasePos = MouseToGridPosition(mouseEvent.X, mouseEvent.Y);
            if (releasePos is not null)
            {
                if (_dragStart is not null && _dragStart != releasePos)
                    GridCellDragged?.Invoke(_dragStart.Value, releasePos.Value);
                else
                    GridCellClicked?.Invoke(releasePos.Value);
            }
            _dragStart = null;
            return true;
        }

        // Also handle Button1Clicked as fallback (some drivers send this instead of press+release)
        if (mouseEvent.Flags.HasFlag(MouseFlags.Button1Clicked))
        {
            var pos = MouseToGridPosition(mouseEvent.X, mouseEvent.Y);
            if (pos is not null)
                GridCellClicked?.Invoke(pos.Value);
            return true;
        }

        // Right click — secondary action
        if (mouseEvent.Flags.HasFlag(MouseFlags.Button3Clicked) ||
            mouseEvent.Flags.HasFlag(MouseFlags.Button3Released))
        {
            var pos = MouseToGridPosition(mouseEvent.X, mouseEvent.Y);
            if (pos is not null)
                GridCellRightClicked?.Invoke(pos.Value);
            return true;
        }

        // Right button = Button2 on some drivers
        if (mouseEvent.Flags.HasFlag(MouseFlags.Button2Clicked) ||
            mouseEvent.Flags.HasFlag(MouseFlags.Button2Released))
        {
            var pos = MouseToGridPosition(mouseEvent.X, mouseEvent.Y);
            if (pos is not null)
                GridCellRightClicked?.Invoke(pos.Value);
            return true;
        }

        return base.MouseEvent(mouseEvent);
    }

    public override void Redraw(Rect bounds)
    {
        var grid = _state.ActiveBag.Grid;
        var cursorPos = _state.Cursor.Position;

        for (int row = 0; row < grid.Rows; row++)
        {
            for (int col = 0; col < grid.Columns; col++)
            {
                var pos = new Position(row, col);
                var cell = grid.GetCell(pos);
                var isCursor = row == cursorPos.Row && col == cursorPos.Col;
                DrawCell(col * CellRenderer.CellWidth, row * CellRenderer.CellHeight, cell, isCursor);
            }
        }
    }

    private void DrawCell(int x, int y, Cell cell, bool isCursor)
    {
        var driver = Application.Driver;
        var normal = ColorScheme.Normal;
        var highlight = Application.Driver.MakeAttribute(Color.Black, Color.White);

        var contentAttr = isCursor ? highlight : normal;

        // Top border
        driver.SetAttribute(normal);
        Move(x, y);
        driver.AddRune('\u250c');
        for (int i = 0; i < CellRenderer.ContentWidth; i++)
            driver.AddRune('\u2500');
        driver.AddRune('\u2510');

        // Content row
        Move(x, y + 1);
        driver.AddRune('\u2502');
        driver.SetAttribute(contentAttr);
        var content = CellRenderer.GetCellContent(cell);
        foreach (var ch in content)
            driver.AddRune(ch);
        driver.SetAttribute(normal);
        driver.AddRune('\u2502');

        // Bottom border
        Move(x, y + 2);
        driver.AddRune('\u2514');
        for (int i = 0; i < CellRenderer.ContentWidth; i++)
            driver.AddRune('\u2500');
        driver.AddRune('\u2518');
    }
}
