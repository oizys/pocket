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
    private IReadOnlyList<string> _queueLines = System.Array.Empty<string>();
    private string _minimapLine = "";

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
    /// Sets the Slice-5 chrome markers on the panel's title bar (the TUI's "top bar" surface with
    /// room to spare): the clock readout, the Shrine-view indicator, and the fullness legend. Each is
    /// supplied by the caller only when its UiLedger flag is on — the thin per-frontend render check
    /// for ClockReadout / ShrineView / FullnessPips. Empty markers restore the plain title.
    /// </summary>
    public void SetChromeMarkers(string markers)
    {
        // Prepend the markers so the clock/shrine/fullness text lands at the very start of the title
        // (the panel is narrow — a trailing suffix would clip past the visible width).
        Title = string.IsNullOrEmpty(markers) ? "Action Log" : $"{markers}  Log";
        SetNeedsDisplay();
    }

    /// <summary>
    /// Sets the Slice-7 realtime-craft chrome for the right panel (the TUI's "World / queue" surface):
    /// the action-queue rows ("&lt;recipe&gt; progress/duration") and the minimap radar line ("◉ n/12").
    /// Both are supplied only when their UiLedger flag is on — the thin per-frontend render of
    /// ActionQueue and Minimap. Recomposed into the panel by the next <see cref="UpdateLog"/>.
    /// </summary>
    public void SetQueueAndMinimap(IReadOnlyList<string> queueLines, string minimapLine)
    {
        _queueLines = queueLines;
        _minimapLine = minimapLine;
    }

    /// <summary>
    /// Updates the log display with every entry from the session, newest first, beneath the realtime
    /// chrome (minimap radar line + action-queue rows, when materialized). Older entries remain
    /// reachable via the ScrollView's vertical scroll.
    /// </summary>
    public void UpdateLog(IReadOnlyList<string> actionLog)
    {
        var lines = new List<string>();
        if (!string.IsNullOrEmpty(_minimapLine))
            lines.Add(_minimapLine);
        lines.AddRange(_queueLines);
        if (lines.Count > 0)
            lines.Add(new string('-', 8)); // divider between the live chrome and the log
        lines.AddRange(actionLog.Reverse());

        _logContent.Text = string.Join("\n", lines);
        _logContent.Height = Math.Max(1, lines.Count);
        _scrollView.ContentSize = new Size(_scrollView.Bounds.Width, lines.Count);
        SetNeedsDisplay();
    }
}
