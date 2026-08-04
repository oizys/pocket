using Terminal.Gui;

namespace Pockets.App.Views;

/// <summary>
/// The dialogue box (Slice 2): a bottom-third overlay with a glyph portrait on the left, the
/// current line to its right, and an advance affordance. Modal-lite — it draws only while a beat
/// is showing (<see cref="GameView"/> gates visibility on the Core dialogue state) and Primary
/// advances/dismisses. Pure renderer: the active line + portrait tag come from Core.
/// </summary>
public class DialogueBoxView : FrameView
{
    private readonly Label _portrait;
    private readonly Label _text;
    private readonly Label _affordance;

    /// <summary>Portrait placeholder width (glyph column), left of the text.</summary>
    private const int PortraitWidth = 9;

    public DialogueBoxView()
    {
        Title = "❝";
        X = 0;
        Y = Pos.Percent(66);
        Width = Dim.Fill();
        Height = Dim.Fill(1); // leave the global command strip's bottom row clear

        _portrait = new Label("")
        {
            X = 1,
            Y = 0,
            Width = PortraitWidth,
            Height = Dim.Fill()
        };

        _text = new Label("")
        {
            X = PortraitWidth + 2,
            Y = 1,
            Width = Dim.Fill(1),
            Height = Dim.Fill(2)
        };

        _affordance = new Label("▸ Primary to continue")
        {
            X = PortraitWidth + 2,
            Y = Pos.AnchorEnd(1),
            Width = Dim.Fill(1),
            Height = 1
        };

        Add(_portrait, _text, _affordance);
    }

    /// <summary>Shows a line: renders the emotion glyph and the spoken text.</summary>
    public void Show(string portrait, string text)
    {
        _portrait.Text = PortraitGlyph(portrait);
        _text.Text = text;
        Visible = true;
        SetNeedsDisplay();
    }

    /// <summary>Hides the box (no beat showing).</summary>
    public void Hide()
    {
        Visible = false;
        SetNeedsDisplay();
    }

    /// <summary>
    /// A three-line glyph portrait placeholder for an emotion tag (no asset pipeline — the Godot
    /// build uses a colored-rect placeholder for the same tags). Unknown tags fall back to neutral.
    /// </summary>
    private static string PortraitGlyph(string emotion) => emotion.ToLowerInvariant() switch
    {
        "groggy"  => " .---. \n |-.-| \n '---' ",
        "puzzled" => " .---. \n |?.o| \n '---' ",
        _          => " .---. \n |o.o| \n '---' "
    };
}
