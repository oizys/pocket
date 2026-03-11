using System.Collections.Immutable;
using Pockets.Core.Data;
using Pockets.Core.Models;

namespace Pockets.Core.Tests.Models;

public class RecipeCycleTests
{
    private static readonly ItemType Rock = new("Plain Rock", Category.Material, IsStackable: true, MaxStackSize: 20);
    private static readonly ItemType Wood = new("Rough Wood", Category.Material, IsStackable: true, MaxStackSize: 20);
    private static readonly ItemType Fiber = new("Woven Fiber", Category.Material, IsStackable: true, MaxStackSize: 20);
    private static readonly ItemType Axe = new("Stone Axe", Category.Tool, IsStackable: false);
    private static readonly ItemType Shield = new("Wooden Shield", Category.Weapon, IsStackable: false);

    private static readonly Recipe AxeRecipe = new(
        "workbench-axe", "Stone Axe",
        new[] { new RecipeInput(Rock, 5), new RecipeInput(Wood, 3) },
        () => new[] { new ItemStack(Axe, 1) },
        Duration: 3, GridColumns: 3, GridRows: 1);

    private static readonly Recipe ShieldRecipe = new(
        "workbench-shield", "Wooden Shield",
        new[] { new RecipeInput(Wood, 6), new RecipeInput(Fiber, 2) },
        () => new[] { new ItemStack(Shield, 1) },
        Duration: 4, GridColumns: 3, GridRows: 1);

    private static readonly ImmutableArray<Recipe> Recipes =
        ImmutableArray.Create(AxeRecipe, ShieldRecipe);

    // Maps the "Workbench" facility type to the test recipe IDs (which use hyphens, not underscores)
    private static readonly ImmutableDictionary<string, ImmutableArray<string>> WorkbenchRecipeMap =
        ImmutableDictionary<string, ImmutableArray<string>>.Empty
            .Add("Workbench", ImmutableArray.Create("workbench-axe", "workbench-shield"));

    // ==================== FacilityState ====================

    [Fact]
    public void FacilityState_HasActiveRecipeId()
    {
        var state = new FacilityState(ActiveRecipeId: "workbench-axe");
        Assert.Equal("workbench-axe", state.ActiveRecipeId);
    }

    // ==================== CycleRecipe ====================

    [Fact]
    public void CycleRecipe_SetsActiveRecipeId()
    {
        var facility = CreateFacilityWithRecipe(AxeRecipe);
        var (updated, dumped) = FacilityLogic.CycleRecipe(facility, Recipes);

        Assert.Equal("workbench-shield", updated.FacilityState!.ActiveRecipeId);
    }

    [Fact]
    public void CycleRecipe_WrapsAroundToFirstRecipe()
    {
        var facility = CreateFacilityWithRecipe(ShieldRecipe);
        var (updated, _) = FacilityLogic.CycleRecipe(facility, Recipes);

        Assert.Equal("workbench-axe", updated.FacilityState!.ActiveRecipeId);
    }

    [Fact]
    public void CycleRecipe_DumpsInputItems()
    {
        var facility = CreateFacilityWithRecipe(AxeRecipe);
        // Place items in input slots
        var grid = facility.Grid;
        grid = grid.SetCell(0, grid.GetCell(0) with { Stack = new ItemStack(Rock, 5) });
        grid = grid.SetCell(1, grid.GetCell(1) with { Stack = new ItemStack(Wood, 3) });
        facility = facility with { Grid = grid };

        var (_, dumped) = FacilityLogic.CycleRecipe(facility, Recipes);

        Assert.Equal(2, dumped.Count);
        Assert.Contains(dumped, s => s.ItemType == Rock && s.Count == 5);
        Assert.Contains(dumped, s => s.ItemType == Wood && s.Count == 3);
    }

    [Fact]
    public void CycleRecipe_DumpsOutputItems()
    {
        var facility = CreateFacilityWithRecipe(AxeRecipe);
        // Place item in output slot
        var grid = facility.Grid;
        grid = grid.SetCell(2, grid.GetCell(2) with { Stack = new ItemStack(Axe, 1) });
        facility = facility with { Grid = grid };

        var (_, dumped) = FacilityLogic.CycleRecipe(facility, Recipes);

        Assert.Single(dumped);
        Assert.Equal(Axe, dumped[0].ItemType);
    }

    [Fact]
    public void CycleRecipe_RebuildsSlotsForNewRecipe()
    {
        var facility = CreateFacilityWithRecipe(AxeRecipe);
        var (updated, _) = FacilityLogic.CycleRecipe(facility, Recipes);

        // Shield recipe: Wood ×6 + Fiber ×2
        var slot0 = updated.Grid.GetCell(0);
        var slot1 = updated.Grid.GetCell(1);
        var slot2 = updated.Grid.GetCell(2);

        Assert.True(slot0.IsInputSlot);
        Assert.True(slot1.IsInputSlot);
        Assert.True(slot2.IsOutputSlot);

        var frame0 = (InputSlotFrame)slot0.Frame!;
        var frame1 = (InputSlotFrame)slot1.Frame!;
        Assert.Equal(Wood, frame0.ItemTypeFilter);
        Assert.Equal(Fiber, frame1.ItemTypeFilter);
    }

    [Fact]
    public void CycleRecipe_ResetsProgress()
    {
        var facility = CreateFacilityWithRecipe(AxeRecipe);
        facility = facility with
        {
            FacilityState = facility.FacilityState! with { RecipeId = "workbench-axe", Progress = 2 }
        };

        var (updated, _) = FacilityLogic.CycleRecipe(facility, Recipes);

        Assert.Equal(0, updated.FacilityState!.Progress);
        Assert.Null(updated.FacilityState.RecipeId);
    }

    [Fact]
    public void CycleRecipe_EmptySlotsAfterCycle()
    {
        var facility = CreateFacilityWithRecipe(AxeRecipe);
        var grid = facility.Grid;
        grid = grid.SetCell(0, grid.GetCell(0) with { Stack = new ItemStack(Rock, 5) });
        facility = facility with { Grid = grid };

        var (updated, _) = FacilityLogic.CycleRecipe(facility, Recipes);

        // All slots should be empty after cycling
        for (int i = 0; i < updated.Grid.Cells.Length; i++)
            Assert.True(updated.Grid.GetCell(i).IsEmpty);
    }

    [Fact]
    public void CycleRecipe_SingleRecipe_StaysOnSame()
    {
        var singleRecipeList = ImmutableArray.Create(AxeRecipe);
        var facility = CreateFacilityWithRecipe(AxeRecipe);

        var (updated, _) = FacilityLogic.CycleRecipe(facility, singleRecipeList);

        Assert.Equal("workbench-axe", updated.FacilityState!.ActiveRecipeId);
    }

    // ==================== Tick respects ActiveRecipeId ====================

    [Fact]
    public void Tick_UsesActiveRecipeId_NotScanning()
    {
        var facility = CreateFacilityWithRecipe(AxeRecipe);
        // Fill inputs for the axe recipe
        var grid = facility.Grid;
        grid = grid.SetCell(0, grid.GetCell(0) with { Stack = new ItemStack(Rock, 5) });
        grid = grid.SetCell(1, grid.GetCell(1) with { Stack = new ItemStack(Wood, 3) });
        facility = facility with { Grid = grid };

        var ticked = FacilityLogic.Tick(facility, Recipes);

        // Should start crafting the active recipe
        Assert.Equal("workbench-axe", ticked.FacilityState!.RecipeId);
        Assert.Equal(1, ticked.FacilityState.Progress);
    }

    // ==================== TickMode ====================

    [Fact]
    public void TickMode_Rogue_TicksOnAction()
    {
        // Create a facility bag with filled inputs for the axe recipe
        var facility = CreateFacilityWithFilledInputs(AxeRecipe);

        // Place the facility inside the root bag as a contained bag
        var bagItemType = new ItemType("Workbench Bag", Category.Bag, IsStackable: false);
        var facilityStack = new ItemStack(bagItemType, 1, ContainedBag: facility);
        var rootGrid = Grid.Create(4, 2);
        rootGrid = rootGrid.SetCell(0, rootGrid.GetCell(0) with { Stack = facilityStack });

        var handBag = new Bag(Grid.Create(1, 1));
        var state = new GameState(
            new Bag(rootGrid),
            new Cursor(new Position(0, 0)),
            ImmutableArray<ItemType>.Empty,
            handBag);

        // Execute an action (AcquireRandom always changes state, triggering a tick in Rogue mode)
        var rng = new Random(42);
        var allTypes = ImmutableArray.Create(Rock, Wood, Fiber, Axe, Shield);
        var stateWithTypes = state with { ItemTypes = allTypes };
        var sessionWithTypes = GameSession.New(stateWithTypes, Recipes, WorkbenchRecipeMap, TickMode.Rogue);
        var afterAction = sessionWithTypes.ExecuteAcquireRandom(rng);

        // Find the facility bag after the action
        var updatedFacilityBag = FindFacilityBag(afterAction.Current);
        Assert.NotNull(updatedFacilityBag);
        // In Rogue mode, facility should have progressed (progress > 0 or recipe started)
        Assert.True(
            updatedFacilityBag!.FacilityState!.Progress > 0 ||
            updatedFacilityBag.FacilityState.RecipeId != null,
            "Expected facility to have ticked (progress > 0 or RecipeId set) in Rogue mode");
    }

    [Fact]
    public void TickMode_Realtime_DoesNotTickOnAction()
    {
        var facility = CreateFacilityWithFilledInputs(AxeRecipe);

        var bagItemType = new ItemType("Workbench Bag", Category.Bag, IsStackable: false);
        var facilityStack = new ItemStack(bagItemType, 1, ContainedBag: facility);
        var rootGrid = Grid.Create(4, 2);
        rootGrid = rootGrid.SetCell(0, rootGrid.GetCell(0) with { Stack = facilityStack });

        var allTypes = ImmutableArray.Create(Rock, Wood, Fiber, Axe, Shield);
        var handBag = new Bag(Grid.Create(1, 1));
        var state = new GameState(
            new Bag(rootGrid),
            new Cursor(new Position(0, 0)),
            allTypes,
            handBag);

        var session = GameSession.New(state, Recipes, WorkbenchRecipeMap, TickMode.Realtime);

        var rng = new Random(42);
        var afterAction = session.ExecuteAcquireRandom(rng);

        // In Realtime mode, facility should NOT have ticked after an action
        var updatedFacilityBag = FindFacilityBag(afterAction.Current);
        Assert.NotNull(updatedFacilityBag);
        Assert.Equal(0, updatedFacilityBag!.FacilityState!.Progress);
        Assert.Null(updatedFacilityBag.FacilityState.RecipeId);
    }

    [Fact]
    public void TickMode_Realtime_ExplicitTickWorks()
    {
        var facility = CreateFacilityWithFilledInputs(AxeRecipe);

        var bagItemType = new ItemType("Workbench Bag", Category.Bag, IsStackable: false);
        var facilityStack = new ItemStack(bagItemType, 1, ContainedBag: facility);
        var rootGrid = Grid.Create(4, 2);
        rootGrid = rootGrid.SetCell(0, rootGrid.GetCell(0) with { Stack = facilityStack });

        var handBag = new Bag(Grid.Create(1, 1));
        var state = new GameState(
            new Bag(rootGrid),
            new Cursor(new Position(0, 0)),
            ImmutableArray<ItemType>.Empty,
            handBag);

        var session = GameSession.New(state, Recipes, WorkbenchRecipeMap, TickMode.Realtime);

        // Explicitly call Tick()
        var afterTick = session.Tick();

        var updatedFacilityBag = FindFacilityBag(afterTick.Current);
        Assert.NotNull(updatedFacilityBag);
        Assert.True(
            updatedFacilityBag!.FacilityState!.Progress > 0 ||
            updatedFacilityBag.FacilityState.RecipeId != null,
            "Expected facility to have ticked (progress > 0 or RecipeId set) after explicit Tick()");
        Assert.Equal(1, afterTick.TickCount);
    }

    [Fact]
    public void FacilityRecipeMap_UsedForCycleRecipe()
    {
        // Create a facility with the axe recipe active
        var facility = CreateFacilityWithRecipe(AxeRecipe);

        var bagItemType = new ItemType("Workbench Bag", Category.Bag, IsStackable: false);
        var facilityStack = new ItemStack(bagItemType, 1, ContainedBag: facility);
        var rootGrid = Grid.Create(4, 2);
        rootGrid = rootGrid.SetCell(0, rootGrid.GetCell(0) with { Stack = facilityStack });

        var handBag = new Bag(Grid.Create(1, 1));
        var state = new GameState(
            new Bag(rootGrid),
            new Cursor(new Position(0, 0)),
            ImmutableArray<ItemType>.Empty,
            handBag);

        // FacilityRecipeMap maps "Workbench" to ["workbench-axe", "workbench-shield"]
        // (using the test recipe IDs defined in this class)
        var facilityRecipeMap = ImmutableDictionary<string, ImmutableArray<string>>.Empty
            .Add("Workbench", ImmutableArray.Create("workbench-axe", "workbench-shield"));

        var session = GameSession.New(state, Recipes, facilityRecipeMap);

        // Navigate into the facility bag
        var enterResult = session.Current.EnterBag();
        Assert.True(enterResult.Success, "Expected EnterBag to succeed");
        var sessionInFacility = session with { Current = enterResult.State };

        // Cycle recipe: axe → shield (using FacilityRecipeMap)
        var afterCycle = sessionInFacility.ExecuteCycleRecipe();

        // Find the updated facility bag
        var updatedFacilityBag = FindFacilityBag(afterCycle.Current);
        Assert.NotNull(updatedFacilityBag);
        Assert.Equal("workbench-shield", updatedFacilityBag!.FacilityState!.ActiveRecipeId);
    }

    // ==================== Craft Completion Logging ====================

    [Fact]
    public void Tick_LogsCraftCompletion_WhenRecipeFinishes()
    {
        var facility = CreateFacilityWithFilledInputs(AxeRecipe);

        var bagItemType = new ItemType("Workbench Bag", Category.Bag, IsStackable: false);
        var facilityStack = new ItemStack(bagItemType, 1, ContainedBag: facility);
        var rootGrid = Grid.Create(4, 2);
        rootGrid = rootGrid.SetCell(0, rootGrid.GetCell(0) with { Stack = facilityStack });

        var handBag = new Bag(Grid.Create(1, 1));
        var state = new GameState(
            new Bag(rootGrid),
            new Cursor(new Position(0, 0)),
            ImmutableArray<ItemType>.Empty,
            handBag);

        var session = GameSession.New(state, Recipes, WorkbenchRecipeMap, TickMode.Realtime);

        // AxeRecipe Duration=3: tick 1 starts (progress=1), tick 2 (progress=2), tick 3 completes
        session = session.Tick();
        session = session.Tick();
        session = session.Tick();

        // Should have a completion log entry
        Assert.Contains(session.ActionLog, log => log.Contains("crafted") && log.Contains("Stone Axe"));
    }

    [Fact]
    public void Tick_NoCompletionLog_WhenStillCrafting()
    {
        var facility = CreateFacilityWithFilledInputs(AxeRecipe);

        var bagItemType = new ItemType("Workbench Bag", Category.Bag, IsStackable: false);
        var facilityStack = new ItemStack(bagItemType, 1, ContainedBag: facility);
        var rootGrid = Grid.Create(4, 2);
        rootGrid = rootGrid.SetCell(0, rootGrid.GetCell(0) with { Stack = facilityStack });

        var handBag = new Bag(Grid.Create(1, 1));
        var state = new GameState(
            new Bag(rootGrid),
            new Cursor(new Position(0, 0)),
            ImmutableArray<ItemType>.Empty,
            handBag);

        var session = GameSession.New(state, Recipes, WorkbenchRecipeMap, TickMode.Realtime);

        // Only 1 tick — still in progress
        session = session.Tick();

        Assert.DoesNotContain(session.ActionLog, log => log.Contains("crafted"));
    }

    [Fact]
    public void Rogue_LogsCraftCompletion_WhenRecipeFinishes()
    {
        var facility = CreateFacilityWithFilledInputs(AxeRecipe);

        var bagItemType = new ItemType("Workbench Bag", Category.Bag, IsStackable: false);
        var facilityStack = new ItemStack(bagItemType, 1, ContainedBag: facility);
        var rootGrid = Grid.Create(4, 2);
        rootGrid = rootGrid.SetCell(0, rootGrid.GetCell(0) with { Stack = facilityStack });

        var allTypes = ImmutableArray.Create(Rock, Wood, Fiber, Axe, Shield);
        var handBag = new Bag(Grid.Create(1, 1));
        var state = new GameState(
            new Bag(rootGrid),
            new Cursor(new Position(0, 0)),
            allTypes,
            handBag);

        var session = GameSession.New(state, Recipes, WorkbenchRecipeMap, TickMode.Rogue);

        // Execute enough actions to complete the recipe (Duration=3)
        var rng = new Random(42);
        session = session.ExecuteAcquireRandom(rng);
        session = session.ExecuteAcquireRandom(rng);
        session = session.ExecuteAcquireRandom(rng);

        // Should have a completion log entry from rogue-mode ticking
        Assert.Contains(session.ActionLog, log => log.Contains("crafted") && log.Contains("Stone Axe"));
    }

    // ==================== Helpers ====================

    private static Bag CreateFacilityWithRecipe(Recipe recipe)
    {
        var grid = Grid.Create(recipe.GridColumns, recipe.GridRows);
        var builder = grid.Cells.ToBuilder();
        for (int i = 0; i < recipe.Inputs.Count; i++)
        {
            builder[i] = new Cell(Frame: new InputSlotFrame(
                $"in{i + 1}", ItemTypeFilter: recipe.Inputs[i].ItemType));
        }
        for (int i = recipe.Inputs.Count; i < builder.Count; i++)
        {
            builder[i] = new Cell(Frame: new OutputSlotFrame($"out{i - recipe.Inputs.Count + 1}"));
        }
        grid = grid with { Cells = builder.MoveToImmutable() };

        return new Bag(grid, "Workbench", "Brown",
            FacilityState: new FacilityState(ActiveRecipeId: recipe.Id));
    }

    /// <summary>
    /// Creates a facility bag with slots set up for the given recipe and all input slots filled.
    /// </summary>
    private static Bag CreateFacilityWithFilledInputs(Recipe recipe)
    {
        var facility = CreateFacilityWithRecipe(recipe);
        var grid = facility.Grid;
        for (int i = 0; i < recipe.Inputs.Count; i++)
        {
            var input = recipe.Inputs[i];
            grid = grid.SetCell(i, grid.GetCell(i) with { Stack = new ItemStack(input.ItemType, input.Count) });
        }
        return facility with { Grid = grid };
    }

    /// <summary>
    /// Finds the first facility bag reachable from root via contained bags.
    /// </summary>
    private static Bag? FindFacilityBag(GameState state)
    {
        return FindFacilityInGrid(state.RootBag.Grid);
    }

    private static Bag? FindFacilityInGrid(Grid grid)
    {
        foreach (var cell in grid.Cells)
        {
            if (cell.Stack?.ContainedBag is { } bag)
            {
                if (bag.FacilityState is not null)
                    return bag;
                var nested = FindFacilityInGrid(bag.Grid);
                if (nested is not null)
                    return nested;
            }
        }
        return null;
    }
}
