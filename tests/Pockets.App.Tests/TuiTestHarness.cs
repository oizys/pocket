using Terminal.Gui;

namespace Pockets.App.Tests;

/// <summary>
/// Test harness wrapping Terminal.Gui's FakeDriver for headless rendering tests.
/// Creates an in-memory terminal buffer that views render into, allowing tests
/// to inspect rendered characters and attributes without a real terminal.
/// FakeDriver always creates an 80×25 buffer.
///
/// Terminal.Gui Application is global/static, so tests using this harness must
/// not run concurrently. Use [Collection("TUI")] on test classes.
/// </summary>
public class TuiTestHarness : IDisposable
{
    private static readonly object Lock = new();

    /// <summary>
    /// Creates a harness and initializes Application with FakeDriver.
    /// Buffer is always 80×25 (FakeDriver default). Must be disposed.
    /// Thread-safe: acquires a lock to prevent concurrent Application usage.
    /// </summary>
    public static TuiTestHarness Create()
    {
        Monitor.Enter(Lock);
        try
        {
            // Ensure clean state
            try { Application.Shutdown(); } catch { /* ignore if not initialized */ }

            Application.Init(driver: new FakeDriver());

            var contents = Application.Driver.Contents;
            var rows = contents.GetLength(0);
            var cols = contents.GetLength(1);

            return new TuiTestHarness(cols, rows);
        }
        catch
        {
            Monitor.Exit(Lock);
            throw;
        }
    }

    private TuiTestHarness(int cols, int rows)
    {
        Cols = cols;
        Rows = rows;
    }

    public int Cols { get; }
    public int Rows { get; }

    /// <summary>
    /// Adds a view to the Application's Toplevel and lays it out.
    /// </summary>
    public void AddView(View view)
    {
        Application.Top.Add(view);
    }

    /// <summary>
    /// Forces a complete layout and redraw of all views into the FakeDriver buffer.
    /// </summary>
    public void Render()
    {
        Application.Top.LayoutSubviews();
        Application.Top.Redraw(Application.Top.Bounds);
    }

    /// <summary>
    /// Gets the character (as Rune int value) at the given buffer position.
    /// Contents is [row, col, 0=rune 1=attr 2=dirty].
    /// </summary>
    public int GetRuneValue(int x, int y)
    {
        var contents = Application.Driver.Contents;
        return contents[y, x, 0];
    }

    /// <summary>
    /// Gets the character at the given buffer position.
    /// </summary>
    public char GetChar(int x, int y)
    {
        return (char)GetRuneValue(x, y);
    }

    /// <summary>
    /// Gets the attribute (color info) at the given buffer position.
    /// </summary>
    public int GetAttribute(int x, int y)
    {
        var contents = Application.Driver.Contents;
        return contents[y, x, 1];
    }

    /// <summary>
    /// Extracts a string of characters from the buffer starting at (x,y) for the given width.
    /// </summary>
    public string GetText(int x, int y, int width)
    {
        var chars = new char[width];
        for (int i = 0; i < width; i++)
            chars[i] = GetChar(x + i, y);
        return new string(chars);
    }

    /// <summary>
    /// Extracts the full row of text from the buffer.
    /// </summary>
    public string GetRow(int y)
    {
        return GetText(0, y, Cols);
    }

    /// <summary>
    /// Dumps the entire buffer as a multi-line string (useful for debugging).
    /// </summary>
    public string DumpBuffer()
    {
        var lines = new string[Rows];
        for (int row = 0; row < Rows; row++)
            lines[row] = GetRow(row);
        return string.Join('\n', lines);
    }

    /// <summary>
    /// Searches the buffer for the first occurrence of the given text.
    /// Returns the (x, y) position or null if not found.
    /// </summary>
    public (int X, int Y)? FindText(string text)
    {
        for (int row = 0; row < Rows; row++)
        {
            var line = GetRow(row);
            var idx = line.IndexOf(text, StringComparison.Ordinal);
            if (idx >= 0)
                return (idx, row);
        }
        return null;
    }

    public void Dispose()
    {
        try { Application.Shutdown(); } catch { /* ignore */ }
        Monitor.Exit(Lock);
    }
}
