using Terminal.Gui;
using Pockets.App.Views;

namespace Pockets.App.Tests.Views;

/// <summary>
/// Tests for the action log panel. Regression coverage for Fix 3: older entries
/// must remain reachable when the log exceeds the visible height.
/// </summary>
public class RightPanelTests : IDisposable
{
    private TuiTestHarness? _harness;

    private RightPanel SetupPanel()
    {
        _harness = TuiTestHarness.Create();
        var panel = new RightPanel
        {
            X = 0,
            Y = 0,
            Width = 40,
            Height = 10
        };
        _harness.AddView(panel);
        _harness.Render();
        return panel;
    }

    public void Dispose()
    {
        _harness?.Dispose();
    }

    [Fact]
    public void Log_KeepsAllEntries_EvenWhenExceedingVisibleHeight()
    {
        var panel = SetupPanel();
        // 50 entries, panel height is 10 — most won't fit on screen at once.
        var entries = Enumerable.Range(1, 50).Select(i => $"entry-{i}").ToList();

        panel.UpdateLog(entries);
        _harness!.Render();

        // The full content must be present in the panel's underlying label,
        // not silently truncated. We check via the panel's first label child.
        var label = FindFirstSubview<Label>(panel)!;

        for (int i = 1; i <= 50; i++)
            Assert.Contains($"entry-{i}", label.Text.ToString());
    }

    [Fact]
    public void Log_NewestEntryAtTop()
    {
        var panel = SetupPanel();
        var entries = new List<string> { "first", "second", "third" };

        panel.UpdateLog(entries);

        var label = FindFirstSubview<Label>(panel)!;

        var text = label.Text.ToString() ?? "";
        var firstIdx = text.IndexOf("first");
        var thirdIdx = text.IndexOf("third");
        Assert.True(thirdIdx >= 0 && firstIdx > thirdIdx,
            $"newest 'third' should come before 'first' in the text. text={text}");
    }

    private static T? FindFirstSubview<T>(View root) where T : View
    {
        foreach (var sub in root.Subviews)
        {
            if (sub is T match)
                return match;
            var deeper = FindFirstSubview<T>(sub);
            if (deeper is not null)
                return deeper;
        }
        return null;
    }
}
