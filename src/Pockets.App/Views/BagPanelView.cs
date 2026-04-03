using Terminal.Gui;
using Pockets.Core.Models;
using Pockets.App.Rendering;
using static Pockets.App.Rendering.CategoryColors;

namespace Pockets.App.Views;

/// <summary>
/// A reusable panel that renders a bag's grid with cursor highlighting.
/// Used for C (container), W (world), and T (toolbar) panels.
/// Simpler than GridPanel — no breadcrumbs, description, or mouse handling.
/// </summary>
public class BagPanelView : FrameView
{
    private readonly LocationId _locationId;
    private Bag? _bag;
    private Cursor _cursor = new(new Position(0, 0));
    private bool _isFocused;

    public LocationId LocationId => _locationId;

    public BagPanelView(LocationId locationId, string title) : base(title)
    {
        _locationId = locationId;
        Visible = false;

        ColorScheme = new ColorScheme
        {
            Normal = Application.Driver.MakeAttribute(Color.White, Color.Black),
            Focus = Application.Driver.MakeAttribute(Color.White, Color.Black),
            HotNormal = Application.Driver.MakeAttribute(Color.White, Color.Black),
            HotFocus = Application.Driver.MakeAttribute(Color.White, Color.Black)
        };
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

    public override void Redraw(Rect bounds)
    {
        // Draw frame border with focus color
        var borderColor = _isFocused
            ? Application.Driver.MakeAttribute(Color.BrightCyan, Color.Black)
            : Application.Driver.MakeAttribute(Color.DarkGray, Color.Black);

        // Override the FrameView border color
        var savedScheme = ColorScheme;
        ColorScheme = new ColorScheme
        {
            Normal = borderColor,
            Focus = borderColor,
            HotNormal = borderColor,
            HotFocus = borderColor
        };

        base.Redraw(bounds);
        ColorScheme = savedScheme;

        if (_bag is null) return;

        var grid = _bag.Grid;
        for (int row = 0; row < grid.Rows; row++)
        {
            for (int col = 0; col < grid.Columns; col++)
            {
                var pos = new Position(row, col);
                var cell = grid.GetCell(pos);
                var isCursor = _isFocused && row == _cursor.Position.Row && col == _cursor.Position.Col;

                var x = col * CellRenderer.CellWidth;
                var y = row * CellRenderer.CellHeight;

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
        Move(x + 1, y + 1); // +1 for FrameView border
        driver.AddRune('\u250c');
        for (int i = 0; i < CellRenderer.ContentWidth; i++)
            driver.AddRune('\u2500');
        driver.AddRune('\u2510');

        // Content row
        Move(x + 1, y + 2);
        driver.SetAttribute(borderAttr);
        driver.AddRune('\u2502');
        driver.SetAttribute(contentAttr);
        var content = CellRenderer.GetCellContent(cell);
        foreach (var ch in content)
            driver.AddRune(ch);
        driver.SetAttribute(borderAttr);
        driver.AddRune('\u2502');

        // Bottom border
        Move(x + 1, y + 3);
        driver.AddRune('\u2514');
        for (int i = 0; i < CellRenderer.ContentWidth; i++)
            driver.AddRune('\u2500');
        driver.AddRune('\u2518');
    }
}
