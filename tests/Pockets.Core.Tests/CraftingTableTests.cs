using System;
using System.Collections.Immutable;
using System.Linq;
using System.Text.Json.Nodes;
using Pockets.Core;
using Pockets.Core.Data;
using Pockets.Core.Models;
using Pockets.Core.Rendering;
using Xunit;

namespace Pockets.Core.Tests;

/// <summary>
/// Slice-7 integration: the Crafting Table's clock-driven timed craft (no per-key drift), the
/// FirstTimedAction → ActionQueue and CompassCrafted → Minimap triggers, the KnownRecipes → craftable-set
/// wiring (including the known-but-uncraftable demo axe), the explorer/organizer OutputFactory bags, and
/// the minimap zones-reached state.
/// </summary>
public class CraftingTableTests
{
    private static readonly ItemType Grass = new("Dry Grass", Category.Material, IsStackable: true);
    private static readonly ItemType Bone = new("Bone Chips", Category.Material, IsStackable: true);
    private static readonly ItemType Compass = new("Quiet Compass", Category.Tool, IsStackable: false);
    private static readonly ItemType TableType = new("Crafting Table", Category.Structure, IsStackable: false);

    /// <summary>The demo compass recipe: Dry Grass ×3 + Bone Chips ×2 → Quiet Compass over 3 ticks.</summary>
    private static Recipe CompassRecipe() => new(
        "compass", "Quiet Compass",
        new[] { new RecipeInput(Grass, 3), new RecipeInput(Bone, 2) },
        () => RecipeOutput.FromStacks(new[] { new ItemStack(Compass, 1) }),
        Duration: 3);

    /// <summary>
    /// A 2×1 root whose cell 0 holds a Crafting Table pre-loaded with Dry Grass ×3 + Bone Chips ×2 and
    /// pinned to the compass recipe. Realtime mode (crafting is clock-driven). The recipe is known only
    /// when <paramref name="compassKnown"/> — the KnownRecipes gate on the assembler's craftable set.
    /// </summary>
    private static (GameController Controller, Guid TableId) CompassWorld(bool compassKnown)
    {
        var tableGrid = Grid.Create(3, 1)
            .SetCell(0, new Cell(new ItemStack(Grass, 3), Frame: new InputSlotFrame("in1", ItemTypeFilter: Grass)))
            .SetCell(1, new Cell(new ItemStack(Bone, 2), Frame: new InputSlotFrame("in2", ItemTypeFilter: Bone)))
            .SetCell(2, new Cell(Frame: new OutputSlotFrame("out1")));
        var table = new Bag(tableGrid, GameState.CraftingTableEnvironment, "Brown",
            FacilityState: new FacilityState(ActiveRecipeId: "compass"));

        var rootGrid = Grid.Create(2, 1)
            .SetCell(0, new Cell(new ItemStack(TableType, 1, ContainedBagId: table.Id)));
        var root = new Bag(rootGrid);
        var hand = GameState.CreateHandBag(1);

        var store = BagStore.Empty.Add(root).Add(hand).Add(table);
        var state = new GameState(store, LocationMap.Create(hand.Id, root.Id),
            ImmutableArray.Create(Grass, Bone, Compass, TableType)) { Ui = UiLedger.DemoInitial };
        if (compassKnown)
            state = state with { KnownRecipes = state.KnownRecipes.Add("compass") };

        var session = GameSession.New(state, ImmutableArray.Create(CompassRecipe()), TickMode.Realtime);
        return (new GameController(session), table.Id);
    }

    private static Bag Table(GameController c, Guid id) => c.Session.Current.Store.GetById(id)!;
    private static int Progress(GameController c, Guid id) =>
        c.Session.Current.Store.GetOwnerOf(id) is { } o &&
        c.Session.Current.Store.GetById(o.ParentBagId) is { } parent
            ? parent.Grid.GetCell(o.CellIndex).Stack?.GetInt("Progress") ?? 0
            : 0;
    private static bool HasItem(GameController c, string name) =>
        c.Session.Current.Store.All.Any(b => b.Grid.Cells.Any(cell => cell.Stack?.ItemType.Name == name));

    // ---- Clock-driven craft: no drift, advances only on advanceTime ----

    [Fact]
    public void KeyPresses_DoNotAdvanceCraft_OnlyAdvanceTimeDoes()
    {
        var (c, table) = CompassWorld(compassKnown: true);

        // Plenty of key presses, no time advance: the craft never starts (no drift).
        for (int i = 0; i < 10; i++) c.HandleKey(GameKey.Right);
        Assert.Null(Table(c, table).FacilityState!.RecipeId);
        Assert.Equal(0, Progress(c, table));
        Assert.False(c.Session.Current.Ui.Has(ChromeElement.ActionQueue));

        // One whole-second advance starts the craft (progress 1) and reveals the ActionQueue.
        c.AdvanceClock(TimeSpan.FromSeconds(1));
        Assert.Equal("compass", Table(c, table).FacilityState!.RecipeId);
        Assert.Equal(1, Progress(c, table));
        Assert.True(c.Session.Current.Ui.Has(ChromeElement.ActionQueue));

        // A key press mid-craft still does not advance it.
        c.HandleKey(GameKey.Left);
        Assert.Equal(1, Progress(c, table));
    }

    [Fact]
    public void SubSecondAdvances_AccumulateToWholeTicks()
    {
        var (c, table) = CompassWorld(compassKnown: true);

        // 600ms + 600ms crosses exactly one whole-second boundary → exactly one tick.
        c.AdvanceClock(TimeSpan.FromMilliseconds(600));
        Assert.Equal(0, Progress(c, table)); // no boundary crossed yet
        c.AdvanceClock(TimeSpan.FromMilliseconds(600));
        Assert.Equal(1, Progress(c, table)); // crossed 1000ms → one tick
    }

    [Fact]
    public void Compass_Completes_AfterDurationTicks_AndFiresMinimap()
    {
        var (c, table) = CompassWorld(compassKnown: true);
        Assert.False(c.Session.Current.Ui.Has(ChromeElement.Minimap));

        c.AdvanceClock(TimeSpan.FromSeconds(3)); // Duration 3 → complete
        Assert.False(HasItem(c, "Dry Grass"));   // inputs consumed
        Assert.False(HasItem(c, "Bone Chips"));
        Assert.True(HasItem(c, "Quiet Compass")); // output produced
        Assert.Null(Table(c, table).FacilityState!.RecipeId);
        Assert.True(c.Session.Current.Ui.Has(ChromeElement.Minimap)); // CompassCrafted → Minimap
    }

    [Fact]
    public void UnknownRecipe_TableStaysIdle_NoDriftNoOutput()
    {
        var (c, table) = CompassWorld(compassKnown: false);

        c.AdvanceClock(TimeSpan.FromSeconds(10));
        Assert.Null(Table(c, table).FacilityState!.RecipeId);
        Assert.Equal(0, Progress(c, table));
        Assert.False(HasItem(c, "Quiet Compass"));
        Assert.False(c.Session.Current.Ui.Has(ChromeElement.ActionQueue));
        Assert.False(c.Session.Current.Ui.Has(ChromeElement.Minimap));
    }

    // ---- The three-path cliff at the demo profile ----

    private static DemoProfile DemoAllKnown()
    {
        var profile = GameInitializer.CreateDemoProfile(ContentLoader.LoadFromDirectory(TestPaths.DataDir));
        // Learn every cliff recipe up-front (the journey does this by picking up the cards).
        var known = profile.State.KnownRecipes
            .Add(GameInitializer.CompassRecipeId)
            .Add(GameInitializer.AnotherQuiet1RecipeId)
            .Add(GameInitializer.BeltPouchRecipeId)
            .Add(GameInitializer.DemoAxeRecipeId);
        return profile with { State = profile.State with { KnownRecipes = known } };
    }

    [Fact]
    public void DemoTables_CraftCompass_Wilderness_AndPouch_ButNeverTheAxe()
    {
        var c = new GameController(DemoAllKnown().NewSession());

        c.AdvanceClock(TimeSpan.FromSeconds(3)); // all three loaded+known tables complete

        Assert.True(HasItem(c, "Quiet Compass"));        // gatherer→compass headline
        Assert.True(HasItem(c, "Belt Pouch"));           // organizer path: a new bag
        // Explorer path: a freshly-crafted EnterOnly Quiet 1 wilderness bag now exists.
        var craftedWilds = c.Session.Current.Store.All
            .Count(b => b.EnvironmentType == GameInitializer.WildernessEnvironment && b.EnterOnly);
        Assert.True(craftedWilds >= 2); // the original home Quiet 1 + at least one crafted

        // Gatherer path deliberately out of reach: the axe recipe is KNOWN but priced in Iron Ore that
        // never appears down here, so the craftable-set gate never admits it — no Stone Axe is produced.
        Assert.Contains(GameInitializer.DemoAxeRecipeId, c.Session.Current.KnownRecipes);
        Assert.False(HasItem(c, "Stone Axe"));
    }

    [Fact]
    public void CraftedQuiet1_IsEnterable_AndLightsASecondWedge()
    {
        var c = new GameController(DemoAllKnown().NewSession());
        c.AdvanceClock(TimeSpan.FromSeconds(3));

        // Find the crafted wilderness (the one NOT reachable from the home root cell 10).
        var homeWildId = c.Session.Current.RootBag.Grid.GetCell(10).Stack!.ContainedBagId!.Value;
        var crafted = c.Session.Current.Store.All.First(b =>
            b.EnvironmentType == GameInitializer.WildernessEnvironment && b.EnterOnly && b.Id != homeWildId);

        Assert.True(crafted.EnterOnly);
        Assert.Equal(GameInitializer.WildernessPalette, crafted.ColorScheme);
        Assert.Equal(GameInitializer.WildernessGlyph, crafted.Glyph);
    }

    // ---- Minimap zones: entering lights a wedge, crafting does not ----

    [Fact]
    public void EnteringWilderness_LightsWedge_ReentryDoesNot()
    {
        var wildGrid = Grid.Create(2, 1).SetCell(0, new Cell(new ItemStack(Grass, 1)));
        var wild = new Bag(wildGrid, GameInitializer.WildernessEnvironment, "Dust") { EnterOnly = true };
        var wildType = new ItemType("Quiet 1", Category.Bag, IsStackable: false);
        var rootGrid = Grid.Create(2, 1).SetCell(0, new Cell(new ItemStack(wildType, 1, ContainedBagId: wild.Id)));
        var root = new Bag(rootGrid);
        var hand = GameState.CreateHandBag(1);
        var store = BagStore.Empty.Add(root).Add(hand).Add(wild);
        var state = new GameState(store, LocationMap.Create(hand.Id, root.Id),
            ImmutableArray.Create(Grass, wildType)) { Ui = UiLedger.DemoInitial };
        var c = new GameController(GameSession.New(state, ImmutableArray<Recipe>.Empty, TickMode.Realtime));

        Assert.Empty(c.Session.Current.ZonesReached);

        c.HandleKey(GameKey.Primary); // enter the wilderness (breadcrumb push)
        Assert.True(c.Session.Current.IsNested);
        Assert.Single(c.Session.Current.ZonesReached);

        c.HandleKey(GameKey.LeaveBag);
        c.HandleKey(GameKey.Primary); // re-enter the SAME wilderness — no new wedge
        Assert.Single(c.Session.Current.ZonesReached);
    }

    // ---- View-model projections ----

    [Fact]
    public void ViewModel_ProjectsActionQueueRow_WhileCrafting()
    {
        var (c, _) = CompassWorld(compassKnown: true);
        c.AdvanceClock(TimeSpan.FromSeconds(1)); // start crafting

        var vm = ViewModelSerializer.Serialize(c.Session);
        var queue = vm["actionQueue"]!.AsArray();
        Assert.Single(queue);
        Assert.Equal("Crafting Table", queue[0]!["facility"]!.GetValue<string>());
        Assert.Equal("compass", queue[0]!["recipe"]!.GetValue<string>());
        Assert.Equal(1, queue[0]!["progress"]!.GetValue<int>());
        Assert.Equal(3, queue[0]!["duration"]!.GetValue<int>());
    }

    [Fact]
    public void ViewModel_MinimapWedgeCount_TracksZonesReached()
    {
        var (c, _) = CompassWorld(compassKnown: true);
        var vm = ViewModelSerializer.Serialize(c.Session);
        var minimap = vm["minimap"]!.AsObject();
        Assert.True(minimap["core"]!.GetValue<bool>());
        Assert.Equal(12, minimap["wedges"]!.GetValue<int>());
        Assert.Equal(0, minimap["litCount"]!.GetValue<int>());
        Assert.Empty(minimap["lit"]!.AsArray());
    }
}
