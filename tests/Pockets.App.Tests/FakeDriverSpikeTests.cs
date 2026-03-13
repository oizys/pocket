using Terminal.Gui;

namespace Pockets.App.Tests;

/// <summary>
/// Spike tests to validate that FakeDriver renders into a readable buffer.
/// These prove the test harness works before building more complex tests.
/// </summary>
public class FakeDriverSpikeTests : IDisposable
{
    private TuiTestHarness? _harness;

    public void Dispose()
    {
        _harness?.Dispose();
    }

    [Fact]
    public void FakeDriver_Label_RendersText()
    {
        _harness = TuiTestHarness.Create();
        var label = new Label("Hello, Pockets!")
        {
            X = 0, Y = 0, Width = 20, Height = 1
        };
        _harness.AddView(label);
        _harness.Render();

        var text = _harness.GetText(0, 0, 15);
        Assert.Equal("Hello, Pockets!", text);
    }

    [Fact]
    public void FakeDriver_Label_AtPosition()
    {
        _harness = TuiTestHarness.Create();
        var label = new Label("Test")
        {
            X = 5, Y = 3, Width = 10, Height = 1
        };
        _harness.AddView(label);
        _harness.Render();

        var found = _harness.FindText("Test");
        Assert.NotNull(found);
        Assert.Equal(5, found.Value.X);
        Assert.Equal(3, found.Value.Y);
    }

    [Fact]
    public void FakeDriver_BufferDump_ContainsLabel()
    {
        _harness = TuiTestHarness.Create();
        var label = new Label("Dump me")
        {
            X = 0, Y = 0, Width = 10, Height = 1
        };
        _harness.AddView(label);
        _harness.Render();

        var dump = _harness.DumpBuffer();
        Assert.Contains("Dump me", dump);
    }
}
