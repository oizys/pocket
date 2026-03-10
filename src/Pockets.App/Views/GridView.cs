using Terminal.Gui;
using Pockets.Core.Models;
using Pockets.App.Rendering;
using static Pockets.App.Rendering.CategoryColors;

namespace Pockets.App.Views;

/// <summary>
/// Custom View that draws the inventory grid with box-drawing borders.
/// Handles mouse events and translates them to grid cell positions.
/// </summary>
public class GridView : View
{
    private GameState _state;

    /// <summary>Fired when a cell is left-clicked.</summary>
    public event Action<Position>? GridCellClicked;

    /// <summary>Fired when a cell is right-clicked.</summary>
    public event Action<Position>? GridCellRightClicked;

    /// <summary>Fired when any mouse event occurs (for debug display).</summary>
    public event Action<MouseFlags>? MouseStateChanged;

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
        MouseStateChanged?.Invoke(mouseEvent.Flags);

        // Left click — primary action (immediate, like Minecraft/Factorio)
        if (mouseEvent.Flags.HasFlag(MouseFlags.Button1Clicked))
        {
            var pos = MouseToGridPosition(mouseEvent.X, mouseEvent.Y);
            if (pos is not null)
                GridCellClicked?.Invoke(pos.Value);
            return true;
        }

        // Right click — secondary action
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

        // Clear entire view area to black
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
                DrawCell(col * CellRenderer.CellWidth, row * CellRenderer.CellHeight, cell, isCursor);
            }
        }
    }

    private void DrawCell(int x, int y, Cell cell, bool isCursor)
    {
        var driver = Application.Driver;

        // Border color: category-colored background, frame-colored foreground
        var borderBg = cell.IsEmpty ? Color.Black : CategoryColors.GetBackground(cell.Stack!.ItemType.Category);
        var borderFg = cell.HasFrame
            ? CategoryColors.GetFrameForeground(cell.Frame)
            : cell.IsEmpty ? Color.DarkGray : CategoryColors.GetBorderForeground(cell.Stack!.ItemType.Category);
        var borderAttr = driver.MakeAttribute(borderFg, borderBg);

        // Content color: black bg normally, inverted for cursor
        var contentAttr = isCursor
            ? driver.MakeAttribute(Color.Black, Color.White)
            : driver.MakeAttribute(Color.White, Color.Black);

        // Top border
        driver.SetAttribute(borderAttr);
        Move(x, y);
        driver.AddRune('\u250c');
        for (int i = 0; i < CellRenderer.ContentWidth; i++)
            driver.AddRune('\u2500');
        driver.AddRune('\u2510');

        // Content row
        Move(x, y + 1);
        driver.SetAttribute(borderAttr);
        driver.AddRune('\u2502');
        driver.SetAttribute(contentAttr);
        var content = CellRenderer.GetCellContent(cell);
        foreach (var ch in content)
            driver.AddRune(ch);
        driver.SetAttribute(borderAttr);
        driver.AddRune('\u2502');

        // Bottom border
        Move(x, y + 2);
        driver.AddRune('\u2514');
        for (int i = 0; i < CellRenderer.ContentWidth; i++)
            driver.AddRune('\u2500');
        driver.AddRune('\u2518');
    }
}
