using Terminal.Gui;
using Pockets.App.Rendering;

namespace Pockets.App.Views;

/// <summary>
/// A clickable cell-sized "back" button (leave-bag navigation). Renders as a
/// single 3×2 cell: ` ← ` on row 1 when enabled, blank otherwise.
/// </summary>
public class BackButtonView : View
{
    private bool _enabled;

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
        var attr = _enabled
            ? driver.MakeAttribute(Color.BrightCyan, Color.Black)
            : driver.MakeAttribute(Color.DarkGray, Color.Black);
        driver.SetAttribute(attr);

        Move(0, 0);
        foreach (var ch in _enabled ? " ← " : "   ")
            driver.AddRune(ch);

        Move(0, 1);
        foreach (var ch in "   ")
            driver.AddRune(ch);
    }
}
