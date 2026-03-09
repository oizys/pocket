using Terminal.Gui;
using Pockets.Core.Models;

namespace Pockets.App.Views;

/// <summary>
/// Right panel showing the action log. Displays recent actions from the GameSession.
/// </summary>
public class RightPanel : FrameView
{
    private readonly Label _logContent;

    public RightPanel()
    {
        Title = "Action Log";
        X = Pos.Percent(70);
        Y = 0;
        Width = Dim.Fill();
        Height = Dim.Fill();

        _logContent = new Label("")
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill()
        };
        Add(_logContent);
    }

    /// <summary>
    /// Updates the log display with the most recent entries from the session.
    /// Shows up to 20 most recent log entries, newest at top.
    /// </summary>
    public void UpdateLog(IReadOnlyList<string> actionLog)
    {
        var maxLines = 20;
        var recent = actionLog.Count > maxLines
            ? actionLog.Skip(actionLog.Count - maxLines).Reverse()
            : actionLog.Reverse();
        _logContent.Text = string.Join("\n", recent);
        SetNeedsDisplay();
    }
}
