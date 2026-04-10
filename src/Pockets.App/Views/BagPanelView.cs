using Terminal.Gui;
using Pockets.Core.Models;
using Pockets.App.Rendering;
using static Pockets.App.Rendering.CategoryColors;

namespace Pockets.App.Views;

/// <summary>
/// A reusable panel that renders a bag's grid with cursor highlighting.
/// Used for C (container), W (world), and T (toolbar) panels.
/// Inherits from View (not FrameView) so mouse events on cells reach the override.
/// Draws its own title and border.
/// </summary>
public class BagPanelView : View
{
    private readonly LocationId _locationId;
    private string _title;
    private Bag? _bag;
    private Cursor _cursor = new(new Position(0, 0));
    private bool _isFocused;

    public LocationId LocationId => _locationId;

    /// <summary>Title to display in the top border.</summary>
    public string Title
    {
        get => _title;
        set { _title = value; SetNeedsDisplay(); }
    }

    /// <summary>Fired when a cell in this panel is clicked. (LocationId, Position, ClickType)</summary>
    public event Action<LocationId, Position, ClickType>? CellClicked;

    public BagPanelView(LocationId locationId, string title)
    {
        _locationId = locationId;
        _title = title;
        Visible = false;
        CanFocus = true;
        WantMousePositionReports = true;
    }

    /// <summary>
    /// Updates the bag and cursor for this panel. Pass null bag to hide.
    /// </summary>
    public void UpdatePanel(Bag? bag, Cursor? cursor, bool isFocused)
    {
        _bag = bag;
        _cursor = cursor ?? new Cursor(new Position(0, 0));
        _isFocused = isFocused;

        if (bag is not null)
        {
            // +2 for border (1 char on each side)
            Width = CellRenderer.CellWidth * bag.Grid.Columns + 2;
            Height = CellRenderer.CellHeight * bag.Grid.Rows + 2;
            Visible = true;
        }
        else
        {
            Visible = false;
        }

        SetNeedsDisplay();
    }

    public override bool MouseEvent(MouseEvent mouseEvent)
    {
        if (_bag is null) return false;

        // Coordinates are relative to this View. Border is at 0/edge, cells start at +1.
        var gridX = mouseEvent.X - 1;
        var gridY = mouseEvent.Y - 1;
        if (gridX < 0 || gridY < 0) return false;

        var col = gridX / CellRenderer.CellWidth;
        var row = gridY / CellRenderer.CellHeight;
        if (col < 0 || col >= _bag.Grid.Columns || row < 0 || row >= _bag.Grid.Rows)
            return false;

        var pos = new Position(row, col);

        if (mouseEvent.Flags.HasFlag(MouseFlags.Button1Clicked))
        {
            CellClicked?.Invoke(_locationId, pos, ClickType.Primary);
            return true;
        }
        if (mouseEvent.Flags.HasFlag(MouseFlags.Button3Clicked) ||
            mouseEvent.Flags.HasFlag(MouseFlags.Button2Clicked))
        {
            CellClicked?.Invoke(_locationId, pos, ClickType.Secondary);
            return true;
        }

        return false;
    }

    public override void Redraw(Rect bounds)
    {
        var driver = Application.Driver;

        // Clear background to black
        var bgAttr = driver.MakeAttribute(Color.White, Color.Black);
        driver.SetAttribute(bgAttr);
        for (int row = 0; row < bounds.Height; row++)
        {
            Move(0, row);
            for (int col = 0; col < bounds.Width; col++)
                driver.AddRune(' ');
        }

        // Draw border
        var borderColor = _isFocused
            ? driver.MakeAttribute(Color.BrightCyan, Color.Black)
            : driver.MakeAttribute(Color.DarkGray, Color.Black);
        driver.SetAttribute(borderColor);

        var w = bounds.Width;
        var h = bounds.Height;

        // Top border with title
        Move(0, 0);
        driver.AddRune('\u250c'); // ┌
        var titleText = _isFocused ? $"► {_title}" : $"  {_title}";
        var titleStart = 1;
        for (int i = 1; i < w - 1; i++)
        {
            if (i - titleStart < titleText.Length)
                driver.AddRune(titleText[i - titleStart]);
            else
                driver.AddRune('\u2500'); // ─
        }
        driver.AddRune('\u2510'); // ┐

        // Side borders
        for (int y = 1; y < h - 1; y++)
        {
            Move(0, y);
            driver.AddRune('\u2502'); // │
            Move(w - 1, y);
            driver.AddRune('\u2502');
        }

        // Bottom border
        Move(0, h - 1);
        driver.AddRune('\u2514'); // └
        for (int i = 1; i < w - 1; i++)
            driver.AddRune('\u2500');
        driver.AddRune('\u2518'); // ┘

        if (_bag is null) return;

        // Draw cells
        var grid = _bag.Grid;
        for (int row = 0; row < grid.Rows; row++)
        {
            for (int col = 0; col < grid.Columns; col++)
            {
                var pos = new Position(row, col);
                var cell = grid.GetCell(pos);
                var isCursor = _isFocused && row == _cursor.Position.Row && col == _cursor.Position.Col;

                var x = col * CellRenderer.CellWidth + 1; // +1 for border
                var y = row * CellRenderer.CellHeight + 1;

                DrawCell(x, y, cell, isCursor);
            }
        }
    }

    private void DrawCell(int x, int y, Cell cell, bool isCursor)
    {
        var driver = Application.Driver;

        var borderBg = cell.IsEmpty ? Color.Black : GetBackground(cell.Stack!.ItemType.Category);
        var borderFg = cell.HasFrame
            ? GetFrameForeground(cell.Frame)
            : cell.IsEmpty ? Color.DarkGray : GetBorderForeground(cell.Stack!.ItemType.Category);
        var borderAttr = driver.MakeAttribute(borderFg, borderBg);

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
