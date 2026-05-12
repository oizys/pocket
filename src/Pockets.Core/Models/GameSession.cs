using System.Collections.Immutable;
using Pockets.Core.Data;
using Pockets.Core.Dsl;

namespace Pockets.Core.Models;

/// <summary>
/// Controls when facility ticks fire: on player action (rogue-like) or via external timer (realtime).
/// </summary>
public enum TickMode { Rogue, Realtime }

/// <summary>
/// Wraps GameState with undo history and action log. GameState stays as pure domain state;
/// GameSession manages history, dispatches tools, and records actions.
/// MoveCursor is not undoable. Failed tools are not pushed to undo stack but errors are logged.
/// </summary>
public record GameSession(
    GameState Current,
    ImmutableStack<GameState> UndoStack,
    ImmutableList<string> ActionLog,
    ImmutableArray<Recipe> Recipes = default,
    ImmutableDictionary<string, ImmutableArray<string>>? FacilityRecipeMap = null,
    TickMode TickMode = TickMode.Rogue,
    int TickCount = 0,
    int MaxUndoDepth = 1000)
{
    /// <summary>
    /// Creates a new session with empty undo history.
    /// </summary>
    public static GameSession New(GameState initialState, int maxUndoDepth = 1000) =>
        new(initialState, ImmutableStack<GameState>.Empty, ImmutableList<string>.Empty,
            ImmutableArray<Recipe>.Empty, null, TickMode.Realtime, 0, maxUndoDepth);

    /// <summary>
    /// Creates a new session with recipes for facility crafting.
    /// </summary>
    public static GameSession New(GameState initialState, ImmutableArray<Recipe> recipes,
        TickMode tickMode = TickMode.Realtime, int maxUndoDepth = 1000) =>
        new(initialState, ImmutableStack<GameState>.Empty, ImmutableList<string>.Empty,
            recipes, null, tickMode, 0, maxUndoDepth);

    /// <summary>
    /// Creates a new session with recipes and facility→recipe mapping from ContentRegistry.
    /// </summary>
    public static GameSession New(
        GameState initialState,
        ImmutableArray<Recipe> recipes,
        ImmutableDictionary<string, ImmutableArray<string>> facilityRecipeMap,
        TickMode tickMode = TickMode.Realtime,
        int maxUndoDepth = 1000) =>
        new(initialState, ImmutableStack<GameState>.Empty, ImmutableList<string>.Empty,
            recipes, facilityRecipeMap, tickMode, 0, maxUndoDepth);

    /// <summary>
    /// True if there is at least one state to undo to.
    /// </summary>
    public bool CanUndo => !UndoStack.IsEmpty;

    /// <summary>
    /// Number of states in the undo stack.
    /// </summary>
    public int UndoDepth => UndoStack.Count();

    /// <summary>
    /// Undo the last action: pop the undo stack and restore that state.
    /// Returns null if nothing to undo. Logs the undo action.
    /// </summary>
    public GameSession? Undo()
    {
        if (!CanUndo) return null;

        var previousState = UndoStack.Peek();
        var poppedStack = UndoStack.Pop();
        var lastAction = ActionLog.Count > 0 ? ActionLog[^1] : "unknown";
        var logEntry = $"Undo: {lastAction}";

        return this with
        {
            Current = previousState,
            UndoStack = poppedStack,
            ActionLog = ActionLog.Add(logEntry)
        };
    }

    /// <summary>
    /// Moves the cursor one step in the B location. Not undoable, not logged.
    /// </summary>
    public GameSession MoveCursor(Direction direction) =>
        MoveCursorAt(LocationId.B, direction);

    /// <summary>
    /// Moves the cursor one step at the given location. Not undoable, not logged.
    /// </summary>
    public GameSession MoveCursorAt(LocationId locId, Direction direction)
    {
        var loc = Current.Locations.TryGet(locId);
        if (loc is null) return this;

        // Resolve the active bag for this location (follow breadcrumbs)
        var bagId = loc.BagId;
        foreach (var entry in loc.Breadcrumbs.Reverse())
        {
            var bag = Current.Store.GetById(bagId);
            if (bag is null) break;
            var cell = bag.Grid.GetCell(entry.CellIndex);
            if (cell.Stack?.ContainedBagId is not { } childId) break;
            bagId = childId;
        }

        var activeBag = Current.Store.GetById(bagId);
        if (activeBag is null) return this;

        var newCursor = loc.Cursor.Move(direction, activeBag.Grid.Rows, activeBag.Grid.Columns);
        var newLoc = loc with { Cursor = newCursor };
        return this with { Current = Current with { Locations = Current.Locations.Set(locId, newLoc) } };
    }

    /// <summary>
    /// Moves the cursor to a specific position at the B location. Not undoable, not logged.
    /// </summary>
    public GameSession MoveCursor(GameState state, Position position)
    {
        var bLoc = state.Locations.Get(LocationId.B);
        var newLoc = bLoc with { Cursor = new Cursor(position) };
        return this with { Current = state with { Locations = state.Locations.Set(LocationId.B, newLoc) } };
    }

    /// <summary>
    /// Public wrapper for ApplyResult, used by GameController for panel operations.
    /// </summary>
    public GameSession ApplyToolResult(ToolResult result, Func<string> formatLog) =>
        ApplyResult(result, formatLog);

    /// <summary>
    /// Runs a tool at a given location by routing the underlying tool methods —
    /// which are written against LocationId.B — through B by temporarily swapping
    /// the focused location's LocationInfo into B and restoring afterwards.
    /// Returning the tool's resulting session unchanged when the focus is B.
    /// </summary>
    private GameSession RunAt(LocationId loc, Func<GameSession, GameSession> tool)
    {
        if (loc == LocationId.B) return tool(this);

        var state = Current;
        var savedB = state.Locations.Get(LocationId.B);
        var focusedLoc = state.Locations.Get(loc);

        var swapped = state with { Locations = state.Locations.Set(LocationId.B, focusedLoc) };
        var swappedSession = this with { Current = swapped };

        var afterTool = tool(swappedSession);

        var newFocusedLoc = afterTool.Current.Locations.Get(LocationId.B);
        var restored = afterTool.Current with
        {
            Locations = afterTool.Current.Locations
                .Set(LocationId.B, savedB)
                .Set(loc, newFocusedLoc)
        };
        return afterTool with { Current = restored };
    }

    /// <summary>
    /// Primary action (left-click / key 1). Contextual grab/drop/swap/merge/interact. Undoable.
    /// Routed via the focused location when not B.
    /// </summary>
    public GameSession ExecutePrimary(LocationId loc) => RunAt(loc, s => s.ExecutePrimary());

    /// <summary>
    /// Primary action against B. Existing call sites without focus continue to operate here.
    /// </summary>
    public GameSession ExecutePrimary()
    {
        var cell = Current.CurrentCell;
        var handFull = Current.HasItemsInHand;
        var result = Current.ToolPrimary();
        return ApplyResult(result, () =>
        {
            if (cell.HasBag)
                return $"Enter: {cell.Stack?.ItemType.Name ?? "bag"}";
            if (!handFull && !cell.IsEmpty && Current.IsNested)
                return $"Harvest: {cell.Stack!.Count} {cell.Stack.ItemType.Name}";
            if (!handFull && !cell.IsEmpty)
                return $"Grab: {cell.Stack!.Count} {cell.Stack!.ItemType.Name}";
            if (handFull && cell.IsEmpty)
                return $"Drop: {Current.HandItems[0].Count} {Current.HandItems[0].ItemType.Name}";
            if (handFull && !cell.IsEmpty && cell.Stack!.ItemType == Current.HandItems[0].ItemType)
                return $"Merge: {Current.HandItems[0].Count} {Current.HandItems[0].ItemType.Name}";
            if (handFull && !cell.IsEmpty)
                return $"Swap: {Current.HandItems[0].ItemType.Name} ↔ {cell.Stack!.ItemType.Name}";
            return "Primary: no-op";
        });
    }

    /// <summary>
    /// Secondary action routed via the focused location when not B.
    /// </summary>
    public GameSession ExecuteSecondary(LocationId loc) => RunAt(loc, s => s.ExecuteSecondary());

    /// <summary>
    /// Secondary action (right-click / key 2). Half/one variant. Undoable.
    /// </summary>
    public GameSession ExecuteSecondary()
    {
        var cell = Current.CurrentCell;
        var handFull = Current.HasItemsInHand;
        var result = Current.ToolSecondary();
        return ApplyResult(result, () =>
        {
            if (!handFull && !cell.IsEmpty)
                return $"Grab half: {cell.Stack!.ItemType.Name}";
            if (handFull)
                return $"Place 1: {Current.HandItems[0].ItemType.Name}";
            return "Secondary: no-op";
        });
    }

    /// <summary>
    /// Context-sensitive interact at cursor. Enters bags, harvests in wilderness, etc. Undoable.
    /// </summary>
    public GameSession ExecuteInteract()
    {
        var cell = Current.CurrentCell;
        var result = Current.Interact();
        return ApplyResult(result, () =>
        {
            if (cell.HasBag)
                return $"Enter: {cell.Stack?.ItemType.Name ?? "bag"}";
            if (Current.IsNested && !cell.IsEmpty)
                return $"Harvest: {cell.Stack!.Count} {cell.Stack.ItemType.Name}";
            return "Interact: nothing to do";
        });
    }

    /// <summary>
    /// Enter the bag at cursor cell. Undoable.
    /// </summary>
    public GameSession ExecuteEnterBag()
    {
        var result = Current.EnterBag();
        var bagName = Current.CurrentCell.Stack?.ItemType.Name ?? "bag";
        return ApplyResult(result, () => $"Enter: {bagName}");
    }

    /// <summary>
    /// Leave the current bag, return to parent. Undoable.
    /// </summary>
    public GameSession ExecuteLeaveBag()
    {
        var result = Current.LeaveBag();
        return ApplyResult(result, () => "Back: returned to parent bag");
    }

    /// <summary>
    /// Execute Grab tool on current state.
    /// </summary>
    public GameSession ExecuteGrab()
    {
        var cursorItem = Current.CurrentCell.Stack;
        var result = Current.ToolGrab();
        return ApplyResult(result, () => FormatGrabLog(cursorItem, Current.Cursor.Position));
    }

    /// <summary>
    /// Execute Drop tool on current state.
    /// </summary>
    public GameSession ExecuteDrop()
    {
        var handItems = Current.HandItems;
        var result = Current.ToolDrop();
        return ApplyResult(result, () => FormatDropLog(handItems, Current.Cursor.Position));
    }

    /// <summary>
    /// QuickSplit routed via the focused location when not B.
    /// </summary>
    public GameSession ExecuteQuickSplit(LocationId loc) => RunAt(loc, s => s.ExecuteQuickSplit());

    /// <summary>
    /// Execute QuickSplit tool on current state.
    /// </summary>
    public GameSession ExecuteQuickSplit()
    {
        var cursorItem = Current.CurrentCell.Stack;
        var result = Current.ToolQuickSplit();
        return ApplyResult(result, () => FormatSplitLog(cursorItem));
    }

    /// <summary>
    /// Execute ModalSplit tool with a specific left count.
    /// </summary>
    public GameSession ExecuteModalSplit(int leftCount)
    {
        var cursorItem = Current.CurrentCell.Stack;
        var result = Current.ToolModalSplit(leftCount);
        return ApplyResult(result, () => FormatModalSplitLog(cursorItem, leftCount));
    }

    /// <summary>
    /// Sort routed via the focused location when not B.
    /// </summary>
    public GameSession ExecuteSort(LocationId loc) => RunAt(loc, s => s.ExecuteSort());

    /// <summary>
    /// Execute Sort tool on current state.
    /// </summary>
    public GameSession ExecuteSort()
    {
        var result = Current.ToolSort();
        return ApplyResult(result, () => "Sort: reorganized bag");
    }

    /// <summary>
    /// Execute Harvest tool on current state. Removes item from cursor cell
    /// in active bag and acquires it into the parent bag.
    /// </summary>
    public GameSession ExecuteHarvest()
    {
        var cursorItem = Current.CurrentCell.Stack;
        var result = Current.ToolHarvest();
        return ApplyResult(result, () =>
            cursorItem != null ? $"Harvest: {cursorItem.Count} {cursorItem.ItemType.Name}" : "Harvest: empty cell");
    }

    /// <summary>
    /// CycleRecipe routed via the focused location when not B.
    /// </summary>
    public GameSession ExecuteCycleRecipe(LocationId loc) => RunAt(loc, s => s.ExecuteCycleRecipe());

    /// <summary>
    /// Cycles the active recipe on the facility at cursor. Dumps slot items to root bag.
    /// </summary>
    public GameSession ExecuteCycleRecipe()
    {
        var activeBag = Current.ActiveBag;
        if (activeBag.FacilityState is null)
            return this with { ActionLog = ActionLog.Add("FAILED: CycleRecipe — not in a facility") };

        if (Recipes.IsDefaultOrEmpty)
            return this with { ActionLog = ActionLog.Add("FAILED: CycleRecipe — no recipes loaded") };

        var facilityRecipes = GetRecipesForFacility(activeBag.EnvironmentType);

        if (facilityRecipes.Count == 0)
            return this with { ActionLog = ActionLog.Add("FAILED: CycleRecipe — no recipes for this facility") };

        var (updated, dumped) = FacilityLogic.CycleRecipe(activeBag, facilityRecipes);

        // Reset progress on the owning stack when cycling recipes
        var newState = Current.ReplaceBagById(activeBag.Id, updated,
            stack => stack.WithProperty("Progress", new IntValue(0)));

        // Acquire dumped items into root bag
        if (dumped.Count > 0)
        {
            var rootGrid = newState.RootBag.Grid;
            foreach (var stack in dumped)
            {
                var (newGrid, _) = rootGrid.AcquireItems(new[] { stack });
                rootGrid = newGrid;
            }
            newState = newState with { Store = newState.Store.Set(newState.RootBagId, newState.RootBag with { Grid = rootGrid }) };
        }

        var newStack = PushWithLimit(UndoStack, Current);
        return this with
        {
            Current = newState,
            UndoStack = newStack,
            ActionLog = ActionLog.Add($"CycleRecipe: switched to {updated.FacilityState!.ActiveRecipeId}")
        };
    }

    /// <summary>
    /// Execute AcquireRandom tool on current state.
    /// </summary>
    public GameSession ExecuteAcquireRandom(Random rng)
    {
        var result = Current.ToolAcquireRandom(rng);
        var newItem = result.State.RootBag.Grid.Cells
            .Where(c => !c.IsEmpty)
            .Select(c => c.Stack!)
            .Except(Current.RootBag.Grid.Cells.Where(c => !c.IsEmpty).Select(c => c.Stack!))
            .FirstOrDefault();
        return ApplyResult(result, () =>
            newItem != null ? $"Acquire: +1 {newItem.ItemType.Name}" : "Acquire: added random item");
    }

    // --- DSL dispatch ---

    /// <summary>
    /// Executes a DSL expression string. The expression is parsed, run through the
    /// interpreter, and the resulting state change is applied with undo/logging/ticking.
    /// One undo snapshot per Execute call (matches user intent).
    /// </summary>
    public GameSession Execute(string dslExpression)
    {
        OpResult opResult;
        try
        {
            opResult = DslInterpreter.RunProgram(Current, dslExpression);
        }
        catch (InvalidOperationException ex)
        {
            return this with { ActionLog = ActionLog.Add($"FAILED: {dslExpression} — {ex.Message}") };
        }

        // Log accumulated errors but still apply state changes (partial success)
        var logSuffix = opResult.IsOk ? "" : $" — {string.Join("; ", opResult.Errors)}";

        // Convert to ToolResult-style flow for ApplyResult
        var toolResult = opResult.State == Current
            ? ToolResult.Ok(Current)
            : ToolResult.Ok(opResult.State);

        return ApplyResult(toolResult, () => dslExpression + logSuffix);
    }

    // --- Private helpers ---

    /// <summary>
    /// Applies a ToolResult: if successful and state changed, push to undo stack and log.
    /// If failed, log the error. If no-op success (state unchanged), don't push or log.
    /// </summary>
    private GameSession ApplyResult(ToolResult result, Func<string> formatLog)
    {
        if (!result.Success)
        {
            var errorLog = $"FAILED: {formatLog()} — {result.Error}";
            return this with { ActionLog = ActionLog.Add(errorLog) };
        }

        // No-op success: state didn't change, don't push undo
        if (result.State == Current)
            return this;

        // In rogue mode, tick facilities and plants after each action. In realtime, ticks are external.
        GameState newState;
        ImmutableList<string> completionLogs;
        if (TickMode == TickMode.Rogue)
        {
            (newState, completionLogs) = TickFacilities(result.State);
            newState = PlantLogic.TickPlants(newState);
        }
        else
        {
            (newState, completionLogs) = (result.State, ImmutableList<string>.Empty);
        }

        var newStack = PushWithLimit(UndoStack, Current);
        var newLog = ActionLog.Add(formatLog());
        foreach (var log in completionLogs)
            newLog = newLog.Add(log);

        return this with
        {
            Current = newState,
            UndoStack = newStack,
            ActionLog = newLog,
            TickCount = TickCount + 1
        };
    }

    /// <summary>
    /// Ticks all facility bags found via the BagRegistry.
    /// Updates each facility in-place within the bag tree.
    /// Uses FacilityRecipeMap when available, falls back to RecipeRegistry.
    /// Returns the updated state and any craft completion log messages.
    /// </summary>
    private (GameState State, ImmutableList<string> CompletionLogs) TickFacilities(GameState state)
    {
        var logs = ImmutableList<string>.Empty;

        if (Recipes.IsDefaultOrEmpty)
            return (state, logs);

        var facilities = state.Store.Facilities.ToList();
        if (facilities.Count == 0)
            return (state, logs);

        foreach (var facility in facilities)
        {
            var facilityRecipes = GetRecipesForFacility(facility.EnvironmentType);
            if (facilityRecipes.Count == 0)
                continue;

            // Read current progress from owning ItemStack's properties
            var ownerInfo = state.Store.GetOwnerOf(facility.Id);
            int currentProgress = 0;
            if (ownerInfo is not null)
            {
                var parentBag = state.Store.GetById(ownerInfo.ParentBagId);
                var ownerStack = parentBag?.Grid.GetCell(ownerInfo.CellIndex).Stack;
                currentProgress = ownerStack?.GetInt("Progress") ?? 0;
            }

            var wasCrafting = facility.FacilityState?.RecipeId;
            var (ticked, newProgress, newBags) = FacilityLogic.Tick(facility, currentProgress, facilityRecipes);
            if (ticked == facility && newProgress == currentProgress)
                continue;

            // Detect craft completion: was crafting, now reset to null
            if (wasCrafting is not null && ticked.FacilityState?.RecipeId is null)
            {
                var recipe = facilityRecipes.FirstOrDefault(r => r.Id == wasCrafting);
                var recipeName = recipe?.Name ?? wasCrafting;
                logs = logs.Add($"✦ {facility.EnvironmentType} crafted: {recipeName}");
            }

            // Register any newly created bags from the output factory
            if (newBags.Count > 0)
                state = state with { Store = state.Store.AddRange(newBags) };

            // Update both the facility bag and the owning stack's progress property
            var progressValue = newProgress;
            state = state.ReplaceBagById(facility.Id, ticked,
                stack => stack.WithProperty("Progress", new IntValue(progressValue)));
        }

        return (state, logs);
    }

    /// <summary>
    /// Public tick method for realtime mode. Advances all facilities by one tick.
    /// Returns the updated session with new state and incremented tick count.
    /// </summary>
    public GameSession Tick()
    {
        var tickedState = Current;
        var completionLogs = ImmutableList<string>.Empty;
        if (!Recipes.IsDefaultOrEmpty)
            (tickedState, completionLogs) = TickFacilities(Current);
        tickedState = PlantLogic.TickPlants(tickedState);
        if (tickedState == Current)
            return this;

        var newStack = PushWithLimit(UndoStack, Current);
        var newLog = ActionLog;
        foreach (var log in completionLogs)
            newLog = newLog.Add(log);
        if (completionLogs.Count == 0)
            newLog = newLog.Add($"Tick #{TickCount + 1}");

        return this with
        {
            Current = tickedState,
            UndoStack = newStack,
            ActionLog = newLog,
            TickCount = TickCount + 1
        };
    }

    /// <summary>
    /// Returns recipes applicable to a facility by environment type.
    /// Uses FacilityRecipeMap when available, falls back to RecipeRegistry.
    /// </summary>
    private IReadOnlyList<Recipe> GetRecipesForFacility(string environmentType)
    {
        if (FacilityRecipeMap is not null &&
            FacilityRecipeMap.TryGetValue(environmentType, out var recipeIds))
        {
            var recipesById = Recipes.ToDictionary(r => r.Id);
            return recipeIds
                .Where(id => recipesById.ContainsKey(id))
                .Select(id => recipesById[id])
                .ToList();
        }

        // Legacy fallback
        return Data.RecipeRegistry.GetRecipesForFacility(environmentType, Recipes);
    }

    /// <summary>
    /// Pushes a state onto the undo stack, trimming oldest entries if over max depth.
    /// </summary>
    private ImmutableStack<GameState> PushWithLimit(ImmutableStack<GameState> stack, GameState state)
    {
        var newStack = stack.Push(state);
        var depth = newStack.Count();
        if (depth <= MaxUndoDepth)
            return newStack;

        // Trim: convert to list, take most recent MaxUndoDepth, rebuild stack
        var items = new List<GameState>();
        var current = newStack;
        while (!current.IsEmpty)
        {
            items.Add(current.Peek());
            current = current.Pop();
        }
        // items[0] is newest, items[^1] is oldest — keep first MaxUndoDepth
        var trimmed = items.Take(MaxUndoDepth).Reverse();
        var rebuilt = ImmutableStack<GameState>.Empty;
        foreach (var item in trimmed)
            rebuilt = rebuilt.Push(item);
        return rebuilt;
    }

    private static string FormatGrabLog(ItemStack? item, Position pos) =>
        item != null
            ? $"Grab: {item.Count} {item.ItemType.Name} from ({pos.Row},{pos.Col})"
            : $"Grab: empty cell at ({pos.Row},{pos.Col})";

    private static string FormatDropLog(IReadOnlyList<ItemStack> items, Position pos) =>
        items.Count > 0
            ? $"Drop: {string.Join(", ", items.Select(i => $"{i.Count} {i.ItemType.Name}"))} at ({pos.Row},{pos.Col})"
            : $"Drop: empty hand at ({pos.Row},{pos.Col})";

    private static string FormatSplitLog(ItemStack? item) =>
        item != null
            ? $"Split: {item.Count} {item.ItemType.Name} → {(int)Math.Ceiling(item.Count / 2.0)}/{item.Count / 2}"
            : "Split: empty cell";

    private static string FormatModalSplitLog(ItemStack? item, int leftCount) =>
        item != null
            ? $"Split: {item.Count} {item.ItemType.Name} → {leftCount}/{item.Count - leftCount}"
            : "Split: empty cell";
}
