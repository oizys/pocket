using System.Collections.Immutable;
using Terminal.Gui;
using Pockets.Core.Data;
using Pockets.Core.Models;
using Pockets.App.Views;

namespace Pockets.App.Tests.Views;

/// <summary>
/// Slice 4 (TUI render prong): the <c>c</c> key drives the generic look-in peek, and an enter-only
/// bag surfaces the failed-peek affordance — the command-strip shake/flash plus the fire-once
/// "Can't just peek at this one." dialogue beat. Asserted at the "is it drawn" depth only; the game
/// logic is proven at the Core view-model level (PeekTests, journey Slice-4 checkpoints).
/// </summary>
public class PeekTuiTests : IDisposable
{
    private TuiTestHarness? _harness;

    private static readonly ItemType Rock = new("Rock", Category.Material, IsStackable: true, MaxStackSize: 20);
    private static readonly ItemType ChestType = new("Chest", Category.Bag, IsStackable: false);
    private static readonly ItemType PocketType = new("Quiet Pocket", Category.Bag, IsStackable: false);
    private static readonly ImmutableArray<ItemType> Types = ImmutableArray.Create(Rock, ChestType, PocketType);

    private const string FailedPeekMarkdown = """
        # Dialogue: failed-peek
        Trigger: FirstFailedPeek

        - (wary) Can't just peek at this one.
        """;

    /// <summary>Root grid: (0,0) enter-only Quiet Pocket, (0,1) peekable Chest holding a Rock.</summary>
    private static GameState MakeState()
    {
        var pocket = new Bag(Grid.Create(2, 2), "Quiet Pocket") { EnterOnly = true };
        var chest = new Bag(Grid.Create(4, 2), "Chest")
            .AcquireItems(new[] { new ItemStack(Rock, 3) }).UpdatedBag;

        var grid = Grid.Create(4, 2)
            .SetCell(0, new Cell(new ItemStack(PocketType, 1, ContainedBagId: pocket.Id)))
            .SetCell(1, new Cell(new ItemStack(ChestType, 1, ContainedBagId: chest.Id)));
        var rootBag = new Bag(grid);
        var handBag = GameState.CreateHandBag();
        var toolbarBag = new Bag(Grid.Create(4, 1), "Toolbar");
        var store = BagStore.Empty.Add(rootBag).Add(handBag).Add(toolbarBag).Add(pocket).Add(chest);
        var locations = LocationMap.Create(handBag.Id, rootBag.Id)
            .Set(LocationId.T, Location.AtOrigin(toolbarBag.Id));
        return new GameState(store, locations, Types);
    }

    private GameView SetupGame(GameState state)
    {
        _harness = TuiTestHarness.Create();
        var book = DialogueLoader.LoadFromMarkdown(FailedPeekMarkdown);
        var gameView = new GameView(state, enableTickTimer: false, dialogue: book)
        {
            X = 0, Y = 0, Width = Dim.Fill(), Height = Dim.Fill()
        };
        _harness.AddView(gameView);
        _harness.Render();
        return gameView;
    }

    private void SendKey(GameView view, Key key)
    {
        view.ProcessKey(new KeyEvent(key, new KeyModifiers()));
        _harness!.Render();
    }

    public void Dispose() => _harness?.Dispose();

    [Fact]
    public void CommandStrip_AdvertisesPeek()
    {
        SetupGame(MakeState());
        Assert.Contains("C:Peek", _harness!.DumpBuffer());
    }

    [Fact]
    public void PeekOnEnterOnly_FlashesShake_AndFiresFailedPeekBeat()
    {
        var view = SetupGame(MakeState()); // cursor starts on the enter-only Quiet Pocket at (0,0)

        SendKey(view, (Key)'c'); // peek → refused

        var dump = _harness!.DumpBuffer();
        Assert.Contains("Enter-only", dump);                    // command-strip shake/flash
        Assert.Contains("Can't just peek at this one", dump);   // fire-once dialogue beat
    }

    [Fact]
    public void PeekOnPlainChest_OpensLookInOverlay()
    {
        var view = SetupGame(MakeState());

        SendKey(view, (Key)'d'); // move right to the Chest at (0,1)
        SendKey(view, (Key)'c'); // peek → opens the look-in overlay

        Assert.True(view.Controller.Session.Current.Locations.Has(LocationId.C));
        Assert.Equal(LocationId.C, view.Controller.Focus);
        // The overlay panel is titled by the bag's environment type.
        Assert.Contains("Chest", _harness!.DumpBuffer());
    }
}
