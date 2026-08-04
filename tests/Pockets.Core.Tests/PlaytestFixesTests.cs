using System;
using System.Collections.Immutable;
using System.Linq;
using Pockets.Core;
using Pockets.Core.Data;
using Pockets.Core.Models;
using Pockets.Core.Rendering;
using Xunit;

namespace Pockets.Core.Tests;

/// <summary>
/// Regression coverage for the 2026-08-04 playtest fix batch (from Aaron's first target-demo run):
///   1. recipe-switch never destroys items (the item-deletion bug),
///   2. Primary/E on a toolbar bag peeks instead of wedging the UI,
///   3. recipe cards are consumed (poof) on learn,
///   5. the modal recipe menu (open / navigate / select / close) replacing R-to-cycle.
/// </summary>
public class PlaytestFixesTests
{
    private static readonly ItemType Grass = new("Dry Grass", Category.Material, IsStackable: true);
    private static readonly ItemType Bone = new("Bone Chips", Category.Material, IsStackable: true);
    private static readonly ItemType Rock = new("Plain Rock", Category.Material, IsStackable: true);
    private static readonly ItemType Wood = new("Rough Wood", Category.Material, IsStackable: true);
    private static readonly ItemType TableType = new("Crafting Table", Category.Structure, IsStackable: false);

    private static Recipe R(string id, params RecipeInput[] ins) =>
        new(id, id, ins, () => RecipeOutput.FromStacks(Array.Empty<ItemStack>()), Duration: 3);

    // ============================================================
    // Item 1 — recipe switch conserves items (never destroys)
    // ============================================================

    /// <summary>A filtered-slot Workbench-style facility loaded with r1's inputs, in a root with the given
    /// number of free cells. Two recipes are known/mapped so a cycle switches to a genuinely different one.</summary>
    private static (GameSession Session, Guid TableId) FilteredFacilityWorld(int rootFreeCells)
    {
        var r1 = R("r1", new RecipeInput(Grass, 3), new RecipeInput(Bone, 2));
        var r2 = R("r2", new RecipeInput(Wood, 1));
        var grid = Grid.Create(3, 1)
            .SetCell(0, new Cell(new ItemStack(Grass, 3), Frame: new InputSlotFrame("in1", ItemTypeFilter: Grass)))
            .SetCell(1, new Cell(new ItemStack(Bone, 2), Frame: new InputSlotFrame("in2", ItemTypeFilter: Bone)))
            .SetCell(2, new Cell(Frame: new OutputSlotFrame("out1")));
        var table = new Bag(grid, "Workbench", "Brown", FacilityState: new FacilityState(ActiveRecipeId: "r1"));

        // Root: cell 0 holds the table; the rest are free up to rootFreeCells.
        var rootGrid = Grid.Create(1 + rootFreeCells, 1)
            .SetCell(0, new Cell(new ItemStack(TableType, 1, ContainedBagId: table.Id)));
        var root = new Bag(rootGrid);
        var hand = GameState.CreateHandBag(1);
        var store = BagStore.Empty.Add(root).Add(hand).Add(table);
        var map = ImmutableDictionary<string, ImmutableArray<string>>.Empty
            .Add("Workbench", ImmutableArray.Create("r1", "r2"));
        var state = new GameState(store, LocationMap.Create(hand.Id, root.Id),
            ImmutableArray.Create(Grass, Bone, Rock, Wood, TableType));
        var session = GameSession.New(state, ImmutableArray.Create(r1, r2), map, TickMode.Realtime);
        // Enter the table so it is the active bag for ExecuteCycleRecipe.
        return (session with { Current = session.Current.EnterBag().State }, table.Id);
    }

    [Fact]
    public void CycleRecipe_WithRoomInRoot_ConservesItems_AndSwitches()
    {
        var (session, tableId) = FilteredFacilityWorld(rootFreeCells: 4);
        var before = InvariantChecker.Census(session.Current);

        var after = session.ExecuteCycleRecipe();

        Assert.Empty(InvariantChecker.CensusDelta(before, InvariantChecker.Census(after.Current))); // nothing lost
        Assert.Equal("r2", after.Current.Store.GetById(tableId)!.FacilityState!.ActiveRecipeId);      // switched
    }

    [Fact]
    public void CycleRecipe_WithFullRoot_NeverDestroysItems_RefusesInstead()
    {
        // The exact deletion repro: a full root (no free cell) used to make CycleRecipe discard the slot
        // items. Now the switch is REFUSED and every item is conserved — no silent destruction.
        var (session, tableId) = FilteredFacilityWorld(rootFreeCells: 0);
        var before = InvariantChecker.Census(session.Current);

        var after = session.ExecuteCycleRecipe();

        Assert.Empty(InvariantChecker.CensusDelta(before, InvariantChecker.Census(after.Current)));
        Assert.Equal("r1", after.Current.Store.GetById(tableId)!.FacilityState!.ActiveRecipeId); // unchanged (refused)
        Assert.Contains(after.ActionLog, l => l.Contains("no room"));
    }

    [Fact]
    public void SetRecipeOnGenericTable_KeepsLoadedItems_AndConserves()
    {
        // The Crafting Table's generic-slot SetRecipe path keeps the loaded ingredients in place and only
        // pins the recipe — so it can never destroy anything (conservation is trivial), even with a full root.
        var grid = Grid.Create(3, 1)
            .SetCell(0, new Cell(new ItemStack(Grass, 3), Frame: new InputSlotFrame("in1")))
            .SetCell(1, new Cell(new ItemStack(Bone, 2), Frame: new InputSlotFrame("in2")))
            .SetCell(2, new Cell(Frame: new OutputSlotFrame("out1")));
        var table = new Bag(grid, GameState.CraftingTableEnvironment, "Brown",
            FacilityState: new FacilityState(RequiresSelectedRecipe: true));
        var rootGrid = Grid.Create(1, 1).SetCell(0, new Cell(new ItemStack(TableType, 1, ContainedBagId: table.Id)));
        var root = new Bag(rootGrid);
        var hand = GameState.CreateHandBag(1);
        var store = BagStore.Empty.Add(root).Add(hand).Add(table);
        var state = new GameState(store, LocationMap.Create(hand.Id, root.Id),
            ImmutableArray.Create(Grass, Bone, TableType))
        { KnownRecipes = ImmutableHashSet.Create("compass") };
        var r = R("compass", new RecipeInput(Grass, 3), new RecipeInput(Bone, 2));
        var session = GameSession.New(state, ImmutableArray.Create(r), TickMode.Realtime);
        session = session with { Current = session.Current.EnterBag().State };
        var before = InvariantChecker.Census(session.Current);

        var c = new GameController(session);
        c.HandleKey(GameKey.RecipeMenu);   // open modal
        c.HandleKey(GameKey.Primary);      // select the single recipe

        Assert.Empty(InvariantChecker.CensusDelta(before, InvariantChecker.Census(c.Session.Current)));
        var t = c.Session.Current.Store.GetById(table.Id)!;
        Assert.Equal("compass", t.FacilityState!.ActiveRecipeId);
        Assert.Equal(3, t.Grid.GetCell(0).Stack!.Count); // ingredients untouched
        Assert.Equal(2, t.Grid.GetCell(1).Stack!.Count);
    }

    // ============================================================
    // Item 2 — toolbar bag peeks, never wedges
    // ============================================================

    private static GameController DemoController(params string[] known)
    {
        var profile = GameInitializer.CreateDemoProfile(ContentLoader.LoadFromDirectory(TestPaths.DataDir));
        var k = profile.State.KnownRecipes;
        foreach (var id in known) k = k.Add(id);
        return new GameController((profile with { State = profile.State with { KnownRecipes = k } }).NewSession());
    }

    [Fact]
    public void ToolbarBag_Primary_Peeks_DoesNotEnter_AndClosesCleanly()
    {
        var c = DemoController();
        var toolbarId = c.Session.Current.ToolbarBagId!.Value;
        var depthBefore = c.Session.Current.Locations.TryGet(LocationId.T)!.Breadcrumbs.Count();

        c.SetFocus(LocationId.T);
        for (int i = 0; i < 3; i++) c.HandleKey(GameKey.Right); // cursor to the Coin Pouch in slot 3

        c.HandleKey(GameKey.Primary); // should PEEK, not enter

        // A C look-in panel opened over the Coin Pouch; focus moved to C.
        Assert.True(c.Session.Current.Locations.Has(LocationId.C));
        Assert.Equal(LocationId.C, c.Focus);
        // The toolbar was NOT entered — its breadcrumb depth is unchanged (no wedge).
        Assert.Equal(depthBefore, c.Session.Current.Locations.TryGet(LocationId.T)!.Breadcrumbs.Count());

        // And it closes cleanly (Q), returning focus to B — the wedge is gone.
        c.HandleKey(GameKey.LeaveBag);
        Assert.False(c.Session.Current.Locations.Has(LocationId.C));
        Assert.Equal(LocationId.B, c.Focus);
    }

    // ============================================================
    // Item 3 — recipe cards consumed on learn
    // ============================================================

    [Fact]
    public void PickingUpARecipeCard_LearnsIt_AndConsumesTheCard()
    {
        var c = DemoController();
        // Dismiss the opening dialogue so keys reach the grid.
        while (c.Session.Current.Dialogue.IsActive) c.HandleKey(GameKey.Primary);

        // Move to the compass recipe card (home cell 21 = row 2, col 5) and pick it up.
        for (int i = 0; i < 2; i++) c.HandleKey(GameKey.Down);
        for (int i = 0; i < 5; i++) c.HandleKey(GameKey.Right);
        Assert.Equal("Compass Recipe", c.Session.Current.CurrentCell.Stack?.ItemType.Name);
        var compassCardsBefore = CountItem(c, "Compass Recipe");

        c.HandleKey(GameKey.Primary); // pickup → learn → poof

        Assert.Contains(GameInitializer.CompassRecipeId, c.Session.Current.KnownRecipes);
        Assert.Equal(compassCardsBefore - 1, CountItem(c, "Compass Recipe")); // the card is gone
        Assert.Equal(0, CountItem(c, "Compass Recipe"));                       // none left anywhere
    }

    private static int CountItem(GameController c, string name) =>
        c.Session.Current.Store.All.Sum(b => b.Grid.Cells
            .Where(cell => cell.Stack?.ItemType.Name == name).Sum(cell => cell.Stack!.Count));

    // ============================================================
    // Item 5 — modal recipe menu
    // ============================================================

    /// <summary>Demo controller with the table opened as a focused C look-in panel (Primary on the facility
    /// cell). The modal recipe menu can be opened on a facility whether it is entered (B) or peeked (C).</summary>
    private static (GameController Controller, Guid TableId) FocusedEmptyTable(params string[] known)
    {
        var c = DemoController(known);
        var tableId = c.Session.Current.Store.All
            .First(b => b.EnvironmentType == GameState.CraftingTableEnvironment).Id;
        var owner = c.Session.Current.Store.GetOwnerOf(tableId)!;
        int cols = c.Session.Current.RootBag.Grid.Columns;
        int col = owner.CellIndex % cols, row = owner.CellIndex / cols;
        while (c.Session.Current.Dialogue.IsActive) c.HandleKey(GameKey.Primary);
        for (int i = 0; i < row; i++) c.HandleKey(GameKey.Down);
        for (int i = 0; i < col; i++) c.HandleKey(GameKey.Right);
        c.HandleKey(GameKey.Primary); // Primary on a facility opens it as a C look-in (focus → C)
        return (c, tableId);
    }

    [Fact]
    public void RecipeMenu_Open_ListsKnownCraftableRecipes()
    {
        var c = FocusedEmptyTable(GameInitializer.CompassRecipeId, GameInitializer.BeltPouchRecipeId).Controller;
        // The table opened as a C panel; open the recipe menu on the focused facility.
        c.HandleKey(GameKey.RecipeMenu);

        Assert.NotNull(c.Session.RecipeMenu);
        var ids = c.Session.RecipeMenu!.RecipeIds;
        Assert.Contains(GameInitializer.CompassRecipeId, ids);
        Assert.Contains(GameInitializer.BeltPouchRecipeId, ids);
        Assert.DoesNotContain(GameInitializer.AnotherQuiet1RecipeId, ids); // not known → not listed
    }

    [Fact]
    public void RecipeMenu_NavigateSelect_SetsFacilityRecipe_AndCloses()
    {
        var (c, tableId) = FocusedEmptyTable(GameInitializer.CompassRecipeId, GameInitializer.BeltPouchRecipeId);
        c.HandleKey(GameKey.RecipeMenu);
        var ids = c.Session.RecipeMenu!.RecipeIds;

        c.HandleKey(GameKey.Down);    // move selection to index 1
        Assert.Equal(1, c.Session.RecipeMenu!.SelectedIndex);
        c.HandleKey(GameKey.Confirm); // select

        Assert.Null(c.Session.RecipeMenu); // closed
        Assert.Equal(ids[1], c.Session.Current.Store.GetById(tableId)!.FacilityState!.ActiveRecipeId);
    }

    [Fact]
    public void RecipeMenu_Esc_Closes_WithoutSettingARecipe()
    {
        var (c, tableId) = FocusedEmptyTable(GameInitializer.CompassRecipeId);
        c.HandleKey(GameKey.RecipeMenu);
        Assert.NotNull(c.Session.RecipeMenu);

        c.HandleKey(GameKey.Cancel); // Esc

        Assert.Null(c.Session.RecipeMenu);
        Assert.Null(c.Session.Current.Store.GetById(tableId)!.FacilityState!.ActiveRecipeId);
    }

    [Fact]
    public void RecipeMenu_ProjectedIntoViewModel()
    {
        var c = FocusedEmptyTable(GameInitializer.CompassRecipeId).Controller;
        Assert.Null(ViewModelSerializer.Serialize(c.Session)["recipeMenu"]); // closed → null

        c.HandleKey(GameKey.RecipeMenu);
        var menu = ViewModelSerializer.Serialize(c.Session)["recipeMenu"]!;
        Assert.True(menu["open"]!.GetValue<bool>());
        Assert.Equal("Crafting Table", menu["facility"]!.GetValue<string>());
        Assert.NotEmpty(menu["recipes"]!.AsArray());
    }
}
