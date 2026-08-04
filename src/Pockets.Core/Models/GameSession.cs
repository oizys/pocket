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
/// <summary>
/// Transient in-progress state for the inline split editor. When non-null the
/// session is in "split mode": arrow keys adjust the grab count, Enter commits
/// (calling ToolModalSplit with the corresponding leftCount), Esc cancels.
/// </summary>
public record SplitModeState(
    LocationId Location,
    Position CellPosition,
    int GrabCount,
    int StackTotal);

/// <summary>
/// Transient modal state for the recipe menu (playtest feature, 2026-08-04): a proper modal list that
/// REPLACES the old R-to-cycle affordance. Opened on a facility (the Crafting Table), it lists every
/// recipe that facility can currently build (KnownRecipes ∩ the loaded set). ↑/↓ move
/// <see cref="SelectedIndex"/>, Enter selects (sets the facility's active recipe), Esc/Q closes.
/// Lives on <see cref="GameSession"/> like <see cref="SplitModeState"/> — a modal-lite the controller
/// owns keys for — and is projected into the view-model so both frontends render an identical modal and
/// the parity stream diffs it. Aaron reversed the old no-modal-dialogs rule for this (see drift report).
/// </summary>
public record RecipeMenuState(
    Guid FacilityBagId,
    string FacilityEnvironment,
    ImmutableArray<string> RecipeIds,
    ImmutableArray<string> RecipeNames,
    int SelectedIndex);

public record GameSession(
    GameState Current,
    ImmutableStack<GameState> UndoStack,
    ImmutableList<string> ActionLog,
    ImmutableArray<Recipe> Recipes = default,
    ImmutableDictionary<string, ImmutableArray<string>>? FacilityRecipeMap = null,
    TickMode TickMode = TickMode.Rogue,
    int TickCount = 0,
    int MaxUndoDepth = 1000,
    SplitModeState? SplitMode = null,
    DialogueBook? Book = null,
    TimeSpan Elapsed = default,
    RecipeMenuState? RecipeMenu = null)
{
    /// <summary>
    /// Accumulated in-game time (Slice 5 clock). Mirrors the injected <see cref="IGameClock"/> via
    /// <see cref="GameController.SyncClock"/>; the parity harness advances it only through scripted
    /// <c>advanceTime</c> steps (never the wall clock). Serialized as the clock readout; monotonic
    /// (carried forward across undo like <see cref="TickCount"/>).
    /// </summary>
    public GameSession WithElapsed(TimeSpan elapsed) =>
        elapsed == Elapsed ? this : this with { Elapsed = elapsed };

    /// <summary>
    /// The dialogue beat definitions in play (static content, like <see cref="Recipes"/>). Empty for
    /// non-demo profiles; the demo profile loads it from <c>/data</c>. Runtime progression lives on
    /// <see cref="GameState.Dialogue"/>.
    /// </summary>
    public DialogueBook Beats => Book ?? DialogueBook.Empty;
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
            // Dialogue progression is monotonic and never rewinds (see DialogueState): carry the
            // live dialogue substate forward onto the restored state so a beat can't un-fire.
            Current = previousState with { Dialogue = Current.Dialogue },
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
    public GameSession MoveCursorAt(LocationId locId, Direction direction) =>
        TryMoveCursorAt(locId, direction).Session;

    /// <summary>
    /// Moves the cursor one step at the given location, returning whether the move was refused by an
    /// unnavigable tree cell (Slice 6). A move whose target is a <see cref="TreeFrame"/> cell leaves the
    /// cursor exactly where it was (position unchanged — the checkpoint assert) and records a bump: the
    /// <see cref="DialogueState.TreeBumpCount"/> counter advances and any <see cref="DialogueTriggerKind.NthTreeBump"/>
    /// beat whose threshold the new count reaches fires once. The <c>TreeBumped</c> flag lets the
    /// controller queue the one-shot <see cref="FeedbackPulse.Bump"/> — the same split the failed-peek
    /// affordance uses (transient cue on the controller, monotonic dialogue on the session). Not undoable.
    /// </summary>
    public (GameSession Session, bool TreeBumped) TryMoveCursorAt(LocationId locId, Direction direction)
    {
        var loc = Current.Locations.TryGet(locId);
        if (loc is null) return (this, false);

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
        if (activeBag is null) return (this, false);

        var newCursor = loc.Cursor.Move(direction, activeBag.Grid.Rows, activeBag.Grid.Columns);

        // Unnavigable (tree) target: refuse the move. The cursor stays put; a bump is recorded and the
        // axe-absence beat fires on the Nth bump. Guard on an actual position change so a wrap no-op on a
        // 1-wide/1-tall grid is never mistaken for a bump.
        if (newCursor.Position != loc.Cursor.Position
            && activeBag.Grid.GetCell(newCursor.Position).IsUnnavigable)
            return (RecordTreeBump(), true);

        var newLoc = loc with { Cursor = newCursor };
        var moved = this with { Current = Current with { Locations = Current.Locations.Set(locId, newLoc) } };

        // Inspecting = the inventory (B) cursor resting on an item. It reveals the description pane
        // (first rest) and feeds the Nth-unique-inspect dialogue condition. Not undoable — moves
        // never are — and inert while a dialogue is blocking the world.
        return (locId == LocationId.B ? moved.EvaluateCursorRest() : moved, false);
    }

    /// <summary>
    /// Records a tree bump: advances <see cref="DialogueState.TreeBumpCount"/> and enqueues any
    /// <see cref="DialogueTriggerKind.NthTreeBump"/> beat whose threshold the new count reaches (id-ordered,
    /// fire-once). Touches only the dialogue substate — the cursor and world are untouched, so a refused
    /// move stays a true no-op on the census. Inert while a beat is already showing (movement is swallowed
    /// then anyway). Same monotonic posture as <see cref="EvaluateCursorRest"/> / <see cref="FireFailedPeek"/>.
    /// </summary>
    private GameSession RecordTreeBump()
    {
        var state = Current;
        if (state.Dialogue.IsActive)
            return this;

        var dialogue = state.Dialogue.Bump();
        dialogue = EnqueueThresholdBeats(dialogue, DialogueTriggerKind.NthTreeBump, dialogue.TreeBumpCount);

        return this with { Current = state with { Dialogue = dialogue } };
    }

    /// <summary>
    /// Enqueues every beat of a threshold-counter trigger whose <see cref="DialogueTrigger.Threshold"/>
    /// the counter has just reached and which hasn't already fired — the shared "fire the beat once the
    /// Nth event lands, once" idiom behind both <see cref="DialogueTriggerKind.NthUniqueInspect"/> and
    /// <see cref="DialogueTriggerKind.NthTreeBump"/>. Id-ordered (deterministic) and fire-once via
    /// <see cref="DialogueState.Enqueue"/>.
    /// </summary>
    private DialogueState EnqueueThresholdBeats(DialogueState dialogue, DialogueTriggerKind kind, int count)
    {
        foreach (var beat in Beats.WithTrigger(kind))
            if (beat.Trigger.Threshold == count && !dialogue.HasFired(beat.Id))
                dialogue = dialogue.Enqueue(beat.Id);
        return dialogue;
    }

    /// <summary>
    /// Evaluates cursor-rest narrative triggers after a B-cursor move: if the cursor now rests on an
    /// item and no dialogue is blocking, fires <see cref="UiTrigger.FirstCursorRest"/> (description
    /// pane) and records a unique inspection, firing any <see cref="DialogueTriggerKind.NthUniqueInspect"/>
    /// beat whose threshold the new unique count reaches. Fire-once and dedup by item type mean a
    /// rapid back-and-forth scan can never double-fire a beat. Returns the same session when nothing changes.
    /// </summary>
    private GameSession EvaluateCursorRest()
    {
        var state = Current;
        if (state.Dialogue.IsActive)
            return this; // the world is blocked while a beat is showing

        var cell = state.CurrentCell;
        if (cell.IsEmpty)
            return this;

        var ui = state.Ui.Fire(UiTrigger.FirstCursorRest);
        // Resting on the Shrine's Clock slot is where the player notices "it was always ticking"
        // (journey 15:00): the clock readout materializes. Deterministic — keyed to the clock glyph.
        if (cell.Frame is FeatureSlotFrame { Glyph: FeatureSlotFrame.ClockGlyph })
            ui = ui.Fire(UiTrigger.NoticeClock);
        var dialogue = state.Dialogue;

        var typeName = cell.Stack!.ItemType.Name;
        if (!dialogue.InspectedItems.Contains(typeName))
        {
            dialogue = dialogue.Inspect(typeName);
            dialogue = EnqueueThresholdBeats(dialogue, DialogueTriggerKind.NthUniqueInspect, dialogue.UniqueInspectCount);
        }

        if (ReferenceEquals(ui, state.Ui) && dialogue == state.Dialogue)
            return this;
        return this with { Current = state with { Ui = ui, Dialogue = dialogue } };
    }

    /// <summary>
    /// Fires the first-failed-peek narrative hook: enqueues every
    /// <see cref="DialogueTriggerKind.FirstFailedPeek"/> beat not yet fired (id-ordered, deterministic).
    /// Monotonic and NOT undoable — same posture as <see cref="EvaluateCursorRest"/>: dialogue never
    /// rewinds and fire-once (<see cref="DialogueState.Enqueue"/> guards on <see cref="DialogueState.FiredBeats"/>)
    /// means a second refused peek can't re-show the beat. Touches only the dialogue substate — the
    /// world, cursor, panels, and item census are untouched, so a refused peek stays a true no-op.
    /// Returns the same session when nothing changes (all such beats already fired, or none authored).
    /// </summary>
    public GameSession FireFailedPeek()
    {
        var state = Current;
        if (state.Dialogue.IsActive)
            return this; // a beat is already showing; the world (and this hook) is blocked

        var dialogue = state.Dialogue;
        foreach (var beat in Beats.WithTrigger(DialogueTriggerKind.FirstFailedPeek))
            dialogue = dialogue.Enqueue(beat.Id);

        return dialogue == state.Dialogue ? this : this with { Current = state with { Dialogue = dialogue } };
    }

    /// <summary>
    /// Fires the capacity-absence narrative hook (Slice 5, journey 21:00 — "Which one has space? I'd
    /// have to look inside every single one."): enqueues every
    /// <see cref="DialogueTriggerKind.FirstPeekWhileCarrying"/> beat not yet fired. Called by the
    /// controller when a look-in peek SUCCEEDS while the hand is carrying something — the mechanical
    /// rhyme for "peering into bags to find room". Fire-once and monotonic, same posture as
    /// <see cref="FireFailedPeek"/>; touches only the dialogue substate.
    /// </summary>
    public GameSession FirePeekWhileCarrying()
    {
        var state = Current;
        if (state.Dialogue.IsActive)
            return this;

        var dialogue = state.Dialogue;
        foreach (var beat in Beats.WithTrigger(DialogueTriggerKind.FirstPeekWhileCarrying))
            dialogue = dialogue.Enqueue(beat.Id);

        return dialogue == state.Dialogue ? this : this with { Current = state with { Dialogue = dialogue } };
    }

    /// <summary>
    /// Advances the active dialogue by one line, or dismisses it when past the last line. On dismiss
    /// the beat may materialize chrome (the opening beat reveals the grid — the world fades in as the
    /// box drops). Not undoable: dialogue progression never rewinds. No-op when nothing is showing.
    /// </summary>
    public GameSession AdvanceDialogue()
    {
        var dialogue = Current.Dialogue;
        if (!dialogue.IsActive)
            return this;

        var beat = Beats.Get(dialogue.ActiveBeatId!);
        var lineCount = beat?.Lines.Length ?? 1;
        var (advanced, dismissed) = dialogue.Advance(lineCount);

        var state = Current with { Dialogue = advanced };
        if (dismissed is not null && beat?.Reveals is ChromeElement reveal)
            state = state with { Ui = state.Ui.With(reveal) };

        var log = dismissed is not null ? $"Dialogue: dismissed {dismissed}" : "Dialogue: advance";
        return this with { Current = state, ActionLog = ActionLog.Add(log) };
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
    /// Fixed-inventory pickup (Slice 3): removes the resting item under the B cursor and acquires it
    /// into the toolbar's first available slot — the demo's "pickup flies to the toolbar" routing.
    /// Uses <see cref="GameState.AcquireIntoBagRecursive"/>, so once the toolbar's own slots are full a
    /// pickup lands <i>inside</i> a non-full toolbar bag (the carrying-capacity mechanic). The hand is
    /// untouched (it stays the in-flight cut buffer). Undoable; a full toolbar fails with the item left
    /// in place. The <see cref="UiTrigger.FirstPickup"/> that materializes the toolbar fires
    /// structurally in <see cref="ApplyUiTriggers"/> when the toolbar gains its first item.
    /// </summary>
    public GameSession ExecutePickupToToolbar()
    {
        var state = Current;
        var cell = state.CurrentCell;
        if (cell.IsEmpty || state.ToolbarBagId is not { } toolbarId)
            return ApplyResult(ToolResult.Ok(state), () => "Pickup: no-op");

        var item = cell.Stack!;
        var (afterAcquire, remaining) = state.AcquireIntoBagRecursive(toolbarId, item);

        var placed = item.Count - (remaining?.Count ?? 0);
        if (placed == 0)
            return ApplyResult(ToolResult.Fail(state, "Toolbar is full"),
                () => $"Pickup: {item.ItemType.Name} → toolbar");

        // Clear the source cell (or leave the unplaced remainder there when the toolbar only had room
        // for part of the stack). The toolbar lives in a different bag, so the acquire above never
        // touched B's active bag — we mutate it here on the post-acquire store.
        var sourceBag = afterAcquire.ActiveBag;
        var newCell = remaining is not null ? cell with { Stack = remaining } : cell with { Stack = null };
        var clearedGrid = sourceBag.Grid.SetCell(state.Cursor.Position, newCell);
        var newState = afterAcquire with
        {
            Store = afterAcquire.Store.Set(afterAcquire.ActiveBagId, sourceBag with { Grid = clearedGrid })
        };

        return ApplyResult(ToolResult.Ok(newState),
            () => $"Pickup: {placed} {item.ItemType.Name} → toolbar");
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
    /// Enters the inline split editor for the cursor cell at the given location.
    /// No-op if the cell is empty or has only one item (can't split a single item).
    /// Defaults GrabCount to half the stack so the initial split is even.
    /// </summary>
    public GameSession BeginSplit(LocationId loc)
    {
        var locInfo = Current.Locations.TryGet(loc);
        if (locInfo is null) return this;

        var bagId = locInfo.BagId;
        foreach (var entry in locInfo.Breadcrumbs.Reverse())
        {
            var b = Current.Store.GetById(bagId);
            if (b is null) break;
            var c = b.Grid.GetCell(entry.CellIndex);
            if (c.Stack?.ContainedBagId is not { } childId) break;
            bagId = childId;
        }
        var bag = Current.Store.GetById(bagId);
        if (bag is null) return this;

        var cell = bag.Grid.GetCell(locInfo.Cursor.Position);
        if (cell.IsEmpty || cell.Stack!.Count <= 1)
            return this;

        var total = cell.Stack.Count;
        var initialGrab = total / 2;
        return this with
        {
            SplitMode = new SplitModeState(loc, locInfo.Cursor.Position, initialGrab, total)
        };
    }

    /// <summary>
    /// Adjusts the active split's GrabCount by `delta`, clamping to [1, StackTotal - 1].
    /// No-op when not in split mode.
    /// </summary>
    public GameSession AdjustSplit(int delta)
    {
        if (SplitMode is null) return this;
        var next = SplitMode.GrabCount + delta;
        if (next < 1) next = 1;
        if (next > SplitMode.StackTotal - 1) next = SplitMode.StackTotal - 1;
        if (next == SplitMode.GrabCount) return this;
        return this with { SplitMode = SplitMode with { GrabCount = next } };
    }

    /// <summary>
    /// Commits the active split by calling ExecuteModalSplit at the split location.
    /// Clears the SplitMode on the resulting session.
    /// </summary>
    public GameSession CommitSplit()
    {
        if (SplitMode is null) return this;
        var leftCount = SplitMode.StackTotal - SplitMode.GrabCount;
        var loc = SplitMode.Location;
        var afterSplit = RunAt(loc, s => s.ExecuteModalSplit(leftCount));
        return afterSplit with { SplitMode = null };
    }

    /// <summary>
    /// Cancels the active split with no state change beyond clearing SplitMode.
    /// </summary>
    public GameSession CancelSplit() => this with { SplitMode = null };

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
    /// Cycles the active recipe on the facility at cursor to the next in the list, rebuilding its slot
    /// filters and re-homing whatever was in the slots. The Crafting Table uses the modal recipe menu
    /// (<see cref="OpenRecipeMenu"/>) instead of cycling; this remains for legacy filtered-slot facilities.
    ///
    /// Conservation is guaranteed: the dumped slot items are re-homed via <see cref="ApplyRecipeSwitch"/>,
    /// which places them into the root bag or REFUSES the switch — it never discards. (The playtest
    /// item-deletion bug was exactly this: the old code discarded <see cref="Grid.AcquireItems"/>'s
    /// unplaced remainder, silently destroying the slot items whenever the root bag had no room.)
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
        return ApplyRecipeSwitch(activeBag.Id, updated, dumped,
            () => $"CycleRecipe: switched to {updated.FacilityState!.ActiveRecipeId}");
    }

    /// <summary>
    /// Installs a rebuilt facility (new active recipe + slot layout) and re-homes the items it dumped from
    /// its slots, with a strict <b>no-loss</b> guarantee. The dumped stacks are acquired into the root
    /// (home) bag <i>first</i>; if any stack cannot be fully placed there, the whole switch is REFUSED —
    /// nothing is mutated and the old facility keeps its items — rather than destroying them. Home is a
    /// large 8×4 grid so a refusal is the rare full-inventory edge; the point is that a full inventory can
    /// never again turn a recipe switch into an item sink (the 2026-08-04 playtest deletion bug).
    /// </summary>
    private GameSession ApplyRecipeSwitch(
        Guid facilityId, Bag rebuiltFacility, IReadOnlyList<ItemStack> dumped, Func<string> log)
    {
        var rootGrid = Current.RootBag.Grid;
        foreach (var stack in dumped)
        {
            var (grid, unplaced) = rootGrid.AcquireItems(new[] { stack });
            if (unplaced.Count > 0)
                return this with { ActionLog = ActionLog.Add("FAILED: recipe switch — no room to set down the current items") };
            rootGrid = grid;
        }

        var newState = Current with
        {
            Store = Current.Store.Set(Current.RootBagId, Current.RootBag with { Grid = rootGrid })
        };
        newState = newState.ReplaceBagById(facilityId, rebuiltFacility,
            stack => stack.WithProperty("Progress", new IntValue(0)));

        var newStack = PushWithLimit(UndoStack, Current);
        return this with
        {
            Current = newState,
            UndoStack = newStack,
            ActionLog = ActionLog.Add(log())
        };
    }

    // ==================== Recipe menu (modal) ====================

    /// <summary>
    /// Opens the modal recipe menu on the facility active at <paramref name="loc"/> (the focused panel).
    /// Lists exactly the recipes the facility can build (KnownRecipes ∩ the loaded set for the Crafting
    /// Table). No-op (logged) when the focused bag is not a facility. The selection starts on the
    /// facility's current active recipe when it has one. Opening pushes no undo snapshot — it is UI.
    /// </summary>
    public GameSession OpenRecipeMenu(LocationId loc)
    {
        var facility = Current.ActiveBagAt(loc);
        if (facility.FacilityState is null)
            return this with { ActionLog = ActionLog.Add("Recipe menu: not a facility") };

        var recipes = GetRecipesForFacility(facility.EnvironmentType);
        var ids = recipes.Select(r => r.Id).ToImmutableArray();
        var names = recipes.Select(r => r.Name).ToImmutableArray();

        var active = facility.FacilityState.ActiveRecipeId;
        var selected = active is not null ? ids.IndexOf(active) : 0;
        if (selected < 0) selected = 0;

        return this with
        {
            RecipeMenu = new RecipeMenuState(facility.Id, facility.EnvironmentType, ids, names, selected),
            ActionLog = ActionLog.Add($"Recipe menu: opened ({ids.Length} craftable)")
        };
    }

    /// <summary>Moves the recipe-menu selection by <paramref name="delta"/>, clamped. No-op when closed/empty.</summary>
    public GameSession MoveRecipeMenu(int delta)
    {
        if (RecipeMenu is not { } menu || menu.RecipeIds.IsEmpty) return this;
        var next = menu.SelectedIndex + delta;
        if (next < 0) next = 0;
        if (next > menu.RecipeIds.Length - 1) next = menu.RecipeIds.Length - 1;
        return next == menu.SelectedIndex ? this : this with { RecipeMenu = menu with { SelectedIndex = next } };
    }

    /// <summary>Closes the recipe menu with no state change (Esc/Q).</summary>
    public GameSession CloseRecipeMenu() =>
        RecipeMenu is null ? this : this with { RecipeMenu = null };

    /// <summary>
    /// Confirms the recipe-menu selection: sets the facility's active recipe (via <see cref="SetRecipeOn"/>)
    /// and closes the menu. A closed/empty menu just closes.
    /// </summary>
    public GameSession ConfirmRecipeMenu()
    {
        if (RecipeMenu is not { } menu) return this;
        var closed = this with { RecipeMenu = null };
        if (menu.RecipeIds.IsEmpty) return closed;
        var recipeId = menu.RecipeIds[Math.Clamp(menu.SelectedIndex, 0, menu.RecipeIds.Length - 1)];
        return closed.SetRecipeOn(menu.FacilityBagId, recipeId);
    }

    /// <summary>
    /// Sets a facility's active recipe by id (the modal recipe menu's "select"). The Crafting Table keeps
    /// generic (unfiltered) input slots, so setting a recipe only pins <see cref="FacilityState.ActiveRecipeId"/>
    /// and resets any in-progress craft — it never rebuilds slots or moves items, so this path can never
    /// destroy anything (conservation is trivial). Progress resets so switching mid-craft restarts cleanly.
    /// </summary>
    private GameSession SetRecipeOn(Guid facilityId, string recipeId)
    {
        var facility = Current.Store.GetById(facilityId);
        if (facility?.FacilityState is null)
            return this with { ActionLog = ActionLog.Add("SetRecipe: not a facility") };

        var updated = facility with
        {
            FacilityState = facility.FacilityState with { ActiveRecipeId = recipeId, RecipeId = null }
        };
        var newState = Current.ReplaceBagById(facilityId, updated,
            stack => stack.WithProperty("Progress", new IntValue(0)));

        var newStack = PushWithLimit(UndoStack, Current);
        return this with
        {
            Current = newState,
            UndoStack = newStack,
            ActionLog = ActionLog.Add($"SetRecipe: {recipeId}")
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

        // Chrome-as-state + structural beats + known-recipes + minimap zones: the shared transition
        // hooks. Centralized here because every state mutation (tool actions, panel open/close,
        // enter/leave) flows through ApplyResult, so no per-tool wiring is needed and the demo
        // profile's chrome grows identically on every frontend/driver. The same hooks run after a
        // scripted facility tick (see Tick) so a timed craft's completion fires identically.
        newState = RunTransitionHooks(Current, newState);

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
    /// Reveals chrome for the gameplay transitions between two states. Structural (not per-tool):
    ///   • hand OR toolbar gained an item ⇒ FirstPickup   (Toolbar)
    ///   • breadcrumb depth increased     ⇒ FirstEnter    (Breadcrumbs)
    ///   • a C/W look-in panel opened     ⇒ FirstPeek     (LookInOverlay)
    /// Each Fire is idempotent, so this runs on every successful action and only the FIRST
    /// occurrence flips a flag. The toolbar arm makes the demo's pickup-to-toolbar routing
    /// (Slice 3) light the toolbar the same way a classic grab-into-hand does. Triggers for
    /// unbuilt mechanics (Shrine, compass, cores) have plumbing + tests but no detector here yet.
    /// </summary>
    private static GameState ApplyUiTriggers(GameState before, GameState after)
    {
        var ui = after.Ui;

        if ((!before.HasItemsInHand && after.HasItemsInHand)
            || ToolbarItemCount(after) > ToolbarItemCount(before))
            ui = ui.Fire(UiTrigger.FirstPickup);

        if (after.BreadcrumbStack.Count() > before.BreadcrumbStack.Count())
            ui = ui.Fire(UiTrigger.FirstEnter);

        if (PanelNewlyOpen(before, after, LocationId.C) || PanelNewlyOpen(before, after, LocationId.W))
            ui = ui.Fire(UiTrigger.FirstPeek);

        // Crossing into the Shrine bag materializes the Shrine slots view (journey 12:00).
        if (JustEnteredShrine(before, after))
            ui = ui.Fire(UiTrigger.EnterShrine);

        // A newly-locked feature slot = a core was just slotted: game-wide fullness pips turn on
        // (journey 28:00 — the first unlock that rewires rendering everywhere).
        if (LockedFeatureSlotCount(after) > LockedFeatureSlotCount(before))
            ui = ui.Fire(UiTrigger.CoreSlotted);

        // A Crafting Table just began crafting (RecipeId null→set) ⇒ FirstTimedAction (journey 24:00):
        // the action-queue panel materializes to show the timed work. Scoped to the Crafting Table
        // because that is the demo's designated first *shown* timed craft (the earlier Slice-3 Workshop
        // craft teaches toolbar-sourced grab-for-move, not the queue). Detected structurally so it fires
        // the same way whether the craft starts during a scripted tick (advanceTime) or any other path.
        if (CraftingTableActiveCount(after) > CraftingTableActiveCount(before))
            ui = ui.Fire(UiTrigger.FirstTimedAction);

        // A Quiet Compass just appeared in the world (craft complete) ⇒ CompassCrafted (journey 26:00):
        // the minimap/radar materializes. Counting the item is robust to where the compass lands.
        if (CompassCount(after) > CompassCount(before))
            ui = ui.Fire(UiTrigger.CompassCrafted);

        return ReferenceEquals(ui, after.Ui) ? after : after with { Ui = ui };
    }

    /// <summary>A facility currently mid-craft, projected for the action-queue chrome (Slice 7).</summary>
    public record ActiveCraft(string Facility, string RecipeId, string? Name, int Progress, int Duration);

    /// <summary>
    /// All facilities in a deterministic, GUID-free order: env → active recipe → owning cell index (all
    /// pure functions of state). The single source of facility ordering for both the tick loop and the
    /// action-queue projection, so the completion log and the queue panel never disagree — bag ids are
    /// process-fresh GUIDs, so the store's dictionary order would otherwise diverge across drivers.
    /// </summary>
    private static IEnumerable<Bag> OrderedFacilities(GameState state) =>
        state.Store.Facilities
            .OrderBy(f => f.EnvironmentType, StringComparer.Ordinal)
            .ThenBy(f => f.FacilityState?.ActiveRecipeId ?? f.FacilityState?.RecipeId ?? "", StringComparer.Ordinal)
            .ThenBy(f => state.Store.GetOwnerOf(f.Id)?.CellIndex ?? 0);

    /// <summary>
    /// The action-queue rows (Slice 7): every facility mid-craft, in canonical order, each with its
    /// current progress and the active recipe's duration/name. The view-model serializer and both
    /// frontends read this one projection, so the TUI queue, the Godot count, and the parity stream
    /// can never drift.
    /// </summary>
    public IReadOnlyList<ActiveCraft> ActiveCrafts()
    {
        var byId = Recipes.IsDefaultOrEmpty
            ? null
            : Recipes.GroupBy(r => r.Id).ToDictionary(g => g.Key, g => g.First());

        var rows = new List<ActiveCraft>();
        foreach (var f in OrderedFacilities(Current))
        {
            if (f.FacilityState?.RecipeId is not { } recipeId) continue;
            var progress = Current.Store.GetOwnerStack(f.Id)?.GetInt("Progress") ?? 0;
            var recipe = byId is not null && byId.TryGetValue(recipeId, out var r) ? r : null;
            rows.Add(new ActiveCraft(f.EnvironmentType, recipeId, recipe?.Name, progress, recipe?.Duration ?? 0));
        }
        return rows;
    }

    /// <summary>Number of Crafting Table facilities currently mid-craft (a non-null active RecipeId).</summary>
    private static int CraftingTableActiveCount(GameState state) =>
        state.Store.All.Count(b =>
            b.EnvironmentType == GameState.CraftingTableEnvironment && b.FacilityState?.RecipeId is not null);

    /// <summary>Total count of Quiet Compass items anywhere in the store (the CompassCrafted signal).</summary>
    private static int CompassCount(GameState state) =>
        state.Store.All.Sum(b => b.Grid.Cells
            .Where(c => c.Stack?.ItemType.Name == GameState.QuietCompassItem)
            .Sum(c => c.Stack!.Count));

    /// <summary>
    /// Lights a minimap wedge when the player crosses the threshold INTO a wilderness (an
    /// <see cref="Bag.EnterOnly"/> bag) they have not entered before. Keyed to a breadcrumb-depth
    /// increase (a real enter, not a look-in), so peeking a ruin or entering the plain Shrine/pouch
    /// never lights a wedge. Distinct + monotonic (a re-entry never double-counts). Returns the same
    /// state when nothing new is reached.
    /// </summary>
    private static GameState RegisterZoneEntry(GameState before, GameState after)
    {
        if (after.BreadcrumbStack.Count() <= before.BreadcrumbStack.Count())
            return after;
        if (!after.ActiveBag.EnterOnly)
            return after;
        var id = after.ActiveBagId;
        if (after.ZonesReached.Contains(id))
            return after;
        return after with { ZonesReached = after.ZonesReached.Add(id) };
    }

    /// <summary>
    /// Runs the shared state→state transition hooks after any successful mutation, whether from a
    /// player action (<see cref="ApplyResult"/>) or a scripted facility tick (<see cref="Tick"/>):
    /// chrome triggers, structural dialogue beats, known-recipe learning, and minimap zone entry.
    /// Centralizing them here means a craft that completes DURING a tick fires CompassCrafted/minimap
    /// exactly like a player-driven transition would.
    /// </summary>
    private GameState RunTransitionHooks(GameState before, GameState after)
    {
        after = ApplyUiTriggers(before, after);
        after = FireStructuralDialogue(before, after);
        after = RegisterKnownRecipes(after);
        after = RegisterZoneEntry(before, after);
        return after;
    }

    /// <summary>Enqueues the slotting-resolution beat when a feature slot is newly locked (core slotted).</summary>
    private GameState FireStructuralDialogue(GameState before, GameState after)
    {
        if (LockedFeatureSlotCount(after) <= LockedFeatureSlotCount(before))
            return after;

        var dialogue = after.Dialogue;
        foreach (var beat in Beats.WithTrigger(DialogueTriggerKind.CoreSlotted))
            dialogue = dialogue.Enqueue(beat.Id);
        return dialogue == after.Dialogue ? after : after with { Dialogue = dialogue };
    }

    /// <summary>
    /// Learns — and then <b>consumes</b> — every recipe-as-item the player is holding in a fixed inventory
    /// (the hand, the toolbar, and one level into a toolbar carrying bag). Learning a card adds its id to
    /// <see cref="GameState.KnownRecipes"/> (permanent, monotonic) and removes the card from its cell:
    /// <b>poof on learn</b> (playtest fix, 2026-08-04). A recipe item is any <see cref="ItemStack"/>
    /// carrying a <see cref="GameState.RecipeItemProperty"/>. This is a <i>sanctioned</i> census removal —
    /// the journey's conservation checks account for the exact card(s) that vanished (see the runner's
    /// <c>expectDelta</c>). Returns the same state when nothing new is learned.
    /// </summary>
    private static GameState RegisterKnownRecipes(GameState state)
    {
        var known = state.KnownRecipes;
        var store = state.Store;

        // Clear-and-learn one grid: any cell holding a recipe card learns its id and empties.
        (Bag, bool) LearnAndClear(Bag bag)
        {
            var changed = false;
            var builder = bag.Grid.Cells.ToBuilder();
            for (var i = 0; i < builder.Count; i++)
            {
                if (builder[i].Stack?.GetString(GameState.RecipeItemProperty) is not { } recipeId) continue;
                known = known.Add(recipeId);
                builder[i] = builder[i] with { Stack = null };
                changed = true;
            }
            return changed ? (bag with { Grid = bag.Grid with { Cells = builder.MoveToImmutable() } }, true) : (bag, false);
        }

        // Hand.
        var (newHand, handChanged) = LearnAndClear(state.HandBag);
        if (handChanged) store = store.Set(state.HandBagId, newHand);

        // Toolbar + one level into each toolbar carrying bag (matches the acquire/serializer nesting depth).
        if (state.ToolbarBagId is { } toolbarId && store.GetById(toolbarId) is { } toolbar)
        {
            foreach (var cell in toolbar.Grid.Cells)
                if (cell.Stack?.ContainedBagId is { } nestedId && store.GetById(nestedId) is { } nested)
                {
                    var (newNested, nestedChanged) = LearnAndClear(nested);
                    if (nestedChanged) store = store.Set(nestedId, newNested);
                }
            var (newToolbar, toolbarChanged) = LearnAndClear(store.GetById(toolbarId)!);
            if (toolbarChanged) store = store.Set(toolbarId, newToolbar);
        }

        if (ReferenceEquals(known, state.KnownRecipes) && ReferenceEquals(store, state.Store))
            return state;
        return state with { KnownRecipes = known, Store = store };
    }

    /// <summary>True when the active bag just became the Shrine (a fresh entry, not a re-render).</summary>
    private static bool JustEnteredShrine(GameState before, GameState after) =>
        after.ActiveBagId != before.ActiveBagId
        && after.ActiveBag.EnvironmentType == GameState.ShrineEnvironment;

    /// <summary>Total number of locked feature slots across every bag (Slice 5 slotting detector).</summary>
    private static int LockedFeatureSlotCount(GameState state) =>
        state.Store.All.Sum(b => b.Grid.Cells.Count(c => c.Frame is FeatureSlotFrame { IsLocked: true }));

    /// <summary>True when <paramref name="panel"/> is absent in <paramref name="before"/> and present in <paramref name="after"/>.</summary>
    private static bool PanelNewlyOpen(GameState before, GameState after, LocationId panel) =>
        !before.Locations.Has(panel) && after.Locations.Has(panel);

    /// <summary>Number of occupied top-level toolbar slots (0 when there is no toolbar).</summary>
    private static int ToolbarItemCount(GameState state) =>
        state.ToolbarBagId is { } id && state.Store.GetById(id) is { } bag
            ? bag.Grid.Cells.Count(c => !c.IsEmpty)
            : 0;

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

        var facilities = OrderedFacilities(state).ToList();
        if (facilities.Count == 0)
            return (state, logs);

        foreach (var facility in facilities)
        {
            var facilityRecipes = GetRecipesForFacility(facility.EnvironmentType);
            if (facilityRecipes.Count == 0)
                continue;

            // Read current progress from the owning ItemStack's properties (shared owner lookup).
            int currentProgress = state.Store.GetOwnerStack(facility.Id)?.GetInt("Progress") ?? 0;

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
        // A craft that starts/completes during this tick must reveal chrome (FirstTimedAction →
        // ActionQueue, CompassCrafted → Minimap) exactly as a player action would.
        tickedState = RunTransitionHooks(Current, tickedState);
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
        // The Crafting Table (Slice 7) is a generic assembler: its craftable set is exactly the
        // recipes the player KNOWS (KnownRecipes ∩ the loaded recipe set) — the Slice-6 deferred
        // wiring. Learn a recipe and the table can build it the moment its inputs match; an unknown
        // recipe (or a known one with no table inputs, e.g. the Iron-Ore-gated demo axe) never crafts.
        if (environmentType == GameState.CraftingTableEnvironment)
            return Recipes.Where(r => Current.KnownRecipes.Contains(r.Id)).ToList();

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
