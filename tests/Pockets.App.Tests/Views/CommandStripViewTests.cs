using System.Collections.Immutable;
using Terminal.Gui;
using Pockets.Core.Models;
using Pockets.App.Views;

namespace Pockets.App.Tests.Views;

/// <summary>
/// Tests for the global CommandStripView. Stage 2 surface: default hint when
/// idle; inline split editor when SplitMode is active.
/// </summary>
public class CommandStripViewTests : IDisposable
{
    private TuiTestHarness? _harness;

    private static readonly ItemType Rock =
        new("Rock", Category.Material, IsStackable: true, MaxStackSize: 20);
    private static readonly ImmutableArray<ItemType> AllTypes = ImmutableArray.Create(Rock);

    private static GameSession MakeSessionWithStack(int count)
    {
        var cells = new Cell[4];
        cells[0] = new Cell(new ItemStack(Rock, count));
        for (int i = 1; i < 4; i++) cells[i] = new Cell();
        var grid = new Grid(4, 1, cells.ToImmutableArray());
        var rootBag = new Bag(grid);
        var handBag = GameState.CreateHandBag();
        var store = BagStore.Empty.Add(rootBag).Add(handBag);
        var state = new GameState(store, LocationMap.Create(handBag.Id, rootBag.Id), AllTypes);
        return GameSession.New(state);
    }

    private CommandStripView SetupStrip()
    {
        _harness = TuiTestHarness.Create();
        var strip = new CommandStripView
        {
            X = 0,
            Y = 0,
            Width = 80,
            Height = 1
        };
        _harness.AddView(strip);
        _harness.Render();
        return strip;
    }

    public void Dispose()
    {
        _harness?.Dispose();
    }

    [Fact]
    public void Idle_ShowsDefaultHotkeyHint()
    {
        var strip = SetupStrip();
        var session = MakeSessionWithStack(8);

        strip.Update(session);
        _harness!.Render();

        var dump = _harness.DumpBuffer();
        // The default hint may overflow an 80-column harness, so only assert
        // on tokens that fit within the visible width.
        Assert.Contains("Action", dump);
        Assert.Contains("Half", dump);
        Assert.Contains("Sort", dump);
        // Inline split editor must NOT show when idle
        Assert.DoesNotContain("←/→ adjust", dump);
    }

    [Fact]
    public void SplitMode_ShowsInlineSplitEditor()
    {
        var strip = SetupStrip();
        var session = MakeSessionWithStack(8).BeginSplit(LocationId.B);
        Assert.NotNull(session.SplitMode);

        strip.Update(session);
        _harness!.Render();

        var dump = _harness.DumpBuffer();
        Assert.Contains("Split:", dump);
        Assert.Contains("grab 4", dump);
        Assert.Contains("leave 4", dump);
        Assert.Contains("←/→ adjust", dump);
        Assert.Contains("Enter confirm", dump);
        Assert.Contains("Esc cancel", dump);
    }

    [Fact]
    public void ExitSplitMode_RestoresDefaultHint()
    {
        var strip = SetupStrip();
        var session = MakeSessionWithStack(8).BeginSplit(LocationId.B);
        strip.Update(session);
        _harness!.Render();

        var afterCancel = session.CancelSplit();
        strip.Update(afterCancel);
        _harness.Render();

        var dump = _harness.DumpBuffer();
        Assert.DoesNotContain("Split:", dump);
        Assert.Contains("Action", dump);
    }
}
