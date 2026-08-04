using System.Collections.Immutable;
using Pockets.Core;
using Pockets.Core.Data;
using Pockets.Core.Models;
using Pockets.Core.Rendering;

namespace Pockets.Core.Tests;

/// <summary>
/// Slice-5 integration: the Shrine's triggers (EnterShrine, NoticeClock, CoreSlotted), the two
/// dialogue beats (capacity-while-carrying, slotting resolution), the game-wide fullness-pip
/// projection, and the demo profile's shrine/eye-core content.
/// </summary>
public class ShrineTests
{
    private static readonly ItemType EyeCoreType = new("Eye Core", Category.Tool, IsStackable: false);
    private static readonly ItemType MenuType = new("Menu", Category.Bag, IsStackable: false);
    private static readonly ItemType ToolbarType = new("Toolbar", Category.Bag, IsStackable: false);
    private static readonly ItemType ClockType = new("Clock", Category.Structure, IsStackable: false);
    private static readonly ItemType ShrineType = new("Shrine", Category.Structure, IsStackable: false);
    private static readonly ItemType PouchType = new("Pouch", Category.Bag, IsStackable: false);
    private static readonly ItemType RockType = new("Rock", Category.Material, IsStackable: true);

    private static ItemStack EyeCore() =>
        new ItemStack(EyeCoreType, 1).WithProperty(FeatureSlotFrame.GlyphProperty, new StringValue("eye"));

    private static DialogueBook Book() => DialogueLoader.LoadFromDirectory(TestPaths.DataDir);

    /// <summary>
    /// Builds a controller over a small world: a root bag whose cell 0 holds a full 2×2 Shrine
    /// (Menu/Toolbar/Clock pre-slotted + one empty eye slot), the given item pre-placed in the hand.
    /// </summary>
    private static GameController ShrineWorld(ItemStack? handItem)
    {
        var menu = new Bag(Grid.Create(2, 1), "Menu");
        var toolbarRep = new Bag(Grid.Create(2, 1), "Toolbar");
        var clockStack = new ItemStack(ClockType, 1)
            .WithProperty(FeatureSlotFrame.GlyphProperty, new StringValue(FeatureSlotFrame.ClockGlyph));

        var shrineGrid = Grid.Create(2, 2)
            .SetCell(0, new Cell(new ItemStack(MenuType, 1, ContainedBagId: menu.Id),
                Frame: new FeatureSlotFrame(FeatureSlotFrame.MenuGlyph, IsLocked: true)))
            .SetCell(1, new Cell(new ItemStack(ToolbarType, 1, ContainedBagId: toolbarRep.Id),
                Frame: new FeatureSlotFrame(FeatureSlotFrame.ToolbarGlyph, IsLocked: true)))
            .SetCell(2, new Cell(clockStack, Frame: new FeatureSlotFrame(FeatureSlotFrame.ClockGlyph, IsLocked: true)))
            .SetCell(3, new Cell(Frame: new FeatureSlotFrame(FeatureSlotFrame.EyeGlyph, IsLocked: false)));
        var shrine = new Bag(shrineGrid, GameState.ShrineEnvironment);

        var rootGrid = Grid.Create(2, 1)
            .SetCell(0, new Cell(new ItemStack(ShrineType, 1, ContainedBagId: shrine.Id)));
        var root = new Bag(rootGrid);

        var hand = GameState.CreateHandBag(1);
        if (handItem is not null)
            (hand, _) = hand.AcquireItems(new[] { handItem });

        var store = BagStore.Empty.Add(root).Add(hand).Add(shrine).Add(menu).Add(toolbarRep);
        var state = new GameState(store, LocationMap.Create(hand.Id, root.Id),
            ImmutableArray.Create(EyeCoreType, MenuType, ToolbarType, ClockType, ShrineType)) { Ui = UiLedger.DemoInitial };
        return new GameController(GameSession.New(state, ImmutableArray<Recipe>.Empty, TickMode.Rogue) with { Book = Book() });
    }

    [Fact]
    public void EnteringShrine_FiresShrineView()
    {
        var c = ShrineWorld(null);
        Assert.False(c.Session.Current.Ui.Has(ChromeElement.ShrineView));
        c.HandleKey(GameKey.Primary); // enter the shrine bag at cursor
        Assert.True(c.Session.Current.Ui.Has(ChromeElement.ShrineView));
        Assert.Equal("Shrine", c.Session.Current.ActiveBag.EnvironmentType);
    }

    [Fact]
    public void RestingOnClockSlot_FiresNoticeClock()
    {
        var c = ShrineWorld(null);
        c.HandleKey(GameKey.Primary);                       // enter shrine; cursor at (0,0) Menu
        Assert.False(c.Session.Current.Ui.Has(ChromeElement.ClockReadout));
        c.HandleKey(GameKey.Down);                           // → (1,0) the Clock slot
        Assert.True(c.Session.Current.Ui.Has(ChromeElement.ClockReadout));
    }

    [Fact]
    public void SlottingEyeCore_FiresCoreSlotted_PipsAndDialogue()
    {
        var c = ShrineWorld(EyeCore());
        c.HandleKey(GameKey.Primary);                        // enter shrine (hand keeps the core)
        c.HandleKey(GameKey.Down);                           // (0,0) → (1,0)
        c.HandleKey(GameKey.Right);                          // (1,0) → (1,1) eye slot
        Assert.False(c.Session.Current.Ui.Has(ChromeElement.FullnessPips));

        c.HandleKey(GameKey.Primary);                        // slot the core

        var state = c.Session.Current;
        Assert.True(state.Ui.Has(ChromeElement.FullnessPips));         // game-wide pips on
        Assert.True(state.CurrentCell.IsLockedFeatureSlot);           // slot locked
        Assert.Equal("Eye Core", state.CurrentCell.Stack!.ItemType.Name);
        Assert.Equal("slotted", state.Dialogue.ActiveBeatId);         // resolution beat fired
        Assert.False(state.HasItemsInHand);
    }

    [Fact]
    public void PeekingBagWhileCarrying_FiresCapacityBeat()
    {
        // A tiny world: a peekable pouch at cursor, an item already in hand.
        var pouch = new Bag(Grid.Create(2, 1), "Pouch");
        var rootGrid = Grid.Create(2, 1).SetCell(0, new Cell(new ItemStack(PouchType, 1, ContainedBagId: pouch.Id)));
        var root = new Bag(rootGrid);
        var hand = GameState.CreateHandBag(1);
        (hand, _) = hand.AcquireItems(new[] { new ItemStack(RockType, 1) });
        var store = BagStore.Empty.Add(root).Add(hand).Add(pouch);
        var state = new GameState(store, LocationMap.Create(hand.Id, root.Id),
            ImmutableArray.Create(PouchType, RockType)) { Ui = UiLedger.DemoInitial };
        var c = new GameController(GameSession.New(state, ImmutableArray<Recipe>.Empty, TickMode.Rogue) with { Book = Book() });

        c.HandleKey(GameKey.Peek);                            // look into the pouch while carrying
        Assert.Equal("capacity", c.Session.Current.Dialogue.ActiveBeatId);
    }

    [Fact]
    public void PeekingBagWhileEmptyHanded_DoesNotFireCapacityBeat()
    {
        var pouch = new Bag(Grid.Create(2, 1), "Pouch");
        var rootGrid = Grid.Create(2, 1).SetCell(0, new Cell(new ItemStack(PouchType, 1, ContainedBagId: pouch.Id)));
        var root = new Bag(rootGrid);
        var hand = GameState.CreateHandBag(1);
        var store = BagStore.Empty.Add(root).Add(hand).Add(pouch);
        var state = new GameState(store, LocationMap.Create(hand.Id, root.Id),
            ImmutableArray.Create(PouchType)) { Ui = UiLedger.DemoInitial };
        var c = new GameController(GameSession.New(state, ImmutableArray<Recipe>.Empty, TickMode.Rogue) with { Book = Book() });

        c.HandleKey(GameKey.Peek);
        Assert.Null(c.Session.Current.Dialogue.ActiveBeatId);
    }

    // ==================== View-model projection ====================

    [Fact]
    public void ViewModel_Pips_AbsentUntilFullnessChrome_ThenPresentOnBagCells()
    {
        var c = ShrineWorld(EyeCore());
        // Before slotting: no pip fields anywhere (fullness chrome off).
        var before = ViewModelSerializer.SerializeToString(c.Session);
        Assert.DoesNotContain("\"pip\"", before);

        c.HandleKey(GameKey.Primary);   // enter shrine
        c.HandleKey(GameKey.Down);
        c.HandleKey(GameKey.Right);     // eye slot
        c.HandleKey(GameKey.Primary);   // slot → FullnessPips on

        var after = ViewModelSerializer.Serialize(c.Session);
        // Active bag is the shrine: its Menu/Toolbar bag cells now carry pips (nested render surface).
        var cells = (System.Text.Json.Nodes.JsonArray)after["cells"]!;
        Assert.Equal("empty", (string)cells[1]!["pip"]!);   // empty Toolbar-rep bag
        // The clock cell is not a bag → no pip.
        Assert.Null(cells[2]!["pip"]);
    }

    [Fact]
    public void ViewModel_AlwaysProjectsClock()
    {
        var c = ShrineWorld(null);
        c.AdvanceClock(TimeSpan.FromSeconds(15));
        var vm = ViewModelSerializer.Serialize(c.Session);
        Assert.Equal(15000, (long)vm["clock"]!["elapsedMs"]!);
        Assert.Equal("00:15", (string)vm["clock"]!["readout"]!);
    }

    // ==================== Demo profile content ====================

    private static GameState DemoState() =>
        GameInitializer.CreateDemoProfile(ContentLoader.LoadFromDirectory(TestPaths.DataDir)).State;

    private static Bag? BagAt(GameState state, int cellIndex)
    {
        var stack = state.RootBag.Grid.GetCell(cellIndex).Stack;
        return stack?.ContainedBagId is { } id ? state.Store.GetById(id) : null;
    }

    [Fact]
    public void DemoProfile_SeedsShrine_WithThreeLockedSlotsAndOneEmptyEyeSlot()
    {
        var shrine = BagAt(DemoState(), 12);
        Assert.NotNull(shrine);
        Assert.Equal(GameState.ShrineEnvironment, shrine!.EnvironmentType);

        var slots = shrine.Grid.Cells.Select(c => c.Frame).OfType<FeatureSlotFrame>().ToList();
        Assert.Equal(4, slots.Count);
        Assert.Equal(3, slots.Count(s => s.IsLocked));                  // Menu/Toolbar/Clock pre-slotted
        var eye = slots.Single(s => s.Glyph == FeatureSlotFrame.EyeGlyph);
        Assert.False(eye.IsLocked);                                     // the empty eye slot
        Assert.True(shrine.Grid.Cells.Single(c => c.Frame is FeatureSlotFrame { Glyph: "eye" }).IsEmpty);
    }

    [Fact]
    public void DemoProfile_MenuBag_ContainsStartAndEsc()
    {
        var state = DemoState(); // one state so the bag ids line up
        var shrineBag = BagAt(state, 12)!;
        var menuBag = state.Store.GetById(shrineBag.Grid.Cells[0].Stack!.ContainedBagId!.Value)!;
        var names = menuBag.Grid.Cells.Where(c => !c.IsEmpty).Select(c => c.Stack!.ItemType.Name).ToList();
        Assert.Contains("Start", names);
        Assert.Contains("Esc", names);
    }

    [Fact]
    public void DemoProfile_SeedsEyeCore_InTheChest()
    {
        var chest = BagAt(DemoState(), 9)!;
        var core = chest.Grid.Cells.Select(c => c.Stack).FirstOrDefault(s => s?.ItemType.Name == "Eye Core");
        Assert.NotNull(core);
        Assert.Equal("eye", core!.GetString(FeatureSlotFrame.GlyphProperty));
    }
}
