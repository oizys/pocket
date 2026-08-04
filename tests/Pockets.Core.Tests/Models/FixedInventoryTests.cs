using System.Collections.Immutable;
using System.Text.Json.Nodes;
using Pockets.Core;
using Pockets.Core.Data;
using Pockets.Core.Models;
using Pockets.Core.Rendering;

namespace Pockets.Core.Tests.Models;

/// <summary>
/// Slice 3 — fixed inventories: the toolbar as a real depth-invariant bag, pickup-to-first-toolbar-slot
/// routing (demo profile only), and the bag-as-partial-empty recursive acquire (carrying capacity).
/// Mechanics are proven on hand-built states for precision; the shared demo profile is checked against
/// the real content registry.
/// </summary>
public class FixedInventoryTests
{
    private static readonly ItemType Rock = new("Rock", Category.Material, IsStackable: true, MaxStackSize: 20);
    private static readonly ItemType Wood = new("Wood", Category.Material, IsStackable: true, MaxStackSize: 20);
    private static readonly ItemType Grass = new("Grass", Category.Material, IsStackable: true, MaxStackSize: 20);
    private static readonly ItemType Bone = new("Bone", Category.Material, IsStackable: true, MaxStackSize: 20);
    private static readonly ItemType Pouch = new("Pouch", Category.Bag, IsStackable: false);

    private static readonly ImmutableArray<ItemType> AllTypes =
        ImmutableArray.Create(Rock, Wood, Grass, Bone, Pouch);

    /// <summary>Builds a state with a root (B), hand, and toolbar (T), plus any extra bags in the store.</summary>
    private static GameState MakeState(Bag root, Bag toolbar, bool toolbarPickup, params Bag[] extra)
    {
        var hand = GameState.CreateHandBag();
        var store = BagStore.Empty.Add(root).Add(hand).Add(toolbar).AddRange(extra);
        var locations = LocationMap.Create(hand.Id, root.Id)
            .Set(LocationId.T, Location.AtOrigin(toolbar.Id));
        return new GameState(store, locations, AllTypes)
        {
            ToolbarPickup = toolbarPickup,
            Ui = UiLedger.DemoInitial // grid only → toolbar chrome starts OFF so FirstPickup is observable
        };
    }

    /// <summary>A bag whose grid is a single row seeded from the given stacks (null = empty cell).</summary>
    private static Bag Row(string env, params ItemStack?[] cells)
    {
        var grid = Grid.Create(cells.Length, 1);
        for (var i = 0; i < cells.Length; i++)
            if (cells[i] is { } s) grid = grid.SetCell(i, new Cell(Stack: s));
        return new Bag(grid, env);
    }

    // ==================== AcquireIntoBagRecursive: ordering + recursion ====================

    [Fact]
    public void Acquire_FillsTrueEmptyCell_BeforePartialBag_EvenWhenBagIsEarlier()
    {
        // Slot 0 holds a non-full plain bag; slot 1 is a true empty. Ordering is empties-first, so the
        // item lands in the empty slot 1 — NOT inside the earlier bag.
        var inner = new Bag(Grid.Create(2, 1), "Pouch");
        var toolbar = Row("Toolbar", new ItemStack(Pouch, 1, ContainedBagId: inner.Id), null);
        var state = MakeState(new Bag(Grid.Create(1, 1)), toolbar, toolbarPickup: false, inner);

        var (after, unplaced) = state.AcquireIntoBagRecursive(toolbar.Id, new ItemStack(Rock, 1));

        Assert.Null(unplaced);
        Assert.Equal("Rock", after.Store.GetById(toolbar.Id)!.Grid.GetCell(1).Stack!.ItemType.Name);
        Assert.True(after.Store.GetById(inner.Id)!.Grid.Cells.All(c => c.IsEmpty)); // bag untouched
    }

    [Fact]
    public void Acquire_MergesIntoSameTypeStack_BeforeDescending()
    {
        var inner = new Bag(Grid.Create(2, 1), "Pouch");
        var toolbar = Row("Toolbar",
            new ItemStack(Rock, 5),
            new ItemStack(Pouch, 1, ContainedBagId: inner.Id));
        var state = MakeState(new Bag(Grid.Create(1, 1)), toolbar, toolbarPickup: false, inner);

        var (after, unplaced) = state.AcquireIntoBagRecursive(toolbar.Id, new ItemStack(Rock, 3));

        Assert.Null(unplaced);
        Assert.Equal(8, after.Store.GetById(toolbar.Id)!.Grid.GetCell(0).Stack!.Count); // merged 5+3
        Assert.True(after.Store.GetById(inner.Id)!.Grid.Cells.All(c => c.IsEmpty));
    }

    [Fact]
    public void Acquire_DescendsIntoPartialBag_WhenNoTopLevelSpace()
    {
        // Every plain slot is full with distinct types; the only space is the non-full bag in slot 3.
        var inner = new Bag(Grid.Create(2, 1), "Pouch");
        var toolbar = Row("Toolbar",
            new ItemStack(Rock, 1), new ItemStack(Wood, 1), new ItemStack(Grass, 1),
            new ItemStack(Pouch, 1, ContainedBagId: inner.Id));
        var state = MakeState(new Bag(Grid.Create(1, 1)), toolbar, toolbarPickup: false, inner);

        var (after, unplaced) = state.AcquireIntoBagRecursive(toolbar.Id, new ItemStack(Bone, 4));

        Assert.Null(unplaced);
        var innerAfter = after.Store.GetById(inner.Id)!;
        Assert.Equal("Bone", innerAfter.Grid.GetCell(0).Stack!.ItemType.Name); // destination cell inside the nested bag
        Assert.Equal(4, innerAfter.Grid.GetCell(0).Stack!.Count);
    }

    [Fact]
    public void Acquire_RecursesIntoBagWithinBag()
    {
        var deep = new Bag(Grid.Create(1, 1), "Deep");
        var mid = Row("Mid", new ItemStack(Rock, 1), new ItemStack(Pouch, 1, ContainedBagId: deep.Id));
        // Toolbar slot 0 full, slot 1 holds `mid` (whose only space is the deeper bag).
        var toolbar = Row("Toolbar", new ItemStack(Wood, 1), new ItemStack(Pouch, 1, ContainedBagId: mid.Id));
        var state = MakeState(new Bag(Grid.Create(1, 1)), toolbar, toolbarPickup: false, mid, deep);

        var (after, unplaced) = state.AcquireIntoBagRecursive(toolbar.Id, new ItemStack(Bone, 2));

        Assert.Null(unplaced);
        Assert.Equal("Bone", after.Store.GetById(deep.Id)!.Grid.GetCell(0).Stack!.ItemType.Name);
    }

    [Fact]
    public void Acquire_DoesNotDescendIntoFacilityBag()
    {
        var facility = new Bag(Grid.Create(2, 1), "Workshop", FacilityState: new FacilityState());
        var toolbar = Row("Toolbar", new ItemStack(Rock, 1), new ItemStack(Pouch, 1, ContainedBagId: facility.Id));
        var state = MakeState(new Bag(Grid.Create(1, 1)), toolbar, toolbarPickup: false, facility);

        var (after, unplaced) = state.AcquireIntoBagRecursive(toolbar.Id, new ItemStack(Bone, 1));

        Assert.NotNull(unplaced); // nowhere legal to place — the crafting station is not carrying space
        Assert.Equal(1, unplaced!.Count);
        Assert.True(after.Store.GetById(facility.Id)!.Grid.Cells.All(c => c.IsEmpty));
    }

    [Fact]
    public void Acquire_DoesNotDescendIntoWildernessBag()
    {
        var forest = new Bag(Grid.Create(2, 1), "Forest"); // IsWildernessType("Forest") == true
        var toolbar = Row("Toolbar", new ItemStack(Rock, 1), new ItemStack(Pouch, 1, ContainedBagId: forest.Id));
        var state = MakeState(new Bag(Grid.Create(1, 1)), toolbar, toolbarPickup: false, forest);

        var (_, unplaced) = state.AcquireIntoBagRecursive(toolbar.Id, new ItemStack(Bone, 1));

        Assert.NotNull(unplaced);
    }

    [Fact]
    public void Acquire_ReturnsUnplaced_WhenEverythingFull()
    {
        var toolbar = Row("Toolbar", new ItemStack(Rock, 1), new ItemStack(Wood, 1));
        var state = MakeState(new Bag(Grid.Create(1, 1)), toolbar, toolbarPickup: false);

        var (_, unplaced) = state.AcquireIntoBagRecursive(toolbar.Id, new ItemStack(Bone, 3));

        Assert.NotNull(unplaced);
        Assert.Equal(3, unplaced!.Count);
    }

    // ==================== Pickup routing (demo profile) ====================

    private static GameController DemoController(GameState state) =>
        new(GameSession.New(state));

    [Fact]
    public void DemoPickup_RoutesToFirstToolbarSlot_HandStaysEmpty()
    {
        var toolbar = new Bag(Grid.Create(4, 1), "Toolbar");
        var root = Row("Default", new ItemStack(Rock, 7));
        var ctrl = DemoController(MakeState(root, toolbar, toolbarPickup: true));

        ctrl.HandleKey(GameKey.Primary); // bare pickup at B(0,0)
        var s = ctrl.Session.Current;

        Assert.False(s.HasItemsInHand); // hand reserved
        var tb = s.Store.GetById(s.ToolbarBagId!.Value)!;
        Assert.Equal("Rock", tb.Grid.GetCell(0).Stack!.ItemType.Name);
        Assert.Equal(7, tb.Grid.GetCell(0).Stack!.Count);
        Assert.True(s.RootBag.Grid.GetCell(0).IsEmpty); // removed from the inventory
    }

    [Fact]
    public void DemoPickup_FillsSlotsLeftToRight()
    {
        var toolbar = new Bag(Grid.Create(4, 1), "Toolbar");
        var root = Row("Default", new ItemStack(Rock, 1), new ItemStack(Wood, 1), new ItemStack(Grass, 1));
        var ctrl = DemoController(MakeState(root, toolbar, toolbarPickup: true));

        ctrl.HandleKey(GameKey.Primary);          // Rock → slot 0
        ctrl.HandleKey(GameKey.Right);
        ctrl.HandleKey(GameKey.Primary);          // Wood → slot 1
        ctrl.HandleKey(GameKey.Right);
        ctrl.HandleKey(GameKey.Primary);          // Grass → slot 2

        var tb = ctrl.Session.Current.Store.GetById(ctrl.Session.Current.ToolbarBagId!.Value)!;
        Assert.Equal("Rock", tb.Grid.GetCell(0).Stack!.ItemType.Name);
        Assert.Equal("Wood", tb.Grid.GetCell(1).Stack!.ItemType.Name);
        Assert.Equal("Grass", tb.Grid.GetCell(2).Stack!.ItemType.Name);
    }

    [Fact]
    public void DemoPickup_FiresFirstPickup_LightingTheToolbarChrome()
    {
        var toolbar = new Bag(Grid.Create(4, 1), "Toolbar");
        var root = Row("Default", new ItemStack(Rock, 1));
        var state = MakeState(root, toolbar, toolbarPickup: true);
        Assert.False(state.Ui.Has(ChromeElement.Toolbar)); // absent before

        var ctrl = DemoController(state);
        ctrl.HandleKey(GameKey.Primary);

        Assert.True(ctrl.Session.Current.Ui.Has(ChromeElement.Toolbar)); // present after
    }

    [Fact]
    public void DemoPickup_OverflowsIntoNonFullToolbarBag()
    {
        var inner = new Bag(Grid.Create(2, 1), "Pouch");
        var toolbar = Row("Toolbar",
            new ItemStack(Rock, 1), new ItemStack(Wood, 1), new ItemStack(Grass, 1),
            new ItemStack(Pouch, 1, ContainedBagId: inner.Id));
        var root = Row("Default", new ItemStack(Bone, 2));
        var ctrl = DemoController(MakeState(root, toolbar, toolbarPickup: true, inner));

        ctrl.HandleKey(GameKey.Primary); // toolbar plain slots full → Bone acquires INTO the pouch

        var s = ctrl.Session.Current;
        Assert.False(s.HasItemsInHand);
        Assert.Equal("Bone", s.Store.GetById(inner.Id)!.Grid.GetCell(0).Stack!.ItemType.Name);
    }

    [Fact]
    public void DemoPickup_ConservesCensus()
    {
        var toolbar = new Bag(Grid.Create(4, 1), "Toolbar");
        var root = Row("Default", new ItemStack(Rock, 9));
        var ctrl = DemoController(MakeState(root, toolbar, toolbarPickup: true));

        var before = InvariantChecker.Census(ctrl.Session.Current);
        ctrl.HandleKey(GameKey.Primary);
        var after = InvariantChecker.Census(ctrl.Session.Current);

        Assert.Equal(before, after); // pickup moves the item B→toolbar; total is conserved
    }

    // ==================== Non-demo profile: unchanged grab-into-hand ====================

    [Fact]
    public void NonDemoPickup_GoesToHand_ToolbarUntouched()
    {
        var toolbar = new Bag(Grid.Create(4, 1), "Toolbar");
        var root = Row("Default", new ItemStack(Rock, 5));
        var ctrl = DemoController(MakeState(root, toolbar, toolbarPickup: false));

        ctrl.HandleKey(GameKey.Primary);
        var s = ctrl.Session.Current;

        Assert.True(s.HasItemsInHand); // classic grab-into-hand
        Assert.Equal("Rock", s.HandItems[0].ItemType.Name);
        Assert.True(s.Store.GetById(s.ToolbarBagId!.Value)!.Grid.Cells.All(c => c.IsEmpty));
    }

    [Fact]
    public void DemoGrabFromToolbarPanel_UsesHand_GrabForMoveStaysAvailable()
    {
        // Routing is B-only: focusing the toolbar and pressing Primary grabs into the hand (the cut buffer).
        var toolbar = Row("Toolbar", new ItemStack(Rock, 4));
        var root = new Bag(Grid.Create(1, 1));
        var ctrl = DemoController(MakeState(root, toolbar, toolbarPickup: true));
        ctrl.SetFocus(LocationId.T);

        ctrl.HandleKey(GameKey.Primary);
        var s = ctrl.Session.Current;

        Assert.True(s.HasItemsInHand);
        Assert.Equal("Rock", s.HandItems[0].ItemType.Name);
    }

    // ==================== Depth-invariance ====================

    [Fact]
    public void Toolbar_IsDepthInvariant_AcrossEnterAndLeave()
    {
        var toolbar = Row("Toolbar", new ItemStack(Rock, 3));
        var innerBag = new Bag(Grid.Create(2, 1), "Pouch");
        // Root holds an enterable plain bag at (0,0).
        var root = Row("Default", new ItemStack(Pouch, 1, ContainedBagId: innerBag.Id));
        var ctrl = DemoController(MakeState(root, toolbar, toolbarPickup: true, innerBag));

        string ToolbarJson() => ViewModelSerializer.Serialize(ctrl.Session)["toolbar"]!.ToJsonString();
        var atRoot = ToolbarJson();

        ctrl.HandleKey(GameKey.Primary); // enter the pouch (bag cell → EnterBag)
        Assert.True(ctrl.Session.Current.IsNested);
        Assert.Equal(atRoot, ToolbarJson()); // identical at depth 1

        ctrl.HandleKey(GameKey.LeaveBag);
        Assert.False(ctrl.Session.Current.IsNested);
        Assert.Equal(atRoot, ToolbarJson()); // and back at depth 0
    }

    // ==================== Serializer shape ====================

    [Fact]
    public void Serializer_EmitsToolbarSlots_WithNestedContents()
    {
        var inner = new Bag(Grid.Create(2, 1), "Pouch").AcquireItems(new[] { new ItemStack(Bone, 2) }).UpdatedBag;
        var toolbar = Row("Toolbar", new ItemStack(Rock, 5), null, null,
            new ItemStack(Pouch, 1, ContainedBagId: inner.Id));
        var state = MakeState(new Bag(Grid.Create(1, 1)), toolbar, toolbarPickup: true, inner);

        var tb = (JsonArray)ViewModelSerializer.Serialize(GameSession.New(state))["toolbar"]!;

        Assert.Equal(2, tb.Count); // only occupied slots (0 and 3)
        Assert.Equal(0, tb[0]!["slot"]!.GetValue<int>());
        Assert.Equal("Rock", tb[0]!["item"]!.GetValue<string>());
        Assert.Equal(3, tb[1]!["slot"]!.GetValue<int>());
        Assert.True(tb[1]!["hasBag"]!.GetValue<bool>());
        Assert.Equal("Bone", tb[1]!["contents"]![0]!["item"]!.GetValue<string>());
    }

    // ==================== Shared demo profile ====================

    private static DemoProfile Demo() =>
        GameInitializer.CreateDemoProfile(ContentLoader.LoadFromDirectory(TestPaths.DataDir));

    [Fact]
    public void DemoProfile_ToolbarIsSingleRowOfFour_WithSeededCarryingBagInLastSlot()
    {
        var state = Demo().State;
        var toolbar = state.Store.GetById(state.ToolbarBagId!.Value)!;

        Assert.Equal(4, toolbar.Grid.Columns);
        Assert.Equal(1, toolbar.Grid.Rows);
        Assert.True(toolbar.Grid.GetCell(0).IsEmpty);
        Assert.True(toolbar.Grid.GetCell(1).IsEmpty);
        Assert.True(toolbar.Grid.GetCell(2).IsEmpty);

        var pouchCell = toolbar.Grid.GetCell(3);
        Assert.Equal("Coin Pouch", pouchCell.Stack!.ItemType.Name);
        Assert.True(pouchCell.HasBag);
        Assert.True(GameState.IsDescendableBag(state.Store.GetById(pouchCell.Stack.ContainedBagId!.Value)!));
    }

    [Fact]
    public void DemoProfile_EnablesToolbarPickup()
    {
        Assert.True(Demo().State.ToolbarPickup);
    }

    [Fact]
    public void FormatToolbarSummary_ListsSlots_WithNestedCapacity()
    {
        var inner = new Bag(Grid.Create(2, 1), "Pouch").AcquireItems(new[] { new ItemStack(Bone, 1) }).UpdatedBag;
        var toolbar = Row("Toolbar", new ItemStack(Rock, 5), new ItemStack(Pouch, 1, ContainedBagId: inner.Id));
        var state = MakeState(new Bag(Grid.Create(1, 1)), toolbar, toolbarPickup: true, inner);

        var summary = RenderHelpers.FormatToolbarSummary(state);

        Assert.Contains("ROCK", summary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("[1/2]", summary); // Coin-Pouch-style nested capacity readout
    }

    [Fact]
    public void NonDemoProfile_KeepsClassicToolbar_AndNoToolbarPickup()
    {
        // CreateStage1 is the non-demo substrate — its toolbar and routing are unchanged.
        var state = GameState.CreateStage1(AllTypes, new[] { new ItemStack(Rock, 3) });
        var toolbar = state.Store.GetById(state.ToolbarBagId!.Value)!;

        Assert.False(state.ToolbarPickup);
        Assert.Equal(10, toolbar.Grid.Columns);
        Assert.Equal(1, toolbar.Grid.Rows);
    }
}
