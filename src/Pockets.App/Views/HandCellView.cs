using Terminal.Gui;
using Pockets.Core.Models;
using Pockets.App.Rendering;
using static Pockets.App.Rendering.CategoryColors;

namespace Pockets.App.Views;

/// <summary>
/// Displays the current hand contents as a prominent cell next to the grid.
/// Shows item abbreviation and count when holding, empty when not.
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
        var driver = Application.Driver;
        var hasItems = _state.HasItemsInHand;

        // Border: category-colored when holding, dim when empty
        var borderBg = hasItems
            ? CategoryColors.GetBackground(_state.HandItems[0].ItemType.Category)
            : Color.Black;
        var borderFg = hasItems ? Color.White : Color.DarkGray;
        var borderAttr = driver.MakeAttribute(borderFg, borderBg);

        // Content: cyan on black when holding, blank when empty
        var contentAttr = hasItems
            ? driver.MakeAttribute(Color.Cyan, Color.Black)
            : driver.MakeAttribute(Color.White, Color.Black);

        // Top border
        driver.SetAttribute(borderAttr);
        Move(0, 0);
        driver.AddRune('\u250c');
        for (int i = 0; i < CellRenderer.ContentWidth; i++)
            driver.AddRune('\u2500');
        driver.AddRune('\u2510');

        // Content row
        Move(0, 1);
        driver.SetAttribute(borderAttr);
        driver.AddRune('\u2502');
        driver.SetAttribute(contentAttr);

        string content;
        if (hasItems)
        {
            var item = _state.HandItems[0];
            var abbrev = CellRenderer.AbbreviateName(item.ItemType.Name);
            content = item.ItemType.IsStackable
                ? $"{abbrev}\u00d7{item.Count}"
                : abbrev;
            content = content.Length <= CellRenderer.ContentWidth
                ? content.PadRight(CellRenderer.ContentWidth)
                : content[..CellRenderer.ContentWidth];
        }
        else
        {
            content = new string(' ', CellRenderer.ContentWidth);
        }

        foreach (var ch in content)
            driver.AddRune(ch);

        driver.SetAttribute(borderAttr);
        driver.AddRune('\u2502');

        // Bottom border
        Move(0, 2);
        driver.AddRune('\u2514');
        for (int i = 0; i < CellRenderer.ContentWidth; i++)
            driver.AddRune('\u2500');
        driver.AddRune('\u2518');
    }
}
