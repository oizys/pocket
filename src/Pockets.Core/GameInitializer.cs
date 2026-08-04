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
    /// Slice-4 demo content: the look-in-vs-enter pair the journey teaches.
    ///   • a peekable <b>Chest</b> (4×2, a couple of resting items) at a fixed free cell — the demo's
    ///     C-peek target (journey 7:00): look into and arrange it without entering.
    ///   • an <b>enter-only</b> placeholder bag (<b>Quiet Pocket</b>) at the next free cell — a cheap,
    ///     clearly-named stand-in for the Slice-6 wilderness. C is refused (the failed peek is the only
    ///     tell — no glyph, RATIFIED); E enters it via breadcrumbs like any plain bag.
    /// Both sit at pinned free indices (9, 10) so they never shift the smoke journey's earlier
    /// coordinates. Defensive: if the layout has changed and those cells aren't free, this is skipped.
    /// Non-demo profiles never call this — it lives only in the demo profile.
    /// </summary>
    private static GameState WithDemoSlice4Bags(GameState state)
    {
        const int chestCellIndex = 9;      // (1,1) — first free cell after the Belt Pouch at 8
        const int enterOnlyCellIndex = 10; // (1,2)

        var rootGrid = state.RootBag.Grid;
        if (!rootGrid.GetCell(chestCellIndex).IsEmpty || !rootGrid.GetCell(enterOnlyCellIndex).IsEmpty)
            return state; // layout changed; skip so we never overwrite content

        // FirstOrDefault (not ToDictionary): the demo's ItemTypes can carry same-named duplicates
        // (registry + fixture-added bag types), which a keyed dictionary would reject.
        ItemStack? Seed(string name, int count) =>
            state.ItemTypes.FirstOrDefault(t => t.Name == name) is { } t ? new ItemStack(t, count) : null;

        // Chest — a plain, peekable carrying bag with a couple of finds inside.
        var chestType = new ItemType("Chest", Category.Bag, IsStackable: false);
        var (chestGrid, _) = Grid.Create(4, 2).AcquireItems(
            new[] { Seed("Smooth Pebble", 3), Seed("Spring Water", 2) }.Where(s => s is not null).Select(s => s!));
        var chestBag = new Bag(chestGrid, "Chest");

        // Quiet Pocket — enter-only. A lone item inside gives the entered view something to show.
        var pocketType = new ItemType("Quiet Pocket", Category.Bag, IsStackable: false);
        var (pocketGrid, _) = Grid.Create(2, 2).AcquireItems(
            new[] { Seed("Plain Rock", 1) }.Where(s => s is not null).Select(s => s!));
        var pocketBag = new Bag(pocketGrid, "Quiet Pocket") { EnterOnly = true };

        rootGrid = rootGrid
            .SetCell(chestCellIndex, new Cell(Stack: new ItemStack(chestType, 1, ContainedBagId: chestBag.Id)))
            .SetCell(enterOnlyCellIndex, new Cell(Stack: new ItemStack(pocketType, 1, ContainedBagId: pocketBag.Id)));

        var itemTypes = state.ItemTypes;
        if (!itemTypes.Contains(chestType)) itemTypes = itemTypes.Add(chestType);
        if (!itemTypes.Contains(pocketType)) itemTypes = itemTypes.Add(pocketType);

        return state with
        {
            ItemTypes = itemTypes,
            Store = state.Store.Add(chestBag).Add(pocketBag)
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
