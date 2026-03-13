using System.Collections.Immutable;
using Pockets.Core.Data;
using Pockets.Core.Models;

namespace Pockets.Core.Tests.Models;

/// <summary>
/// Data-driven validation that exercises every facility and recipe in the game.
/// Loads real content from /data/, builds an agent that navigates, grabs, delivers,
/// and crafts — asserting that every recipe completes successfully.
/// </summary>
public class AgentPlayValidationTests
{
    // --- Setup ---

    private record TestContext(
        GameSession Session,
        ContentRegistry Registry,
        ImmutableDictionary<string, ImmutableArray<string>> FacilityRecipeMap);

    /// <summary>
    /// Loads real content, places ALL facilities in root grid with sufficient materials,
    /// and returns a GameSession in Rogue tick mode.
    /// </summary>
    private static TestContext CreateTestState()
    {
        var dataPath = GetDataPath();
        var registry = ContentLoader.LoadFromDirectory(dataPath);
        var allRecipes = registry.Recipes.Values.ToImmutableArray();
        var facilityRecipeMap = registry.BuildFacilityRecipeMap();
        var itemTypes = registry.Items.Values.ToImmutableArray();
        var stacks = new List<ItemStack>();

        // Place ALL facilities in root grid (replicate BuildFacilityBag logic)
        foreach (var (facilityId, facility) in registry.Facilities)
        {
            if (!registry.Items.TryGetValue(facilityId, out var facilityItemType))
                continue;

            var firstRecipeId = facility.RecipeIds.FirstOrDefault();
            Recipe? firstRecipe = firstRecipeId is not null && registry.Recipes.TryGetValue(firstRecipeId, out var r) ? r : null;

            var facilityBag = BuildFacilityBag(facility, firstRecipe);
            stacks.Add(new ItemStack(facilityItemType, 1, ContainedBag: facilityBag));
        }

        // Compute total materials needed across ALL recipes and provide surplus
        var materialNeeds = new Dictionary<string, int>();
        foreach (var recipe in allRecipes)
        {
            foreach (var input in recipe.Inputs)
            {
                var name = input.ItemType.Name;
                materialNeeds.TryGetValue(name, out var current);
                materialNeeds[name] = current + input.Count;
            }
        }

        // Place material stacks with 2x needed quantities (enough for all recipes)
        foreach (var (name, needed) in materialNeeds)
        {
            if (registry.Items.TryGetValue(name, out var itemType))
            {
                var total = needed * 2;
                var max = itemType.EffectiveMaxStackSize;
                while (total > 0)
                {
                    var count = Math.Min(total, max);
                    stacks.Add(new ItemStack(itemType, count));
                    total -= count;
                }
            }
        }

        var state = GameState.CreateStage1(itemTypes, stacks);
        var session = GameSession.New(state, allRecipes, facilityRecipeMap, TickMode.Rogue);

        return new TestContext(session, registry, facilityRecipeMap);
    }

    /// <summary>
    /// Replicates GameInitializer.BuildFacilityBag (which is internal).
    /// </summary>
    private static Bag BuildFacilityBag(FacilityDefinition facility, Recipe? activeRecipe)
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

    private static string GetDataPath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Pockets.sln")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "data");
    }

    // --- Agent Helpers ---

    /// <summary>
    /// Moves cursor step-by-step from current position to target position (wrap-aware).
    /// </summary>
    private static GameSession NavigateTo(GameSession session, Position target)
    {
        var current = session.Current.Cursor.Position;
        var grid = session.Current.ActiveBag.Grid;

        // Navigate rows
        while (current.Row != target.Row)
        {
            var rowDist = target.Row - current.Row;
            if (rowDist < 0) rowDist += grid.Rows;
            session = rowDist <= grid.Rows / 2
                ? session.MoveCursor(Direction.Down)
                : session.MoveCursor(Direction.Up);
            current = session.Current.Cursor.Position;
        }

        // Navigate columns
        while (current.Col != target.Col)
        {
            var colDist = target.Col - current.Col;
            if (colDist < 0) colDist += grid.Columns;
            session = colDist <= grid.Columns / 2
                ? session.MoveCursor(Direction.Right)
                : session.MoveCursor(Direction.Left);
            current = session.Current.Cursor.Position;
        }

        return session;
    }

    /// <summary>
    /// Scans the active bag for an item type and returns its position, or null if not found.
    /// </summary>
    private static Position? FindItemInGrid(GameState state, ItemType itemType)
    {
        var grid = state.ActiveBag.Grid;
        for (int i = 0; i < grid.Cells.Length; i++)
        {
            var cell = grid.GetCell(i);
            if (!cell.IsEmpty && cell.Stack!.ItemType == itemType)
                return Position.FromIndex(i, grid.Columns);
        }
        return null;
    }

    /// <summary>
    /// Scans the active bag for an item type with at least minCount quantity.
    /// </summary>
    private static Position? FindItemInGrid(GameState state, ItemType itemType, int minCount)
    {
        var grid = state.ActiveBag.Grid;
        for (int i = 0; i < grid.Cells.Length; i++)
        {
            var cell = grid.GetCell(i);
            if (!cell.IsEmpty && cell.Stack!.ItemType == itemType && cell.Stack.Count >= minCount)
                return Position.FromIndex(i, grid.Columns);
        }
        return null;
    }

    /// <summary>
    /// Scans root grid for a facility bag by environment type and returns its position.
    /// </summary>
    private static Position? FindFacilityInGrid(GameState state, string envType)
    {
        var grid = state.RootBag.Grid;
        for (int i = 0; i < grid.Cells.Length; i++)
        {
            var cell = grid.GetCell(i);
            if (!cell.IsEmpty && cell.Stack!.ContainedBag is { } bag && bag.EnvironmentType == envType)
                return Position.FromIndex(i, grid.Columns);
        }
        return null;
    }

    /// <summary>
    /// Finds first empty cell in the active bag.
    /// </summary>
    private static Position? FindEmptyCell(GameState state)
    {
        var grid = state.ActiveBag.Grid;
        for (int i = 0; i < grid.Cells.Length; i++)
        {
            if (grid.GetCell(i).IsEmpty)
                return Position.FromIndex(i, grid.Columns);
        }
        return null;
    }

    /// <summary>
    /// Navigates to an item and grabs it. Asserts hand is full after.
    /// If the item has more than needed count, splits first.
    /// </summary>
    private static GameSession GrabFromGrid(GameSession session, Position itemPos, int? exactCount = null)
    {
        session = NavigateTo(session, itemPos);
        var stack = session.Current.CurrentCell.Stack;
        Assert.NotNull(stack);

        if (exactCount.HasValue && stack!.Count > exactCount.Value)
        {
            // Modal split: keep (total - needed) in cell, take needed into hand
            session = session.ExecuteModalSplit(stack.Count - exactCount.Value);
        }
        else
        {
            session = session.ExecutePrimary(); // grab
        }
        Assert.True(session.Current.HasItemsInHand, $"Expected hand to have items after grabbing {stack!.ItemType.Name}");
        return session;
    }

    /// <summary>
    /// Enters a facility, navigates to a slot, drops hand contents, then leaves.
    /// </summary>
    private static GameSession DeliverToSlot(GameSession session, Position facilityPos, int slotIdx)
    {
        Assert.True(session.Current.HasItemsInHand, "Hand must have items before delivering");

        // Ensure we're at root level
        Assert.False(session.Current.IsNested, "Must be at root to deliver to facility");

        // Navigate to facility and enter
        session = NavigateTo(session, facilityPos);
        session = session.ExecutePrimary(); // enter facility (bag at cursor)
        Assert.True(session.Current.IsNested, "Expected to be inside facility");

        // Navigate to the target slot within the facility
        var slotPos = Position.FromIndex(slotIdx, session.Current.ActiveBag.Grid.Columns);
        session = NavigateTo(session, slotPos);

        // Drop
        session = session.ExecutePrimary(); // drop in slot
        Assert.False(session.Current.HasItemsInHand, "Hand should be empty after drop");

        // Leave facility
        session = session.ExecuteLeaveBag();
        Assert.False(session.Current.IsNested, "Expected to be back at root");

        return session;
    }

    /// <summary>
    /// Grabs an item from the grid and delivers it to a facility slot.
    /// Handles splitting when the stack has more than needed.
    /// </summary>
    private static GameSession GrabAndDeliver(GameSession session, ItemType itemType, int count,
        Position facilityPos, int slotIdx)
    {
        // Find the item in root grid (we should be at root)
        var itemPos = FindItemInGrid(session.Current, itemType, count);
        Assert.NotNull(itemPos);

        session = GrabFromGrid(session, itemPos.Value, exactCount: count);
        session = DeliverToSlot(session, facilityPos, slotIdx);
        return session;
    }

    /// <summary>
    /// Enters a facility and cycles its recipe until it matches the target recipe ID.
    /// </summary>
    private static GameSession SetFacilityRecipe(GameSession session, Position facilityPos,
        string targetRecipeId)
    {
        session = NavigateTo(session, facilityPos);
        session = session.ExecutePrimary(); // enter facility
        Assert.True(session.Current.IsNested);

        var facility = session.Current.ActiveBag;
        var maxCycles = 20; // safety limit
        var cycles = 0;

        while (facility.FacilityState?.ActiveRecipeId != targetRecipeId && cycles < maxCycles)
        {
            session = session.ExecuteCycleRecipe();
            facility = session.Current.ActiveBag;
            cycles++;
        }

        Assert.Equal(targetRecipeId, facility.FacilityState?.ActiveRecipeId);

        session = session.ExecuteLeaveBag();
        Assert.False(session.Current.IsNested);
        return session;
    }

    /// <summary>
    /// Ticks the game (via ExecuteSort as a neutral action) until the facility's
    /// RecipeId resets to null (craft complete).
    /// </summary>
    private static GameSession TickUntilComplete(GameSession session, string envType, int maxTicks = 50)
    {
        for (int i = 0; i < maxTicks; i++)
        {
            var facility = session.Current.Registry.Facilities
                .FirstOrDefault(f => f.EnvironmentType == envType);
            if (facility is not null && facility.FacilityState?.RecipeId is null)
                return session;

            session = session.ExecuteSort(); // neutral action that generates a tick
        }

        // Final check
        var finalFacility = session.Current.Registry.Facilities
            .FirstOrDefault(f => f.EnvironmentType == envType);
        Assert.Null(finalFacility?.FacilityState?.RecipeId);
        return session;
    }

    // --- Main Test ---

    [Fact]
    public void AllFacilities_AllRecipes_CraftSuccessfully()
    {
        var ctx = CreateTestState();
        var session = ctx.Session;
        var registry = ctx.Registry;
        var completedRecipes = new List<string>();

        foreach (var (facilityId, facilityDef) in registry.Facilities)
        {
            var envType = facilityDef.EnvironmentType;

            foreach (var recipeId in facilityDef.RecipeIds)
            {
                if (!registry.Recipes.TryGetValue(recipeId, out var recipe))
                    continue;

                // 1. Find the facility in root grid
                var facilityPos = FindFacilityInGrid(session.Current, envType);
                Assert.NotNull(facilityPos);

                // 2. Set the correct recipe
                session = SetFacilityRecipe(session, facilityPos.Value, recipeId);

                // Re-find facility position (cycle may have dumped items, causing sort shifts)
                facilityPos = FindFacilityInGrid(session.Current, envType);
                Assert.NotNull(facilityPos);

                // 3. Deliver each input material
                for (int i = 0; i < recipe.Inputs.Count; i++)
                {
                    var input = recipe.Inputs[i];
                    session = GrabAndDeliver(session, input.ItemType, input.Count,
                        facilityPos.Value, i);
                }

                // 4. Tick until craft completes
                session = TickUntilComplete(session, envType);

                // 5. Verify output and extract it
                facilityPos = FindFacilityInGrid(session.Current, envType);
                Assert.NotNull(facilityPos);

                session = NavigateTo(session, facilityPos.Value);
                session = session.ExecutePrimary(); // enter facility
                Assert.True(session.Current.IsNested);

                // Find the output slot (last cell in facility grid)
                var facilityGrid = session.Current.ActiveBag.Grid;
                Position? outputPos = null;
                for (int i = 0; i < facilityGrid.Cells.Length; i++)
                {
                    var cell = facilityGrid.GetCell(i);
                    if (cell.Frame is OutputSlotFrame && !cell.IsEmpty)
                    {
                        outputPos = Position.FromIndex(i, facilityGrid.Columns);
                        break;
                    }
                }

                Assert.True(outputPos.HasValue,
                    $"No output found for {recipe.Name} in {envType}");

                session = NavigateTo(session, outputPos.Value);
                session = session.ExecutePrimary(); // output slots always grab
                Assert.True(session.Current.HasItemsInHand,
                    $"Expected to grab crafted {recipe.Name} from output slot");

                session = session.ExecuteLeaveBag();
                Assert.False(session.Current.IsNested);

                // Drop crafted item in an empty root cell
                var emptyPos = FindEmptyCell(session.Current);
                Assert.NotNull(emptyPos);
                session = NavigateTo(session, emptyPos.Value);
                session = session.ExecutePrimary(); // drop
                Assert.False(session.Current.HasItemsInHand);

                completedRecipes.Add(recipeId);
            }
        }

        // Assert all recipes were crafted
        var allRecipeIds = registry.Facilities.Values
            .SelectMany(f => f.RecipeIds)
            .Distinct()
            .ToHashSet();

        Assert.Equal(allRecipeIds.Count, completedRecipes.Count);
        foreach (var id in allRecipeIds)
        {
            Assert.Contains(id, completedRecipes);
        }
    }
}
