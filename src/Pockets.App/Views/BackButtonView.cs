using Terminal.Gui;
using Pockets.App.Rendering;

namespace Pockets.App.Views;

/// <summary>
/// A clickable cell-sized button that triggers "leave bag" (back navigation).
/// Renders as a box-drawing cell with "< Back" text.
/// </summary>
public class BackButtonView : View
{
    private bool _enabled;

    /// <summary>Fired when the back button is clicked.</summary>
    public event Action? BackClicked;

    public BackButtonView()
    {
        Width = CellRenderer.CellWidth;
        Height = CellRenderer.CellHeight;
        WantMousePositionReports = true;
    }

    public void SetEnabled(bool enabled)
    {
        _enabled = enabled;
        SetNeedsDisplay();
    }

    public override bool MouseEvent(MouseEvent mouseEvent)
    {
        if (_enabled && mouseEvent.Flags.HasFlag(MouseFlags.Button1Clicked))
        {
            BackClicked?.Invoke();
            return true;
        }
        return true;
    }

    public override void Redraw(Rect bounds)
    {
        var driver = Application.Driver;
        var borderAttr = _enabled
            ? driver.MakeAttribute(Color.White, Color.Black)
            : driver.MakeAttribute(Color.DarkGray, Color.Black);
        var contentAttr = _enabled
            ? driver.MakeAttribute(Color.White, Color.Black)
            : driver.MakeAttribute(Color.DarkGray, Color.Black);

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
        var content = _enabled ? "\u2190 Back  " : "        ";
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
