using Terminal.Gui;
using Pockets.Core.Models;

namespace Pockets.App.Views;

/// <summary>
/// Right panel showing the scrollable action log. Newest entry at top; older
/// entries reachable via scroll. ContentSize grows with the log so nothing is
/// silently dropped.
/// </summary>
public class RightPanel : FrameView
{
    private readonly ScrollView _scrollView;
    private readonly Label _logContent;

    public RightPanel()
    {
        Title = "Action Log";
        X = Pos.Percent(70);
        Y = 0;
        Width = Dim.Fill();
        Height = Dim.Fill();

        _scrollView = new ScrollView()
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
            ShowVerticalScrollIndicator = true,
            ShowHorizontalScrollIndicator = false,
        };

        _logContent = new Label("")
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = 1
        };
        _scrollView.Add(_logContent);
        Add(_scrollView);
    }

    /// <summary>
    /// Updates the log display with every entry from the session, newest first.
    /// Older entries remain reachable via the ScrollView's vertical scroll.
    /// </summary>
    public void UpdateLog(IReadOnlyList<string> actionLog)
    {
        var reversed = actionLog.Reverse().ToList();
        _logContent.Text = string.Join("\n", reversed);
        _logContent.Height = Math.Max(1, reversed.Count);
        _scrollView.ContentSize = new Size(_scrollView.Bounds.Width, reversed.Count);
        SetNeedsDisplay();
    }
}
