using Terminal.Gui;
using Pockets.Core.Models;

namespace Pockets.App.Views;

/// <summary>
/// Single global one-row strip at the bottom of GameView. Replaces the
/// per-GridPanel hotkey-hint label and serves as the inline editor surface
/// for SplitMode (Stage 2). When SplitMode is null the strip shows the
/// available verbs as a static hotkey hint. When SplitMode is active it
/// shows the inline split editor.
///
/// Stage 2 keeps the verb hint static. Context-aware verbs (varying by
/// focused panel and cursor cell) land in a later stage.
/// </summary>
public class CommandStripView : View
{
    private readonly Label _content;

    private const string DefaultHint =
        "[1/E:Action] [2:Half] [3:QuickSplit] [#:Split] [4:Sort] [R:Recipe] [Q:Back] [^Z:Undo]";

    public CommandStripView()
    {
        X = 0;
        Y = Pos.AnchorEnd(1);
        Width = Dim.Fill();
        Height = 1;

        _content = new Label(DefaultHint)
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = 1
        };
        Add(_content);
    }

    /// <summary>
    /// Updates the strip to reflect SplitMode if active, otherwise the default hint.
    /// </summary>
    public void Update(GameSession session)
    {
        if (session.SplitMode is { } sm)
        {
            var left = sm.StackTotal - sm.GrabCount;
            _content.Text =
                $"Split: grab {sm.GrabCount} / leave {left} (total {sm.StackTotal})  " +
                "←/→ adjust   Enter confirm   Esc cancel";
        }
        else
        {
            _content.Text = DefaultHint;
        }
        _content.SetNeedsDisplay();
        SetNeedsDisplay();
    }
}
