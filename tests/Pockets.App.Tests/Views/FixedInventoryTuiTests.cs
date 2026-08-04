using System.Collections.Immutable;
using Terminal.Gui;
using Pockets.Core.Models;
using Pockets.App.Views;

namespace Pockets.App.Tests.Views;

/// <summary>
/// Slice 3 (TUI render): the toolbar is a real bottom panel that materializes on the demo's first
/// pickup and shows the routed item — exercised through the real GameView/FakeDriver pipeline.
/// </summary>
public class FixedInventoryTuiTests : IDisposable
{
    private TuiTestHarness? _harness;

    private static readonly ItemType Rock = new("Rock", Category.Material, IsStackable: true, MaxStackSize: 20);
    private static readonly ImmutableArray<ItemType> AllTypes = ImmutableArray.Create(Rock);

    /// <summary>Demo-like state: grid-only chrome (toolbar OFF), pickup routing ON, one item at (0,0), a 4×1 toolbar.</summary>
    private static GameState DemoLikeState()
    {
        var root = new Bag(new Grid(4, 2,
            (new[] { new Cell(new ItemStack(Rock, 5)) }.Concat(Enumerable.Repeat(new Cell(), 7))).ToImmutableArray()));
        var hand = GameState.CreateHandBag();
        var toolbar = new Bag(Grid.Create(4, 1), "Toolbar");
        var store = BagStore.Empty.Add(root).Add(hand).Add(toolbar);
        var locations = LocationMap.Create(hand.Id, root.Id)
            .Set(LocationId.T, Location.AtOrigin(toolbar.Id));
        return new GameState(store, locations, AllTypes)
        {
            ToolbarPickup = true,
            Ui = UiLedger.DemoInitial // Grid only → toolbar chrome starts OFF
        };
    }

    private (GameView View, TuiTestHarness Harness) Setup(GameState state)
    {
        _harness = TuiTestHarness.Create();
        var view = new GameView(state, enableTickTimer: false)
        {
            X = 0, Y = 0, Width = Dim.Fill(), Height = Dim.Fill()
        };
        _harness.AddView(view);
        _harness.Render();
        return (view, _harness);
    }

    private string Buffer() => _harness!.DumpBuffer();

    [Fact]
    public void ToolbarPanel_AbsentBeforePickup_PresentAfter()
    {
        var (view, _) = Setup(DemoLikeState());
        Assert.DoesNotContain("Toolbar", Buffer()); // ledger-gated off at start

        view.ProcessKey(new KeyEvent((Key)'1', new KeyModifiers())); // Primary → pickup routes to toolbar
        _harness!.Render();

        Assert.Contains("Toolbar", Buffer()); // panel materialized on FirstPickup
    }

    public void Dispose() => _harness?.Dispose();
}
