using Terminal.Gui;
using Pockets.Core.Models;
using Pockets.App.Rendering;

namespace Pockets.App.Views;

/// <summary>
/// Custom View that draws the inventory grid with box-drawing borders.
/// </summary>
public class GridView : View
{
    private GameState _state;

    public GridView(GameState state)
    {
        _state = state;
        Width = CellRenderer.CellWidth * state.RootBag.Grid.Columns;
        Height = CellRenderer.CellHeight * state.RootBag.Grid.Rows;
    }

    public void UpdateState(GameState state)
    {
        _state = state;
        SetNeedsDisplay();
    }

    public override void Redraw(Rect bounds)
    {
        var grid = _state.RootBag.Grid;
        var cursorPos = _state.Cursor.Position;

        for (int row = 0; row < grid.Rows; row++)
        {
            for (int col = 0; col < grid.Columns; col++)
            {
                var cell = grid.GetCell(new Position(row, col));
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
        driver.SetAttribute(isCursor ? highlight : normal);
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
