using System.Collections.Immutable;
using Pockets.Core.Data;
using Pockets.Core.Sim;

namespace Pockets.Core.Models;

/// <summary>
/// Controls when facility ticks fire: on player action (rogue-like) or via external timer (realtime).
/// </summary>
public enum TickMode { Rogue, Realtime }

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


/// <summary>
/// The session shell around <see cref="Simulation.Step"/> (two-layer #23). It holds the L1 state
/// (<see cref="Current"/> + <see cref="TickCount"/>, <see cref="Elapsed"/>, <see cref="SplitMode"/>,
/// <see cref="RecipeMenu"/>, <see cref="Rng"/>) and the shell bookkeeping: the undo stack, the action log and
/// the intent log. Slice 1.3: <see cref="Apply"/> is its one public mutation entry — every change is an
/// <see cref="Intent"/> run through Step, and the shell writes the action-log line exactly as before (P1-D6),
/// pushes undo where the action was undoable, and appends the intent to <see cref="IntentLog"/>.
/// The old per-tool methods remain as <c>internal</c> wrappers for tests.
/// </summary>
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
    RecipeMenuState? RecipeMenu = null,
    SimRng? Rng = null)
{
    /// <summary>
    /// The intent log (Slice 1.3; A6 "intents are the only input"): every intent <see cref="Apply"/> was handed
    /// that changed the session, in order, with the step it landed in (<c>TickCount + 1</c> at the time).
    /// Re-applying it to a fresh session of the same profile reproduces the session exactly; an intent that
    /// changed nothing is left out, which replay never notices. Append-only; Slice 1.8 adds retention and
    /// undo truncation.
    /// </summary>
    public ImmutableList<(int Step, Intent Intent)> IntentLog { get; init; } = ImmutableList<(int Step, Intent Intent)>.Empty;

    /// <summary>
    /// The session's counter RNG (Slice 1.1, L1): every in-step random draw comes from it. A session
    /// created without one uses world seed 0; the demo profile seeds it from its init seed. Like
    /// <see cref="TickCount"/>, it is carried forward across undo (the step number keeps rising, so
    /// a re-done action draws fresh values). Not serialized in the VM.
    /// </summary>
    public SimRng WorldRng => Rng ?? DefaultRng;

    /// <summary>The RNG of a session created without a world seed.</summary>
    private static readonly SimRng DefaultRng = SimRng.Create(0);

    /// <summary>A copy of this session whose RNG is keyed on <paramref name="worldSeed"/>.</summary>
    public GameSession WithWorldSeed(ulong worldSeed) => this with { Rng = SimRng.Create(worldSeed) };

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

    // ==================== The shell: Apply ====================

    /// <summary>
    /// Applies one intent (the shell's dispatcher, the plan's <c>GameSession.Step(intents)</c>): runs it
    /// through <see cref="Simulation.Step"/> (a composite <see cref="Intent.Click"/> / <see cref="Intent.CommitSplit"/>
    /// as its sub-intents, one step each), writes the action log, pushes undo, and records the intent in
    /// <see cref="IntentLog"/>. Returns this same session when the intent changed nothing.
    /// </summary>
    public GameSession Apply(Intent intent) => ApplyTraced(intent).Session;

    /// <summary>
    /// <see cref="Apply"/>, also returning how the (last) step resolved — the controller reads it to move
    /// its panel focus and pick its status message. Null for <see cref="Intent.Undo"/> and empty composites.
    /// </summary>
    internal (GameSession Session, StepTrace? Trace) ApplyTraced(Intent intent)
    {
        var (next, trace) = Dispatch(intent);
        if (next == this)
            return (this, trace);
        return (next with { IntentLog = IntentLog.Add((TickCount + 1, intent)) }, trace);
    }

    /// <summary>Routes one intent: undo to the shell, a composite to its sub-intents, anything else to one step.</summary>
    private (GameSession Session, StepTrace? Trace) Dispatch(Intent intent)
    {
        if (intent is Intent.Undo)
            return (UndoCore(), null);

        var subs = Simulation.Expand(ToSimState(), intent);
        if (subs.Count == 1 && ReferenceEquals(subs[0], intent))
            return RunStep(intent);

        var session = this;
        StepTrace? last = null;
        foreach (var sub in subs)
            (session, last) = session.RunStep(sub);
        return (session, last);
    }

    /// <summary>
    /// Shell around one <see cref="Simulation.Step"/> of one intent. When the step changed state it counts
    /// it and writes the new L1 state back; then, by how the intent resolved, it pushes the undo snapshot and
    /// appends the action-log lines exactly as the pre-1.3 methods did: <c>FAILED: …</c> for a rejected tool,
    /// otherwise the action's line followed by any craft-completion lines.
    /// </summary>
    private (GameSession Session, StepTrace? Trace) RunStep(Intent intent)
    {
        var traces = new List<StepTrace>(1);
        var result = Simulation.StepTraced(ToSimState(), new[] { intent }, traces);
        var trace = traces.Count > 0 ? traces[^1] : null;
        var events = result.Events;
        var rejected = events.Length > 0 ? events[0] as SimEvent.IntentRejected : null;
        var changed = events.Length > 0 && rejected is null;

        var session = this;
        if (changed)
        {
            t_debugStepCount++;
            session = WithSimState(result.State);
        }
        if (trace is null)
            return (session, null);

        // Push the pre-step snapshot (a tool run at a panel pushes the swapped frame, as RunAt did).
        GameSession Undoable(GameState snapshot) => session with { UndoStack = PushWithLimit(UndoStack, snapshot) };
        GameSession Log(IEnumerable<string> lines) => session with { ActionLog = session.ActionLog.AddRange(lines) };
        IEnumerable<string> WithCompletions(string line) => new[] { line }.Concat(CompletionLogs(events, session.Current));

        switch (trace.Route)
        {
            case Route.Tick when changed:
            {
                session = Undoable(Current);
                var completions = CompletionLogs(events, session.Current).ToList();
                session = Log(completions.Count > 0 ? completions : new List<string> { $"Tick #{TickCount + 1}" });
                break;
            }

            case Route.Tool:
            {
                var inner = trace.Inner ?? intent;
                var view = LocationOf(inner) is { } at ? Simulation.SwapIn(Current, at) : Current;
                if (rejected is not null)
                    session = Log(new[] { $"FAILED: {ToolLog(inner, view, trace.Detail)} — {rejected.Reason}" });
                else if (changed)
                {
                    session = Undoable(view);
                    session = Log(WithCompletions(ToolLog(inner, view, trace.Detail)));
                }
                break;
            }

            case Route.PickupToToolbar when rejected is not null:
                session = Log(new[] { $"FAILED: Pickup: {Current.CurrentCell.Stack!.ItemType.Name} → toolbar — {rejected.Reason}" });
                break;

            case Route.ToggleClose or Route.OpenContainer or Route.OpenWorld or Route.ToolbarClose
                or Route.ToolbarPeek or Route.PickupToToolbar or Route.PeekClosePanel or Route.PeekCloseBag
                or Route.PeekOpen or Route.LeaveClosePanel when changed:
                session = Undoable(Current);
                session = Log(WithCompletions(PanelLog(trace, intent)));
                break;

            case Route.Dialogue when changed:
                session = Log(new[] { trace.Detail is { } dismissed ? $"Dialogue: dismissed {dismissed}" : "Dialogue: advance" });
                break;

            case Route.MenuOpened:
                session = Log(new[] { $"Recipe menu: opened ({trace.Detail} craftable)" });
                break;
            case Route.MenuNotFacility:
                session = Log(new[] { "Recipe menu: not a facility" });
                break;
            case Route.SetRecipe:
                session = Undoable(Current);
                session = Log(new[] { $"SetRecipe: {trace.Detail}" });
                break;
            case Route.SetRecipeNotFacility:
                session = Log(new[] { "SetRecipe: not a facility" });
                break;

            case Route.CycleRecipeRejected:
                session = Log(new[] { $"FAILED: CycleRecipe — {trace.Detail}" });
                break;
            case Route.RecipeSwitchNoRoom:
                session = Log(new[] { "FAILED: recipe switch — no room to set down the current items" });
                break;
            case Route.RecipeSwitched:
                session = Undoable(LocationOf(intent) is { } switchAt ? Simulation.SwapIn(Current, switchAt) : Current);
                session = Log(new[] { $"CycleRecipe: switched to {trace.Detail}" });
                break;
        }

        return (session, trace);
    }

    /// <summary>The target location of a tool intent (null for location-free intents such as DSL).</summary>
    private static LocationId? LocationOf(Intent intent) => intent switch
    {
        Intent.ToolPrimary i => i.At,
        Intent.Secondary i => i.At,
        Intent.QuickSplit i => i.At,
        Intent.ModalSplit i => i.At,
        Intent.Sort i => i.At,
        Intent.AcquireRandom i => i.At,
        Intent.Grab i => i.At,
        Intent.Drop i => i.At,
        Intent.Interact i => i.At,
        Intent.EnterBag i => i.At,
        Intent.LeaveBag i => i.At,
        Intent.Harvest i => i.At,
        Intent.CycleRecipe i => i.At,
        _ => null
    };

    /// <summary>
    /// A tool's action-log line, named from <paramref name="v"/> — the pre-step state in the tool's own frame
    /// (B swapped to the tool's location) — exactly as each pre-1.3 <c>Execute*</c> method's log lambda read it.
    /// </summary>
    private string ToolLog(Intent tool, GameState v, string? detail)
    {
        var cell = v.CurrentCell;
        var handFull = v.HasItemsInHand;
        return tool switch
        {
            Intent.ToolPrimary =>
                cell.HasBag ? $"Enter: {cell.Stack?.ItemType.Name ?? "bag"}"
                : !handFull && !cell.IsEmpty && v.IsNested ? $"Harvest: {cell.Stack!.Count} {cell.Stack.ItemType.Name}"
                : !handFull && !cell.IsEmpty ? $"Grab: {cell.Stack!.Count} {cell.Stack!.ItemType.Name}"
                : handFull && cell.IsEmpty ? $"Drop: {v.HandItems[0].Count} {v.HandItems[0].ItemType.Name}"
                : handFull && !cell.IsEmpty && cell.Stack!.ItemType == v.HandItems[0].ItemType
                    ? $"Merge: {v.HandItems[0].Count} {v.HandItems[0].ItemType.Name}"
                : handFull && !cell.IsEmpty ? $"Swap: {v.HandItems[0].ItemType.Name} ↔ {cell.Stack!.ItemType.Name}"
                : "Primary: no-op",
            Intent.Secondary =>
                !handFull && !cell.IsEmpty ? $"Grab half: {cell.Stack!.ItemType.Name}"
                : handFull ? $"Place 1: {v.HandItems[0].ItemType.Name}"
                : "Secondary: no-op",
            Intent.Interact =>
                cell.HasBag ? $"Enter: {cell.Stack?.ItemType.Name ?? "bag"}"
                : v.IsNested && !cell.IsEmpty ? $"Harvest: {cell.Stack!.Count} {cell.Stack.ItemType.Name}"
                : "Interact: nothing to do",
            Intent.EnterBag => $"Enter: {cell.Stack?.ItemType.Name ?? "bag"}",
            Intent.LeaveBag => "Back: returned to parent bag",
            Intent.Grab => FormatGrabLog(cell.Stack, v.Cursor.Position),
            Intent.Drop => FormatDropLog(v.HandItems, v.Cursor.Position),
            Intent.QuickSplit => FormatSplitLog(cell.Stack),
            Intent.ModalSplit m => FormatModalSplitLog(cell.Stack, m.LeftCount),
            Intent.Sort => "Sort: reorganized bag",
            Intent.Harvest => cell.Stack is { } item ? $"Harvest: {item.Count} {item.ItemType.Name}" : "Harvest: empty cell",
            Intent.AcquireRandom => AcquireLog(v),
            Intent.Dsl d => d.Expression + (detail ?? ""),
            _ => throw new InvalidOperationException($"No tool log for {tool.GetType().Name}")
        };
    }

    /// <summary>
    /// The routed panel verbs' action-log lines (open/close/peek/pickup), named from the pre-step state and
    /// the trace detail (the bag's environment, or the count a pickup placed).
    /// </summary>
    private string PanelLog(StepTrace trace, Intent intent) => trace.Route switch
    {
        Route.ToggleClose or Route.ToolbarClose => $"Close: {trace.Detail}",
        Route.OpenContainer => $"Open Container: {trace.Detail}",
        Route.OpenWorld => $"Open World: {trace.Detail}",
        Route.ToolbarPeek => $"Peek toolbar bag: {trace.Detail}",
        Route.PickupToToolbar => $"Pickup: {trace.Detail} {Current.CurrentCell.Stack!.ItemType.Name} → toolbar",
        Route.PeekClosePanel => $"Close peek: {((Intent.Peek)intent).At}",
        Route.PeekCloseBag => $"Close peek: {trace.Detail}",
        Route.PeekOpen => $"Peek: {trace.Detail}",
        Route.LeaveClosePanel => $"Close: {((Intent.Leave)intent).At}",
        _ => throw new InvalidOperationException($"No panel log for {trace.Route}")
    };

    /// <summary>
    /// The AcquireRandom log line (P1-D6, unchanged text). The shell previews the step's exact draw (same RNG,
    /// same step: pure) on the pre-step state to name the acquired stack. The diff reads the <i>root</i> grid,
    /// so inside a nested bag the line says "added random item" — kept as is. Slice 1.5's events can replace this.
    /// </summary>
    private string AcquireLog(GameState v)
    {
        var result = v.ToolAcquireRandom(NextStepRandom(SimStream.Acquire));
        var newItem = result.State.RootBag.Grid.Cells
            .Where(c => !c.IsEmpty)
            .Select(c => c.Stack!)
            .Except(v.RootBag.Grid.Cells.Where(c => !c.IsEmpty).Select(c => c.Stack!))
            .FirstOrDefault();
        return newItem != null ? $"Acquire: +1 {newItem.ItemType.Name}" : "Acquire: added random item";
    }

    /// <summary>
    /// Undo the last action: pop the undo stack and restore that state, logging <c>Undo: {last line}</c>.
    /// Returns this session when there is nothing to undo. Dialogue progression is monotonic and never
    /// rewinds (see <see cref="DialogueState"/>), so the live dialogue substate is carried forward onto the
    /// restored state; <see cref="TickCount"/>, <see cref="Elapsed"/> and the RNG are untouched (they keep
    /// moving forward). A shell operation: the snapshots are shell state, so it never reaches Step.
    /// </summary>
    private GameSession UndoCore()
    {
        if (!CanUndo) return this;

        var previousState = UndoStack.Peek();
        var lastAction = ActionLog.Count > 0 ? ActionLog[^1] : "unknown";
        return this with
        {
            Current = previousState with { Dialogue = Current.Dialogue },
            UndoStack = UndoStack.Pop(),
            ActionLog = ActionLog.Add($"Undo: {lastAction}")
        };
    }

    // ==================== Internal wrappers (one intent each; tests use them) ====================

    /// <summary>Undo via <see cref="Intent.Undo"/>; null when there is nothing to undo.</summary>
    internal GameSession? Undo() => CanUndo ? Apply(new Intent.Undo()) : null;

    /// <summary>Moves the B cursor one step (<see cref="Intent.Move"/>). Not undoable, not logged.</summary>
    internal GameSession MoveCursor(Direction direction) => MoveCursorAt(LocationId.B, direction);

    /// <summary>Moves <paramref name="locId"/>'s cursor one step (<see cref="Intent.Move"/>). Not undoable, not logged.</summary>
    internal GameSession MoveCursorAt(LocationId locId, Direction direction) => Apply(new Intent.Move(locId, direction));

    /// <summary>
    /// Test helper: replaces <see cref="Current"/> with <paramref name="state"/> and places its B cursor on
    /// <paramref name="position"/> (<see cref="Intent.SetCursor"/>). Not undoable, not logged.
    /// </summary>
    internal GameSession MoveCursor(GameState state, Position position) =>
        (this with { Current = state }).Apply(new Intent.SetCursor(LocationId.B, position));

    /// <summary>Advances or dismisses the showing dialogue (<see cref="Intent.AdvanceDialogue"/>). Not undoable.</summary>
    internal GameSession AdvanceDialogue() => Apply(new Intent.AdvanceDialogue());

    /// <summary>Sets the clock readout (<see cref="Intent.SetElapsed"/>). Monotonic across undo.</summary>
    internal GameSession WithElapsed(TimeSpan elapsed) => Apply(new Intent.SetElapsed(elapsed));

    /// <summary>The contextual primary tool at <paramref name="loc"/> (<see cref="Intent.ToolPrimary"/>). Undoable.</summary>
    internal GameSession ExecutePrimary(LocationId loc) => Apply(new Intent.ToolPrimary(loc));

    /// <summary>The contextual primary tool at B. Undoable.</summary>
    internal GameSession ExecutePrimary() => ExecutePrimary(LocationId.B);

    /// <summary>The secondary tool at <paramref name="loc"/>. Undoable.</summary>
    internal GameSession ExecuteSecondary(LocationId loc) => Apply(new Intent.Secondary(loc));

    /// <summary>The secondary tool at B. Undoable.</summary>
    internal GameSession ExecuteSecondary() => ExecuteSecondary(LocationId.B);

    /// <summary>Context-sensitive interact at B. Undoable.</summary>
    internal GameSession ExecuteInteract() => Apply(new Intent.Interact(LocationId.B));

    /// <summary>Enter the bag at B's cursor. Undoable.</summary>
    internal GameSession ExecuteEnterBag() => Apply(new Intent.EnterBag(LocationId.B));

    /// <summary>Leave B's current bag. Undoable.</summary>
    internal GameSession ExecuteLeaveBag() => Apply(new Intent.LeaveBag(LocationId.B));

    /// <summary>Grab at B. Undoable.</summary>
    internal GameSession ExecuteGrab() => Apply(new Intent.Grab(LocationId.B));

    /// <summary>Drop at B. Undoable.</summary>
    internal GameSession ExecuteDrop() => Apply(new Intent.Drop(LocationId.B));

    /// <summary>QuickSplit at <paramref name="loc"/>. Undoable.</summary>
    internal GameSession ExecuteQuickSplit(LocationId loc) => Apply(new Intent.QuickSplit(loc));

    /// <summary>QuickSplit at B. Undoable.</summary>
    internal GameSession ExecuteQuickSplit() => ExecuteQuickSplit(LocationId.B);

    /// <summary>ModalSplit at B leaving <paramref name="leftCount"/>. Undoable.</summary>
    internal GameSession ExecuteModalSplit(int leftCount) => Apply(new Intent.ModalSplit(LocationId.B, leftCount));

    /// <summary>Opens the inline split editor at <paramref name="loc"/>.</summary>
    internal GameSession BeginSplit(LocationId loc) => Apply(new Intent.BeginSplit(loc));

    /// <summary>Adjusts the inline split's grab count by <paramref name="delta"/>.</summary>
    internal GameSession AdjustSplit(int delta) => Apply(new Intent.AdjustSplit(delta));

    /// <summary>Commits the inline split and closes the editor.</summary>
    internal GameSession CommitSplit() => Apply(new Intent.CommitSplit());

    /// <summary>Closes the inline split editor.</summary>
    internal GameSession CancelSplit() => Apply(new Intent.CancelSplit());

    /// <summary>Sort at <paramref name="loc"/>. Undoable.</summary>
    internal GameSession ExecuteSort(LocationId loc) => Apply(new Intent.Sort(loc));

    /// <summary>Sort at B. Undoable.</summary>
    internal GameSession ExecuteSort() => ExecuteSort(LocationId.B);

    /// <summary>Harvest at B. Undoable.</summary>
    internal GameSession ExecuteHarvest() => Apply(new Intent.Harvest(LocationId.B));

    /// <summary>CycleRecipe at <paramref name="loc"/>. Undoable.</summary>
    internal GameSession ExecuteCycleRecipe(LocationId loc) => Apply(new Intent.CycleRecipe(loc));

    /// <summary>CycleRecipe at B. Undoable.</summary>
    internal GameSession ExecuteCycleRecipe() => ExecuteCycleRecipe(LocationId.B);

    /// <summary>Opens the recipe menu on the facility at <paramref name="loc"/>.</summary>
    internal GameSession OpenRecipeMenu(LocationId loc) => Apply(new Intent.OpenRecipeMenu(loc));

    /// <summary>Moves the recipe-menu selection.</summary>
    internal GameSession MoveRecipeMenu(int delta) => Apply(new Intent.MoveRecipeMenu(delta));

    /// <summary>Closes the recipe menu.</summary>
    internal GameSession CloseRecipeMenu() => Apply(new Intent.CloseRecipeMenu());

    /// <summary>Sets the facility's recipe to the menu selection and closes the menu.</summary>
    internal GameSession ConfirmRecipeMenu() => Apply(new Intent.ConfirmRecipeMenu());

    /// <summary>The debug AcquireRandom tool at B. Undoable.</summary>
    internal GameSession ExecuteAcquireRandom() => Apply(new Intent.AcquireRandom(LocationId.B));

    /// <summary>Runs a DSL program (<see cref="Intent.Dsl"/>). One undo snapshot per program.</summary>
    internal GameSession Execute(string dslExpression) => Apply(new Intent.Dsl(dslExpression));

    /// <summary>One realtime clock tick (<see cref="Intent.Tick"/>).</summary>
    internal GameSession Tick() => Apply(new Intent.Tick());

    // ==================== Projection ====================

    /// <summary>
    /// Debug counter (thread-static): how many state-changing <see cref="Simulation.Step"/> results this
    /// thread's sessions have applied. Since Slice 1.3 every change except undo goes through Step, so tests
    /// assert that a journey step with no Step result changed nothing. Not part of session state.
    /// </summary>
    public static int DebugStepCount => t_debugStepCount;

    [ThreadStatic] private static int t_debugStepCount;

    /// <summary>The read-only L1 content <see cref="Simulation.Step"/> reads (recipes, facility map, beats, tick mode).</summary>
    public SimContent Content => new(Recipes, FacilityRecipeMap, Beats, TickMode);

    /// <summary>
    /// Projects the session's L1 fields into the <see cref="SimState"/> <see cref="Simulation.Step"/>
    /// consumes. The session keeps them stored on itself (public shape unchanged) and projects.
    /// </summary>
    public SimState ToSimState() => new(Current, TickCount, Elapsed, SplitMode, RecipeMenu, Content, WorldRng);

    /// <summary>
    /// A <see cref="System.Random"/> over <paramref name="stream"/> positioned exactly where the next
    /// <see cref="Simulation.Step"/> will draw (step <c>TickCount + 1</c>). Used by the shell to preview a draw Step makes.
    /// </summary>
    private SimRandom NextStepRandom(SimStream stream) => WorldRng.AtStep(TickCount + 1).AsRandom(stream);

    /// <summary>Writes a stepped <see cref="SimState"/> back onto the session's L1 fields.</summary>
    private GameSession WithSimState(SimState s) => this with
    {
        Current = s.Game,
        TickCount = s.TickCount,
        Elapsed = s.Elapsed,
        SplitMode = s.SplitMode,
        RecipeMenu = s.RecipeMenu,
        Rng = s.Rng
    };

    /// <summary>
    /// The craft-completion log lines (<c>✦ {facility} crafted: {recipe name}</c>) for a step's
    /// <see cref="SimEvent.CraftCompleted"/> events, in emission (facility) order. The recipe name is
    /// resolved against the facility's recipe set under the <i>pre-step</i> known recipes, as the tick
    /// loop itself does, falling back to the recipe id.
    /// </summary>
    private IEnumerable<string> CompletionLogs(ImmutableArray<SimEvent> events, GameState after) =>
        events.OfType<SimEvent.CraftCompleted>().Select(e =>
        {
            var environment = (after.Store.GetById(e.FacilityBagId) ?? Current.Store.GetById(e.FacilityBagId))!.EnvironmentType;
            var recipe = GetRecipesForFacility(environment).FirstOrDefault(r => r.Id == e.RecipeId);
            return $"✦ {environment} crafted: {recipe?.Name ?? e.RecipeId}";
        });

    /// <summary>
    /// Returns recipes applicable to a facility by environment type, under the current known recipes.
    /// Delegates to <see cref="SimContent.RecipesForFacility"/>.
    /// </summary>
    private IReadOnlyList<Recipe> GetRecipesForFacility(string environmentType) =>
        Content.RecipesForFacility(environmentType, Current.KnownRecipes);

    /// <summary>A facility currently mid-craft, projected for the action-queue chrome (Slice 7).</summary>
    public record ActiveCraft(string Facility, string RecipeId, string? Name, int Progress, int Duration);

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
        foreach (var f in Simulation.OrderedFacilities(Current))
        {
            if (f.FacilityState?.RecipeId is not { } recipeId) continue;
            var progress = Current.Store.GetOwnerStack(f.Id)?.GetInt("Progress") ?? 0;
            var recipe = byId is not null && byId.TryGetValue(recipeId, out var r) ? r : null;
            rows.Add(new ActiveCraft(f.EnvironmentType, recipeId, recipe?.Name, progress, recipe?.Duration ?? 0));
        }
        return rows;
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
