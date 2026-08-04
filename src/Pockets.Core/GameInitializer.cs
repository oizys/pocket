using System.Collections.Immutable;
using Pockets.Core.Data;
using Pockets.Core.Models;

namespace Pockets.Core;

/// <summary>
/// A fully-specified starting point both frontends load identically: the initial state,
/// its recipe set + facility→recipe map, and the tick mode. Produced by
/// <see cref="GameInitializer.CreateDemoProfile"/> from a fixed seed so the TUI and Godot
/// builds begin in byte-identical game state — the parity baseline for the journey runner.
/// </summary>
public record DemoProfile(
    GameState State,
    ImmutableArray<Recipe> Recipes,
    ImmutableDictionary<string, ImmutableArray<string>> FacilityRecipeMap,
    TickMode TickMode,
    DialogueBook Dialogue)
{
    /// <summary>
    /// Builds the session both frontends run. Centralizing this guarantees the tick mode
    /// (and thus per-action tick semantics) and the dialogue beat book are identical across
    /// TUI and Godot.
    /// </summary>
    public GameSession NewSession() =>
        GameSession.New(State, Recipes, FacilityRecipeMap, TickMode) with { Book = Dialogue };
}

/// <summary>
/// Creates initial game states with random item placement.
/// All created bags are registered in the BagStore.
/// </summary>
public static class GameInitializer
{
    /// <summary>
    /// Fixed seed for the demo profile. Date-derived (2026-08-03) and pinned so both
    /// frontends — and repeated runner passes — produce identical starting states.
    /// </summary>
    public const int DemoSeed = 20260803;

    /// <summary>The demo's opening beat id — seeded active at frame 0 (journey 0:00).</summary>
    public const string OpeningBeatId = "opening";

    /// <summary>
    /// The shared demo-profile initializer used by BOTH the TUI and Godot builds.
    /// Wraps <see cref="CreateFromRegistry"/> with a fixed RNG seed and pins
    /// <see cref="TickMode.Rogue"/> (deterministic per-action ticks, no wall-clock timer),
    /// reconciling the two previously-divergent init paths (unseeded RNG on both;
    /// TUI defaulted to Realtime while Godot pinned Rogue). See design/parity-drift-report.md.
    ///
    /// When a <paramref name="dialogue"/> book with the opening beat is supplied (the real
    /// frontends + journey runner always pass it), the profile starts at journey frame 0:
    /// dialogue-box only (<see cref="UiLedger.DemoFrameZero"/>, grid ledger-off) with the opening
    /// beat active — dismissing it fires the grid's materialization. Without the book (bare
    /// determinism/serializer tests) it starts grid-on (<see cref="UiLedger.DemoInitial"/>).
    /// </summary>
    public static DemoProfile CreateDemoProfile(
        ContentRegistry registry, int? seed = null, DialogueBook? dialogue = null)
    {
        var rng = new Random(seed ?? DemoSeed);
        var (state, recipes) = CreateFromRegistry(registry, rng);
        var facilityRecipeMap = registry.BuildFacilityRecipeMap();
        var book = dialogue ?? DialogueBook.Empty;

        state = WithDemoLedgerFixtures(state);
        state = WithDemoToolbar(state) with { ToolbarPickup = true };
        state = WithDemoSlice4Bags(state);
        state = WithDemoWilderness(state);
        state = WithDemoShrine(state);

        // Frame 0 (journey 0:00): the dialogue box alone, world not yet materialized. Only applied
        // when the opening beat is available so bookless callers keep the grid-on Slice-1 baseline.
        if (book.Get(OpeningBeatId) is not null)
        {
            state = state with
            {
                Ui = UiLedger.DemoFrameZero,
                Dialogue = DialogueState.Empty.Enqueue(OpeningBeatId)
            };
        }

        return new DemoProfile(state, recipes, facilityRecipeMap, TickMode.Rogue, book);
    }

    /// <summary>
    /// Demo-profile-only fixtures for the progressive-UI ledger:
    ///   • starts chrome at <see cref="UiLedger.DemoInitial"/> (grid + cursor only — journey 0:45);
    ///   • adds a plain, enterable Belt Pouch bag in a free root cell so the journey can fire a
    ///     real first-enter (breadcrumb push) trigger. Facility/wilderness bags open as look-in
    ///     panels (C/W) instead of pushing breadcrumbs, so a plain bag is the only end-to-end
    ///     way to exercise FirstEnter → Breadcrumbs.
    /// Placed at a fixed free index so it never shifts the smoke journey's pinned coordinates.
    /// Non-demo profiles are untouched — this lives only in the demo profile.
    /// </summary>
    private static GameState WithDemoLedgerFixtures(GameState state)
    {
        // First free root cell after the generated layout (0-7 filled, 28-29 planters); a plain
        // 4×2 pouch with a non-wilderness EnvironmentType so it enters via breadcrumbs.
        const int pouchCellIndex = 8;

        var pouchType = new ItemType("Belt Pouch", Category.Bag, IsStackable: false);
        var pouchBag = new Bag(Grid.Create(4, 2), "Pouch");

        var rootGrid = state.RootBag.Grid;
        if (!rootGrid.GetCell(pouchCellIndex).IsEmpty)
            return state with { Ui = UiLedger.DemoInitial }; // defensive: layout changed; skip the pouch, keep the ledger

        rootGrid = rootGrid.SetCell(pouchCellIndex,
            new Cell(Stack: new ItemStack(pouchType, 1, ContainedBagId: pouchBag.Id)));

        return state with
        {
            ItemTypes = state.ItemTypes.Contains(pouchType) ? state.ItemTypes : state.ItemTypes.Add(pouchType),
            Store = state.Store.Add(pouchBag).Set(state.RootBagId, state.RootBag with { Grid = rootGrid }),
            Ui = UiLedger.DemoInitial
        };
    }

    /// <summary>
    /// Slice-4 demo content: the look-in target the journey teaches.
    ///   • a peekable <b>Chest</b> (4×2, a couple of resting items) at a fixed free cell — the demo's
    ///     C-peek target (journey 7:00): look into and arrange it without entering.
    /// The enter-only teaching (journey 15:30) is now carried by the <b>Quiet 1</b> wilderness itself
    /// (<see cref="WithDemoWilderness"/>) — Slice 6 upgraded the throwaway "Quiet Pocket" placeholder
    /// into the real wilderness, so a single enter-only bag serves both the failed-peek tell and the
    /// exploration payoff. The eye core moved out of the Chest into the wilderness ruin (Slice 6).
    /// Pinned to a free index (9); defensive if the layout shifts. Demo profile only.
    /// </summary>
    private static GameState WithDemoSlice4Bags(GameState state)
    {
        const int chestCellIndex = 9;      // (1,1) — first free cell after the Belt Pouch at 8

        var rootGrid = state.RootBag.Grid;
        if (!rootGrid.GetCell(chestCellIndex).IsEmpty)
            return state; // layout changed; skip so we never overwrite content

        // FirstOrDefault (not ToDictionary): the demo's ItemTypes can carry same-named duplicates
        // (registry + fixture-added bag types), which a keyed dictionary would reject.
        ItemStack? Seed(string name, int count) =>
            state.ItemTypes.FirstOrDefault(t => t.Name == name) is { } t ? new ItemStack(t, count) : null;

        // Chest — a plain, peekable carrying bag with a couple of finds inside. (The eye core used to
        // ride here as a Slice-5 stopgap; Slice 6 relocated it to the wilderness ruin, journey 15:30.)
        var chestType = new ItemType("Chest", Category.Bag, IsStackable: false);
        var (chestGrid, _) = Grid.Create(4, 2).AcquireItems(
            new[] { Seed("Smooth Pebble", 3), Seed("Spring Water", 2) }.Where(s => s is not null).Select(s => s!));
        var chestBag = new Bag(chestGrid, "Chest");

        rootGrid = rootGrid
            .SetCell(chestCellIndex, new Cell(Stack: new ItemStack(chestType, 1, ContainedBagId: chestBag.Id)));

        var itemTypes = state.ItemTypes;
        if (!itemTypes.Contains(chestType)) itemTypes = itemTypes.Add(chestType);

        return state with
        {
            ItemTypes = itemTypes,
            Store = state.Store.Add(chestBag)
                .Set(state.RootBagId, state.RootBag with { Grid = rootGrid })
        };
    }

    /// <summary>Fixed-seed offset for the wilderness type-scatter — deterministic yet independent of
    /// the profile RNG already consumed by <see cref="CreateFromRegistry"/> (order-decoupled).</summary>
    private const int WildernessSeed = DemoSeed + 6;

    /// <summary>The demo wilderness's EnvironmentType, palette (Dust), and Quiet+ entropy glyph slug.</summary>
    public const string WildernessEnvironment = "Quiet 1";
    public const string WildernessPalette = "Dust";
    public const string WildernessGlyph = "quiet-positive"; // Quiet+ basis glyph (design/glyphs.md)

    /// <summary>The recipe id the ruin's "another Quiet 1" card teaches on pickup (Slice 6 explorer path).</summary>
    public const string AnotherQuiet1RecipeId = "another-quiet-1";

    /// <summary>
    /// Slice-6 demo content: the <b>Quiet 1 wilderness</b> — the enter-only, Dust-palette, Quiet+-glyphed
    /// bag that replaces the Slice-4 "Quiet Pocket" placeholder at the same home-room cell (10). Its 6×4
    /// grid is a <b>fixed, deterministic layout</b> (the demo's "fixed placements", build-plan Slice 6 —
    /// the conditional loot-table director stays deferred): open ground, seven <b>unnavigable trees</b>
    /// (<see cref="TreeFrame"/> + a Standing Tree item so both frontends render them), and scattered
    /// harvestables (Dry Grass / Bone Chips / sticks=Rough Wood / rocks=Plain Rock). Harvestable <i>types</i>
    /// away from the entrance are chosen by a fixed-seed RNG (<see cref="WildernessSeed"/>) — a genuine
    /// seeded scatter that is still 100% reproducible, so checkpoints stay stable. The entrance cell (0,0)
    /// is a fixed Dry Grass so the harvest checkpoint has a known target, and (0,1) is a tree so a player
    /// entering at (0,0) can bump it (the axe-absence beat).
    ///
    /// Deeper in sits a peekable <b>ruin</b> bag (C works on it — only the wilderness itself resists the
    /// peek) holding the three ruin finds: a <b>Scrawled Note</b> (half-pictographic scrap flavor), the
    /// <b>"another Quiet 1" recipe</b> card (a <see cref="GameState.RecipeItemProperty"/>-tagged item that
    /// teaches <see cref="AnotherQuiet1RecipeId"/> on pickup), and the relocated <b>eye core</b> (glyph
    /// "eye", matching the Shrine's empty slot). Pinned to cell 10; defensive if the layout shifts.
    /// </summary>
    private static GameState WithDemoWilderness(GameState state)
    {
        const int wildernessCellIndex = 10; // (1,2) — the cell the Slice-4 Quiet Pocket used to occupy

        var rootGrid = state.RootBag.Grid;
        if (!rootGrid.GetCell(wildernessCellIndex).IsEmpty)
            return state; // layout changed; skip so we never overwrite content

        ItemType Type(string name) =>
            state.ItemTypes.FirstOrDefault(t => t.Name == name)
            ?? new ItemType(name, Category.Material, IsStackable: true);

        // --- The ruin bag (peekable) — note, recipe, eye core, in a 3×1 row. ---
        var noteStack = new ItemStack(Type("Scrawled Note"), 1);
        var recipeStack = new ItemStack(Type("Quiet Recipe"), 1)
            .WithProperty(GameState.RecipeItemProperty, new StringValue(AnotherQuiet1RecipeId));
        var eyeCoreType = new ItemType("Eye Core", Category.Tool, IsStackable: false,
            Description: "A cold lens of stone, an eye-glyph cut deep. It watches nothing, or everything.");
        var eyeCore = new ItemStack(eyeCoreType, 1)
            .WithProperty(FeatureSlotFrame.GlyphProperty, new StringValue(FeatureSlotFrame.EyeGlyph));

        var ruinGrid = Grid.Create(3, 1)
            .SetCell(0, new Cell(Stack: noteStack))
            .SetCell(1, new Cell(Stack: recipeStack))
            .SetCell(2, new Cell(Stack: eyeCore));
        var ruinBag = new Bag(ruinGrid, "Ruin");
        var ruinType = new ItemType("Ruin", Category.Structure, IsStackable: false,
            Description: "A collapsed shape of stone, half-swallowed by dust. Something was kept here.");

        // --- The wilderness grid (6×4). Trees are unnavigable; the ruin sits at (2,0). ---
        var treeType = new ItemType("Standing Tree", Category.Structure, IsStackable: false,
            Description: "A grey trunk, hard as the day it stopped growing. No way through — not without an edge.");
        var treeCells = new HashSet<int> { 1, 3, 8, 11, 15, 19, 23 };
        const int ruinCellIndex = 12;

        // Harvestable positions (fixed) and the seeded type pool. Cell 0 is a fixed Dry Grass entrance.
        var harvestCells = new[] { 0, 4, 7, 10, 14, 17, 20, 22 };
        var pool = new[] { "Dry Grass", "Bone Chips", "Rough Wood", "Plain Rock" };
        var rng = new Random(WildernessSeed);

        var wildGrid = Grid.Create(6, 4);
        foreach (var i in treeCells)
            wildGrid = wildGrid.SetCell(i, new Cell(Stack: new ItemStack(treeType, 1), Frame: new TreeFrame()));
        wildGrid = wildGrid.SetCell(ruinCellIndex,
            new Cell(Stack: new ItemStack(ruinType, 1, ContainedBagId: ruinBag.Id)));
        foreach (var i in harvestCells)
        {
            // Entrance stays a fixed Dry Grass (stable harvest target); the rest are a seeded scatter.
            var name = i == 0 ? "Dry Grass" : pool[rng.Next(pool.Length)];
            wildGrid = wildGrid.SetCell(i, new Cell(Stack: new ItemStack(Type(name), i == 0 ? 2 : 1)));
        }

        var wildernessBag = new Bag(wildGrid, WildernessEnvironment, WildernessPalette)
        {
            EnterOnly = true,
            Glyph = WildernessGlyph
        };
        var wildernessType = new ItemType(WildernessEnvironment, Category.Bag, IsStackable: false,
            Description: "Dark inside. No bottom that you can see. You could fit — but you could not just look.");

        rootGrid = rootGrid.SetCell(wildernessCellIndex,
            new Cell(Stack: new ItemStack(wildernessType, 1, ContainedBagId: wildernessBag.Id)));

        var itemTypes = state.ItemTypes;
        foreach (var t in new[] { eyeCoreType, ruinType, treeType, wildernessType })
            if (!itemTypes.Contains(t)) itemTypes = itemTypes.Add(t);

        return state with
        {
            ItemTypes = itemTypes,
            Store = state.Store.Add(ruinBag).Add(wildernessBag)
                .Set(state.RootBagId, state.RootBag with { Grid = rootGrid })
        };
    }

    /// <summary>
    /// Slice-5 demo content: the <b>Shrine</b> — a plain, enterable bag item placed in a free home-room
    /// cell (reachable directly; a doorway/passage is unnecessary for the demo). Its 2×2 grid holds
    /// three pre-slotted, LOCKED feature slots — <b>Menu</b> (a peekable bag of the Start/Esc actions),
    /// the <b>Toolbar</b> representation, and the <b>Clock</b> (glyph-tagged so resting on it notices the
    /// readout) — plus one <b>empty eye slot</b> (glyph "eye", unlocked) awaiting the eye core. Entering
    /// the Shrine fires <see cref="UiTrigger.EnterShrine"/> (the ShrineView chrome row). Placed at a
    /// pinned free index; defensive if the layout shifts. Demo profile only.
    /// </summary>
    private static GameState WithDemoShrine(GameState state)
    {
        const int shrineCellIndex = 12; // (1,4) — first free cell after the Slice-4 bags (8,9,10) + drop (11)

        var rootGrid = state.RootBag.Grid;
        if (!rootGrid.GetCell(shrineCellIndex).IsEmpty)
            return state; // layout changed; skip so we never overwrite content

        // Menu — a peekable bag whose contents represent the Start/Esc actions (simple named items).
        var startType = new ItemType("Start", Category.Tool, IsStackable: false);
        var escType = new ItemType("Esc", Category.Tool, IsStackable: false);
        var (menuGrid, _) = Grid.Create(2, 1).AcquireItems(
            new[] { new ItemStack(startType, 1), new ItemStack(escType, 1) });
        var menuBag = new Bag(menuGrid, "Menu");
        var menuType = new ItemType("Menu", Category.Bag, IsStackable: false);

        // Toolbar representation — a portrait of the bar the player watched be born (empty stand-in bag).
        var toolbarRepBag = new Bag(Grid.Create(2, 1), "Toolbar");
        var toolbarRepType = new ItemType("Toolbar", Category.Bag, IsStackable: false);

        // Clock — the ticking clock item; glyph-tagged so a cursor-rest fires NoticeClock.
        var clockType = new ItemType("Clock", Category.Structure, IsStackable: false);
        var clockStack = new ItemStack(clockType, 1)
            .WithProperty(FeatureSlotFrame.GlyphProperty, new StringValue(FeatureSlotFrame.ClockGlyph));

        // Shrine grid — three locked feature slots + one empty eye slot.
        var shrineGrid = Grid.Create(2, 2)
            .SetCell(0, new Cell(
                Stack: new ItemStack(menuType, 1, ContainedBagId: menuBag.Id),
                Frame: new FeatureSlotFrame(FeatureSlotFrame.MenuGlyph, IsLocked: true)))
            .SetCell(1, new Cell(
                Stack: new ItemStack(toolbarRepType, 1, ContainedBagId: toolbarRepBag.Id),
                Frame: new FeatureSlotFrame(FeatureSlotFrame.ToolbarGlyph, IsLocked: true)))
            .SetCell(2, new Cell(
                Stack: clockStack,
                Frame: new FeatureSlotFrame(FeatureSlotFrame.ClockGlyph, IsLocked: true)))
            .SetCell(3, new Cell(
                Frame: new FeatureSlotFrame(FeatureSlotFrame.EyeGlyph, IsLocked: false)));
        var shrineBag = new Bag(shrineGrid, GameState.ShrineEnvironment);
        var shrineType = new ItemType("Shrine", Category.Structure, IsStackable: false);

        rootGrid = rootGrid.SetCell(shrineCellIndex,
            new Cell(Stack: new ItemStack(shrineType, 1, ContainedBagId: shrineBag.Id)));

        var itemTypes = state.ItemTypes;
        foreach (var t in new[] { startType, escType, menuType, toolbarRepType, clockType, shrineType })
            if (!itemTypes.Contains(t)) itemTypes = itemTypes.Add(t);

        return state with
        {
            ItemTypes = itemTypes,
            Store = state.Store.Add(menuBag).Add(toolbarRepBag).Add(shrineBag)
                .Set(state.RootBagId, state.RootBag with { Grid = rootGrid })
        };
    }

    /// <summary>
    /// Replaces the demo profile's toolbar (fixed inventory, Slice 3) with a single row of 4 slots:
    /// three fillable slots plus a seeded, non-full <b>Coin Pouch</b> carrying bag in the last slot.
    /// The size is deliberate and documented — small enough that one short journey can exhaustively
    /// exercise both the slot-fill order (pickups land 1→2→3, journey 4:00) AND capacity overflow
    /// (a 4th distinct pickup, with every plain slot full, acquires <i>into</i> the Coin Pouch —
    /// the bag-as-partial-empty rule). CreateStage1's wider 10×1 toolbar is kept for non-demo
    /// profiles, so this touches only the demo. Depth-invariance is intrinsic: the toolbar is its own
    /// <see cref="LocationId.T"/> bag, unchanged by how deep B navigates.
    /// </summary>
    private static GameState WithDemoToolbar(GameState state)
    {
        var pouchType = new ItemType("Coin Pouch", Category.Bag, IsStackable: false);
        var pouchBag = new Bag(Grid.Create(2, 1), "Pouch");

        var toolbarGrid = Grid.Create(4, 1)
            .SetCell(3, new Cell(Stack: new ItemStack(pouchType, 1, ContainedBagId: pouchBag.Id)));
        var toolbarBag = new Bag(toolbarGrid, "Toolbar");

        var store = state.Store.Add(toolbarBag).Add(pouchBag);
        if (state.ToolbarBagId is { } oldToolbarId)
            store = store.Remove(oldToolbarId); // drop CreateStage1's placeholder 10×1 toolbar

        return state with
        {
            Store = store,
            Locations = state.Locations.Set(LocationId.T, Location.AtOrigin(toolbarBag.Id)),
            ItemTypes = state.ItemTypes.Contains(pouchType) ? state.ItemTypes : state.ItemTypes.Add(pouchType)
        };
    }

    /// <summary>
    /// Creates a Stage 1 game with 4-10 random item stacks from the given item types.
    /// </summary>
    public static GameState CreateRandomStage1Game(ImmutableArray<ItemType> itemTypes, Random? random = null)
    {
        random ??= new Random();

        var stackCount = random.Next(4, 11);
        var stacks = Enumerable.Range(0, stackCount)
            .Select(_ =>
            {
                var itemType = itemTypes[random.Next(itemTypes.Length)];
                var count = itemType.IsStackable
                    ? random.Next(1, itemType.EffectiveMaxStackSize + 1)
                    : 1;
                return new ItemStack(itemType, count);
            })
            .ToList();

        return GameState.CreateStage1(itemTypes, stacks);
    }

    /// <summary>
    /// Creates a Stage 2 game: Stage 1 base plus a forest wilderness bag in the grid.
    /// </summary>
    public static GameState CreateRandomStage2Game(ImmutableArray<ItemType> itemTypes, Random? random = null)
    {
        random ??= new Random();

        var stackCount = random.Next(4, 11);
        var stacks = Enumerable.Range(0, stackCount)
            .Select(_ =>
            {
                var itemType = itemTypes[random.Next(itemTypes.Length)];
                var count = itemType.IsStackable
                    ? random.Next(1, itemType.EffectiveMaxStackSize + 1)
                    : 1;
                return new ItemStack(itemType, count);
            })
            .ToList();

        // Create forest wilderness bag from material-category items
        var materials = itemTypes.Where(t => t.Category == Category.Material).ToImmutableArray();
        var extraBags = new List<Bag>();
        if (materials.Length > 0)
        {
            var lootTable = materials.Select(m => (m, 1.0)).ToImmutableArray();
            var template = new WildernessTemplate("Forest", "Forest", "Green", 6, 4, 0.6, lootTable);
            var wildernessBag = WildernessGenerator.Generate(template, random);
            extraBags.Add(wildernessBag);

            var forestBagType = new ItemType("Forest Bag", Category.Bag, IsStackable: false);
            itemTypes = itemTypes.Add(forestBagType);
            var forestStack = new ItemStack(forestBagType, 1, ContainedBagId: wildernessBag.Id);
            stacks.Add(forestStack);
        }

        var state = GameState.CreateStage1(itemTypes, stacks);
        if (extraBags.Count > 0)
            state = state with { Store = state.Store.AddRange(extraBags) };
        return state;
    }

    /// <summary>
    /// Creates a Stage 3 game: Stage 2 base plus 3 facility bags (Workbench, Tanner, Seedling Pot)
    /// and starter crafting materials. Legacy method — uses hardcoded RecipeRegistry/FacilityBuilder.
    /// </summary>
    public static GameState CreateRandomStage3Game(ImmutableArray<ItemType> itemTypes, Random? random = null)
    {
        random ??= new Random();
        var byName = itemTypes.ToDictionary(t => t.Name);

        // Ensure facility bag types exist
        var workbenchType = new ItemType("Workbench", Category.Structure, IsStackable: false);
        var tannerType = new ItemType("Tanner", Category.Structure, IsStackable: false);
        var seedlingPotType = new ItemType("Seedling Pot", Category.Structure, IsStackable: false);
        var forestBagType = new ItemType("Forest Bag", Category.Bag, IsStackable: false);
        var beltPouchType = new ItemType("Belt Pouch", Category.Bag, IsStackable: false);

        itemTypes = itemTypes
            .Add(workbenchType)
            .Add(tannerType)
            .Add(seedlingPotType)
            .Add(forestBagType)
            .Add(beltPouchType);

        var stacks = new List<ItemStack>();
        var extraBags = new List<Bag>();

        // Build recipes so facility input slots can be filtered to specific item types
        var recipes = RecipeRegistry.BuildRecipes(itemTypes);
        var workbenchRecipe = recipes.FirstOrDefault(r => r.Id.StartsWith("workbench_"));
        var tannerRecipe = recipes.FirstOrDefault(r => r.Id.StartsWith("tanner_"));
        var seedlingRecipe = recipes.FirstOrDefault(r => r.Id.StartsWith("seedling_"));

        // Facility bags with recipe-filtered input slots
        var workbenchBag = FacilityBuilder.CreateWorkbench(workbenchRecipe);
        var tannerBag = FacilityBuilder.CreateTanner(tannerRecipe);
        var seedlingBag = FacilityBuilder.CreateSeedlingPot(seedlingRecipe);
        extraBags.AddRange(new[] { workbenchBag, tannerBag, seedlingBag });

        stacks.Add(new ItemStack(workbenchType, 1, ContainedBagId: workbenchBag.Id));
        stacks.Add(new ItemStack(tannerType, 1, ContainedBagId: tannerBag.Id));
        stacks.Add(new ItemStack(seedlingPotType, 1, ContainedBagId: seedlingBag.Id));

        // Forest wilderness bag
        var materials = itemTypes.Where(t => t.Category == Category.Material).ToImmutableArray();
        if (materials.Length > 0)
        {
            var lootTable = materials.Select(m => (m, 1.0)).ToImmutableArray();
            var template = new WildernessTemplate("Forest", "Forest", "Green", 6, 4, 0.6, lootTable);
            var wildernessBag = WildernessGenerator.Generate(template, random);
            extraBags.Add(wildernessBag);
            stacks.Add(new ItemStack(forestBagType, 1, ContainedBagId: wildernessBag.Id));
        }

        // Starter crafting materials (enough for 1-2 recipes)
        if (byName.TryGetValue("Plain Rock", out var rock))
            stacks.Add(new ItemStack(rock, 8));
        if (byName.TryGetValue("Rough Wood", out var wood))
            stacks.Add(new ItemStack(wood, 5));
        if (byName.TryGetValue("Tanned Leather", out var leather))
            stacks.Add(new ItemStack(leather, 4));
        if (byName.TryGetValue("Woven Fiber", out var fiber))
            stacks.Add(new ItemStack(fiber, 3));
        if (byName.TryGetValue("Forest Seed", out var seed))
            stacks.Add(new ItemStack(seed, 6));
        if (byName.TryGetValue("Rich Soil", out var soil))
            stacks.Add(new ItemStack(soil, 4));

        var state = GameState.CreateStage1(itemTypes, stacks);
        state = state with { Store = state.Store.AddRange(extraBags) };
        return state;
    }

    /// <summary>
    /// Creates a game from a ContentRegistry. Builds facility bags from facility/recipe definitions,
    /// generates wilderness bags from templates, and places starter materials.
    /// </summary>
    public static (GameState State, ImmutableArray<Recipe> Recipes) CreateFromRegistry(
        ContentRegistry registry, Random? random = null)
    {
        random ??= new Random();
        var itemTypes = registry.Items.Values.ToImmutableArray();
        var byName = registry.Items;
        var stacks = new List<ItemStack>();
        var extraBags = new List<Bag>();

        // Build facility bags from data-driven definitions.
        // Only Workshop is placed directly — other facilities are crafted from Workshop.
        var workshopFacilities = new HashSet<string> { "Workshop" };
        var workshopCraftable = registry.Facilities
            .Where(kv => workshopFacilities.Contains(kv.Key))
            .SelectMany(kv => registry.Recipes
                .Where(r => kv.Value.RecipeIds.Contains(r.Key))
                .Select(r => r.Value.Name))
            .ToHashSet();

        foreach (var (facilityId, facility) in registry.Facilities)
        {
            if (!byName.TryGetValue(facilityId, out var facilityItemType))
                continue;

            // Skip facilities that are crafted from Workshop (player builds them)
            if (workshopCraftable.Contains(facilityId))
                continue;

            // Get the first recipe for this facility to set initial slot filters
            var firstRecipeId = facility.RecipeIds.FirstOrDefault();
            Recipe? firstRecipe = firstRecipeId is not null && registry.Recipes.TryGetValue(firstRecipeId, out var r) ? r : null;

            var facilityBag = BuildFacilityBag(facility, firstRecipe);
            extraBags.Add(facilityBag);
            stacks.Add(new ItemStack(facilityItemType, 1, ContainedBagId: facilityBag.Id));
        }

        // Build wilderness bags from grid + loot table templates
        foreach (var (templateId, gridTemplate) in registry.GridTemplates)
        {
            // Find an item type matching the template's environment + " Bag"
            var bagName = $"{gridTemplate.EnvironmentType} Bag";
            if (!byName.TryGetValue(bagName, out var bagItemType))
                continue;

            // Find a matching loot table template (convention: same prefix)
            var lootTemplate = registry.LootTableTemplates.Values
                .FirstOrDefault();

            if (lootTemplate is null)
                continue;

            // Thread the caller's (possibly seeded) rng into wilderness generation so the
            // demo profile is reproducible — the built-in generator otherwise spins up its
            // own unseeded Random, which broke TUI↔Godot start-state parity.
            var wildernessBag = GeneratorBuiltins.Wilderness(null,
                new object[] { gridTemplate, lootTemplate, byName }, random);

            if (wildernessBag is BagValue bv)
            {
                extraBags.Add(bv.Bag);
                stacks.Add(new ItemStack(bagItemType, 1, ContainedBagId: bv.Bag.Id));
            }
        }

        // Starter crafting materials (enough to craft 1-2 facilities from Workshop)
        var starterMaterials = new[]
        {
            ("Plain Rock", 10), ("Rough Wood", 6), ("Tanned Leather", 6),
            ("Woven Fiber", 3), ("Forest Seed", 6), ("Rich Soil", 4)
        };
        foreach (var (name, count) in starterMaterials)
        {
            if (byName.TryGetValue(name, out var itemType))
                stacks.Add(new ItemStack(itemType, count));
        }

        var allRecipes = registry.Recipes.Values.ToImmutableArray();
        var state = GameState.CreateStage1(itemTypes, stacks);
        state = state with { Store = state.Store.AddRange(extraBags) };

        // Set up planter frames in bottom-right 4 cells (indices 28-31 of 8×4 grid)
        // and pre-plant Green Bean Plants in cells 28-29
        var rootBag = state.RootBag;
        var rootGrid = rootBag.Grid;
        for (int i = 28; i <= 31; i++)
        {
            var cell = rootGrid.GetCell(i);
            rootGrid = rootGrid.SetCell(i, cell with { Frame = new PlanterFrame() });
        }

        if (byName.TryGetValue("Green Bean Plant", out var beanPlantType))
        {
            for (int i = 28; i <= 29; i++)
            {
                var plant = new ItemStack(beanPlantType, 1)
                    .WithProperty("Progress", new IntValue(0))
                    .WithProperty("Duration", new IntValue(6))
                    .WithProperty("Yield", new IntValue(3))
                    .WithProperty("Produce", new StringValue("Green Bean"));
                var cell = rootGrid.GetCell(i);
                rootGrid = rootGrid.SetCell(i, cell with { Stack = plant });
            }
        }

        state = state with { Store = state.Store.Set(state.RootBagId, rootBag with { Grid = rootGrid }) };

        return (state, allRecipes);
    }

    /// <summary>
    /// Builds a facility bag from a FacilityDefinition and optional active recipe.
    /// Grid layout comes from the recipe; if no recipe, creates a default 3x1 grid.
    /// </summary>
    internal static Bag BuildFacilityBag(FacilityDefinition facility, Recipe? activeRecipe)
    {
        Grid grid;
        if (activeRecipe is not null)
        {
            grid = Grid.Create(activeRecipe.GridColumns, activeRecipe.GridRows);
            var builder = grid.Cells.ToBuilder();
            for (int i = 0; i < activeRecipe.Inputs.Count; i++)
            {
                builder[i] = new Cell(Frame: new InputSlotFrame(
                    $"in{i + 1}",
                    ItemTypeFilter: activeRecipe.Inputs[i].ItemType));
            }
            // Output slots fill remaining cells
            for (int i = activeRecipe.Inputs.Count; i < builder.Count; i++)
            {
                builder[i] = new Cell(Frame: new OutputSlotFrame($"out{i - activeRecipe.Inputs.Count + 1}"));
            }
            grid = grid with { Cells = builder.MoveToImmutable() };
        }
        else
        {
            grid = Grid.Create(3, 1);
        }

        return new Bag(grid, facility.EnvironmentType, facility.ColorScheme,
            FacilityState: new FacilityState(ActiveRecipeId: activeRecipe?.Id));
    }
}
