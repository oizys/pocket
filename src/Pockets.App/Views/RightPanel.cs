using Terminal.Gui;

namespace Pockets.App.Views;

/// <summary>
/// Right panel placeholder for Stage 1. Will hold action queue and world view in later stages.
/// </summary>
public class RightPanel : FrameView
{
    public RightPanel()
    {
        Title = "World";
        X = Pos.Percent(70);
        Y = 0;
        Width = Dim.Fill();
        Height = Dim.Fill();

        var placeholder = new Label("Stage 1")
        {
            X = Pos.Center(),
            Y = Pos.Center()
        };
        Add(placeholder);
    }
}
