using Pockets.Core.Dsl;
using Pockets.Core.Models;

namespace Pockets.Core.Sim;

/// <summary>
/// How <see cref="Simulation.Step"/> resolved one intent (Slice 1.3). The session shell reads it to write
/// the action-log line exactly as before (P1-D6) and to decide the undo snapshot; the controller reads it
/// for its status message. Not part of the step's output contract: events are. Since Slice 1.5 the
/// controller's focus and bump cue come from events (<see cref="SimEvent.PanelOpened"/>,
/// <see cref="SimEvent.PanelClosed"/>, <see cref="SimEvent.Bumped"/>) and the AcquireRandom log line from
/// <see cref="SimEvent.Acquired"/>; what remains here is log and status wording.
/// </summary>
internal enum Route
{
    /// <summary>A clock tick.</summary>
    Tick,
    /// <summary>The clock readout was set.</summary>
    Clock,
    /// <summary>A tool ran at a location. <see cref="StepTrace.Inner"/> is the tool intent; <see cref="StepTrace.Detail"/> carries a DSL error suffix.</summary>
    Tool,
    /// <summary>Primary on B closed the C/W panel showing the cursor bag. Detail = bag environment.</summary>
    ToggleClose,
    /// <summary>Primary on B opened a facility as C. Detail = bag environment.</summary>
    OpenContainer,
    /// <summary>Primary on B opened a wilderness as W. Detail = bag environment.</summary>
    OpenWorld,
    /// <summary>Primary on T closed the peeked toolbar bag. Detail = bag environment.</summary>
    ToolbarClose,
    /// <summary>Primary on T peeked a toolbar bag as C. Detail = bag environment.</summary>
    ToolbarPeek,
    /// <summary>Primary on B sent a bare pickup to the toolbar. Detail = the count placed.</summary>
    PickupToToolbar,
    /// <summary>Peek on a focused C/W panel closed it.</summary>
    PeekClosePanel,
    /// <summary>Peek on a C/W panel that could not close.</summary>
    PeekNothingToClose,
    /// <summary>Peek on a cell holding no bag.</summary>
    PeekNoBag,
    /// <summary>Peek on a bag cell whose bag is missing from the store.</summary>
    PeekBagNotFound,
    /// <summary>Peek refused by an enter-only bag (failed-peek beat). Detail = bag environment.</summary>
    PeekRefused,
    /// <summary>Peek closed the already-peeked cursor bag. Detail = bag environment.</summary>
    PeekCloseBag,
    /// <summary>Peek opened the cursor bag as a C look-in overlay. Detail = bag environment.</summary>
    PeekOpen,
    /// <summary>Peek could not open the cursor bag.</summary>
    PeekCouldNotOpen,
    /// <summary>Leave on a focused C/W panel closed it.</summary>
    LeaveClosePanel,
    /// <summary>A cursor move (or a move with nowhere to go).</summary>
    Moved,
    /// <summary>A cursor move refused by an unnavigable tree cell (the bump cue).</summary>
    Bumped,
    /// <summary>A cursor placed directly.</summary>
    Cursor,
    /// <summary>Dialogue advanced. Detail = the dismissed beat id, if the beat was dismissed.</summary>
    Dialogue,
    /// <summary>The inline split editor changed (begin / adjust / cancel).</summary>
    Split,
    /// <summary>The recipe menu opened. Detail = the number of craftable recipes listed.</summary>
    MenuOpened,
    /// <summary>The recipe menu was asked for on a bag that is not a facility.</summary>
    MenuNotFacility,
    /// <summary>The recipe menu moved, closed, or confirmed with nothing to set.</summary>
    Menu,
    /// <summary>The recipe menu set the facility's recipe. Detail = the recipe id.</summary>
    SetRecipe,
    /// <summary>The recipe menu's facility is gone; the menu just closed.</summary>
    SetRecipeNotFacility,
    /// <summary>CycleRecipe refused. Detail = the reason.</summary>
    CycleRecipeRejected,
    /// <summary>CycleRecipe switched the facility. Detail = the new active recipe id.</summary>
    RecipeSwitched,
    /// <summary>CycleRecipe refused because the dumped slot items had nowhere to go.</summary>
    RecipeSwitchNoRoom,
}

/// <summary>One intent's resolution (see <see cref="Route"/>): the route, the tool intent it ran, and a detail value.</summary>
internal sealed record StepTrace(Route Route, Intent? Inner = null, string? Detail = null);

public static partial class Simulation
{
    /// <summary>
    /// The sub-intents a composite intent stands for: a <see cref="Intent.Click"/> is a cursor placement
    /// then Primary/Secondary at that panel; a <see cref="Intent.CommitSplit"/> is the split's
    /// <see cref="Intent.ModalSplit"/> then closing the editor (nothing when no split is open). Any other
    /// intent stands for itself. The shell runs each sub-intent as its own step, exactly as the old
    /// controller code applied them one after the other.
    /// </summary>
    internal static IReadOnlyList<Intent> Expand(SimState s, Intent intent) => intent switch
    {
        Intent.Click c => new Intent[]
        {
            new Intent.SetCursor(c.At, c.Pos),
            c.Type == ClickType.Primary ? new Intent.Primary(c.At) : new Intent.Secondary(c.At)
        },
        Intent.CommitSplit => s.SplitMode is { } split
            ? new Intent[] { new Intent.ModalSplit(split.Location, split.StackTotal - split.GrabCount), new Intent.CancelSplit() }
            : Array.Empty<Intent>(),
        _ => new[] { intent }
    };

    /// <summary>The intent dispatcher: applies one intent, appending its events and (when asked) its trace.</summary>
    private static SimState StepOne(
        SimState s, Intent intent, int step, ImmutableArray<SimEvent>.Builder events, List<StepTrace>? traces)
    {
        void Trace(Route route, Intent? inner = null, string? detail = null) =>
            traces?.Add(new StepTrace(route, inner, detail));

        SimState Tool(LocationId at, Func<GameState, ToolResult> tool)
        {
            Trace(Route.Tool, intent);
            return ApplyToolAt(s, intent, at, tool, step, events);
        }

        SimState Change(Route route, SimState next, string? detail = null, IEnumerable<Func<int, SimEvent>>? extra = null)
        {
            Trace(route, null, detail);
            return Changed(s, next, intent, step, events, extra);
        }

        switch (intent)
        {
            case Intent.Tick:
                Trace(Route.Tick);
                return ApplyTick(s, intent, step, events);
            case Intent.SetElapsed e:
                return Change(Route.Clock, s with { Elapsed = e.Elapsed });

            case Intent.Click or Intent.CommitSplit:
                foreach (var sub in Expand(s, intent))
                    s = StepOne(s, sub, step, events, traces);
                return s;

            // --- Tools (each at its location, the RunAt swap) ---
            case Intent.ToolPrimary t: return Tool(t.At, g => g.ToolPrimary());
            case Intent.Secondary t: return Tool(t.At, g => g.ToolSecondary());
            case Intent.QuickSplit t: return Tool(t.At, g => g.ToolQuickSplit());
            case Intent.ModalSplit t: return Tool(t.At, g => g.ToolModalSplit(t.LeftCount));
            case Intent.Sort t: return Tool(t.At, g => g.ToolSort());
            case Intent.Grab t: return Tool(t.At, g => g.ToolGrab());
            case Intent.Drop t: return Tool(t.At, g => g.ToolDrop());
            case Intent.Interact t: return Tool(t.At, g => g.Interact());
            case Intent.EnterBag t: return Tool(t.At, g => g.EnterBag());
            case Intent.LeaveBag t: return Tool(t.At, g => g.LeaveBag());
            case Intent.Harvest t: return Tool(t.At, g => g.ToolHarvest());
            case Intent.AcquireRandom a:
                Trace(Route.Tool, intent);
                return ApplyToolAt(s, intent, a.At, (g, rng) =>
                {
                    var random = rng.AsRandom(SimStream.Acquire);
                    var result = g.ToolAcquireRandom(random);
                    return (result, random.Rng);
                }, step, events);
            case Intent.Dsl d:
                return StepDsl(s, d, step, events, Trace);
            case Intent.CycleRecipe c:
                return StepCycleRecipe(s, c, step, events, Trace);

            // --- Routed panel verbs ---
            case Intent.Primary p:
                return StepPrimary(s, p, step, events, Trace);
            case Intent.Peek p:
                return StepPeek(s, p, step, events, Trace);
            case Intent.Leave l:
                return StepLeave(s, l, step, events, Trace);

            // --- Cursor ---
            case Intent.Move m:
            {
                var fired = new List<Func<int, SimEvent>>();
                var (moved, bumped) = MoveCursor(s.Game, m.At, m.Dir, s.Content.Beats,
                    trigger => fired.Add(seq => new SimEvent.UiTriggerFired(step, seq, trigger)));
                if (!bumped)
                    return Change(Route.Moved, s with { Game = moved }, extra: fired);

                // A refused move always emits Bumped (the cue fires on every bump), even when the bump count
                // didn't change (a beat is showing) and the state is otherwise untouched.
                var bump = BumpAt(s.Game, m.At, m.Dir, BumpReason.Unnavigable, step);
                return BumpedChange(s, s with { Game = moved }, intent, Route.Bumped, bump);
            }
            case Intent.SetCursor c:
            {
                var g = s.Game;
                var placed = g.Locations.TryGet(c.At) is { } loc
                    ? g with { Locations = g.Locations.Set(c.At, loc with { Cursor = new Cursor(c.Pos) }) }
                    : g;
                return Change(Route.Cursor, s with { Game = placed });
            }

            // --- Dialogue ---
            case Intent.AdvanceDialogue:
            {
                var (advanced, dismissed) = AdvanceDialogue(s.Game, s.Content.Beats);
                var extra = s.Game.Dialogue.ActiveBeatId is { } beat
                    ? new Func<int, SimEvent>[] { seq => new SimEvent.DialogueAdvanced(step, seq, beat, dismissed is not null) }
                    : null;
                return Change(Route.Dialogue, s with { Game = advanced }, dismissed, extra);
            }

            // --- Inline split ---
            case Intent.BeginSplit b:
                return Change(Route.Split, BeginSplit(s.Game, b.At) is { } split ? s with { SplitMode = split } : s);
            case Intent.AdjustSplit a:
                return Change(Route.Split, s with { SplitMode = AdjustSplit(s.SplitMode, a.Delta) });
            case Intent.CancelSplit:
                return Change(Route.Split, s with { SplitMode = null });

            // --- Recipe menu ---
            case Intent.OpenRecipeMenu o:
            {
                var facility = s.Game.ActiveBagAt(o.At);
                if (facility.FacilityState is null)
                {
                    Trace(Route.MenuNotFacility);
                    return Rejected(s, intent, "not a facility", step, events);
                }
                var menu = OpenRecipeMenu(facility, s.Content, s.Game.KnownRecipes);
                return Change(Route.MenuOpened, s with { RecipeMenu = menu }, menu.RecipeIds.Length.ToString());
            }
            case Intent.MoveRecipeMenu m:
                return Change(Route.Menu, s with { RecipeMenu = MoveRecipeMenu(s.RecipeMenu, m.Delta) });
            case Intent.CloseRecipeMenu:
                return Change(Route.Menu, s with { RecipeMenu = null });
            case Intent.ConfirmRecipeMenu:
                return StepConfirmRecipeMenu(s, step, events, Trace);

            case Intent.Undo:
                throw new InvalidOperationException("Undo is handled by the session shell, which owns the undo snapshots.");
            default:
                throw new InvalidOperationException($"Unhandled intent {intent.GetType().Name}");
        }

        // A bump: the Bumped event always, behind the envelope and the change when anything changed.
        SimState BumpedChange(SimState pre, SimState next, Intent i, Route route, Func<int, SimEvent> bump)
        {
            Trace(route);
            if (next != pre)
                return Changed(pre, next, i, step, events, new[] { bump });
            events.Add(bump(events.Count));
            return pre;
        }
    }

    /// <summary>
    /// A <see cref="SimEvent.Bumped"/> at <paramref name="at"/>'s cursor (in the bag it shows), in
    /// <paramref name="dir"/>, for <paramref name="reason"/>.
    /// </summary>
    private static Func<int, SimEvent> BumpAt(GameState g, LocationId at, Direction? dir, BumpReason reason, int step)
    {
        var loc = g.Locations.TryGet(at);
        var bag = loc is not null ? ResolveBag(g, loc)?.Id ?? loc.BagId : g.ActiveBagId;
        var pos = loc?.Cursor.Position ?? g.Cursor.Position;
        return seq => new SimEvent.Bumped(step, seq, at, bag, pos, dir, reason);
    }

    // ==================== Routed verbs (moved from GameController, focus → the intent's At) ====================

    /// <summary>
    /// Primary at <paramref name="p"/>.At, routed exactly as the controller's <c>ExecuteFocusedPrimary</c> did:
    /// on B, a bag already open as C/W toggles closed, else (empty hand) a facility opens as C and a
    /// wilderness as W; on T, a toolbar bag is peeked (or toggled closed); on B with the demo's toolbar
    /// pickup, a bare pickup flies to the toolbar; anything else runs the contextual primary tool at At.
    /// </summary>
    private static SimState StepPrimary(
        SimState s, Intent.Primary p, int step, ImmutableArray<SimEvent>.Builder events, Action<Route, Intent?, string?> trace)
    {
        var g = s.Game;
        var at = p.At;
        var cell = CellAt(g, at);
        var bagId = cell.HasBag ? cell.Stack!.ContainedBagId!.Value : Guid.Empty;
        var bag = cell.HasBag ? g.Store.GetById(bagId) : null;

        SimState Panel(Route route, ToolResult result)
        {
            trace(route, null, bag!.EnvironmentType);
            return ApplyToolResult(s, p, result, s.Rng.AtStep(step), step, events);
        }

        bool ShowsBag(LocationId panel) => g.Locations.TryGet(panel) is { } loc && loc.BagId == bagId;

        // Toggle: a bag in B that's already open as C/W closes that panel.
        if (at == LocationId.B && bag is not null)
        {
            if (ShowsBag(LocationId.C) && g.ClosePanel(LocationId.C) is { Success: true } closeC)
                return Panel(Route.ToggleClose, closeC);
            if (ShowsBag(LocationId.W) && g.ClosePanel(LocationId.W) is { Success: true } closeW)
                return Panel(Route.ToggleClose, closeW);
        }

        // Open as C/W when the bag in B is a facility / wilderness (only with an empty hand).
        if (at == LocationId.B && bag is not null && !g.HasItemsInHand)
        {
            if (GameState.IsFacilityBag(bag))
            {
                if (g.OpenAsContainer(bagId) is { Success: true } open)
                    return Panel(Route.OpenContainer, open);
            }
            else if (GameState.IsWildernessType(bag.EnvironmentType))
            {
                if (g.OpenAsWorld(bagId) is { Success: true } open)
                    return Panel(Route.OpenWorld, open);
            }
        }

        // A bag in the TOOLBAR is peeked (a C look-in overlay), never breadcrumb-entered: entering it
        // wedged the toolbar panel (playtest fix, 2026-08-04). Already peeked → toggle closed.
        if (at == LocationId.T && bag is not null)
        {
            if (ShowsBag(LocationId.C))
            {
                if (g.ClosePanel(LocationId.C) is { Success: true } close)
                    return Panel(Route.ToolbarClose, close);
            }
            else if (g.OpenAsContainer(bagId) is { Success: true } open)
                return Panel(Route.ToolbarPeek, open);
        }

        // Fixed-inventory routing (Slice 3): a bare pickup in B flies to the toolbar in the demo profile.
        if (at == LocationId.B && g.ToolbarPickup && IsBarePickup(g, cell))
        {
            var (result, placed) = PickupToToolbar(g);
            trace(Route.PickupToToolbar, null, placed.ToString());
            return ApplyToolResult(s, p, result, s.Rng.AtStep(step), step, events);
        }

        trace(Route.Tool, new Intent.ToolPrimary(at), null);
        return ApplyToolAt(s, p, at, gs => gs.ToolPrimary(), step, events);
    }

    /// <summary>
    /// Look-in peek at <paramref name="p"/>.At (moved from the controller's <c>ExecutePeek</c>): a focused C/W
    /// panel closes; a cell with no (known) bag does nothing; an enter-only bag refuses the peek and fires the
    /// first-failed-peek beat (dialogue only — not undoable, no step); an already-peeked bag toggles closed;
    /// otherwise the bag opens as a C overlay, and peeking while carrying fires the capacity-absence beat.
    /// </summary>
    private static SimState StepPeek(
        SimState s, Intent.Peek p, int step, ImmutableArray<SimEvent>.Builder events, Action<Route, Intent?, string?> trace)
    {
        var g = s.Game;
        var at = p.At;
        var rng = s.Rng.AtStep(step);

        if (at is LocationId.C or LocationId.W)
        {
            if (g.ClosePanel(at) is { Success: true } close)
            {
                trace(Route.PeekClosePanel, null, null);
                return ApplyToolResult(s, p, close, rng, step, events);
            }
            trace(Route.PeekNothingToClose, null, null);
            return s;
        }

        var cell = CellAt(g, at);
        if (!cell.HasBag)
        {
            trace(Route.PeekNoBag, null, null);
            return s;
        }

        var bagId = cell.Stack!.ContainedBagId!.Value;
        if (g.Store.GetById(bagId) is not { } bag)
        {
            trace(Route.PeekBagNotFound, null, null);
            return s;
        }

        // Enter-only: refuse the peek. Fire the beat once and leave everything else exactly as it was.
        if (bag.EnterOnly)
        {
            trace(Route.PeekRefused, null, bag.EnvironmentType);
            var bump = BumpAt(g, at, null, BumpReason.FailedPeek, step);
            var next = s with { Game = FireBeats(g, s.Content.Beats, DialogueTriggerKind.FirstFailedPeek) };
            if (next != s)
                return Changed(s, next, p, step, events, new[] { bump });
            events.Add(bump(events.Count));
            return s;
        }

        if (g.Locations.TryGet(LocationId.C) is { } cLoc && cLoc.BagId == bagId
            && g.ClosePanel(LocationId.C) is { Success: true } closeBag)
        {
            trace(Route.PeekCloseBag, null, bag.EnvironmentType);
            return ApplyToolResult(s, p, closeBag, rng, step, events);
        }

        if (g.OpenAsContainer(bagId) is { Success: true } open)
        {
            trace(Route.PeekOpen, null, bag.EnvironmentType);
            // Capacity-absence beat (Slice 5): peering into a bag while carrying fires its line once.
            return ApplyToolResult(s, p, open, rng, step, events, after: st =>
                st.HasItemsInHand ? FireBeats(st, s.Content.Beats, DialogueTriggerKind.FirstPeekWhileCarrying) : st);
        }

        trace(Route.PeekCouldNotOpen, null, null);
        return s;
    }

    /// <summary>
    /// Leave / back at <paramref name="l"/>.At: an open C/W panel at At closes; otherwise the B bag is left
    /// for its parent (the leave tool always acts on B, as <c>ExecuteLeaveBag</c> did).
    /// </summary>
    private static SimState StepLeave(
        SimState s, Intent.Leave l, int step, ImmutableArray<SimEvent>.Builder events, Action<Route, Intent?, string?> trace)
    {
        if (l.At is LocationId.C or LocationId.W && s.Game.ClosePanel(l.At) is { Success: true } close)
        {
            trace(Route.LeaveClosePanel, null, null);
            return ApplyToolResult(s, l, close, s.Rng.AtStep(step), step, events);
        }

        trace(Route.Tool, new Intent.LeaveBag(LocationId.B), null);
        return ApplyToolAt(s, l, LocationId.B, g => g.LeaveBag(), step, events);
    }

    // ==================== DSL / recipes ====================

    /// <summary>
    /// Runs a DSL program inside the step: its random opcodes draw from <see cref="SimStream.Dsl"/> at this
    /// step and the advanced RNG is committed with the state. A parse/run failure rejects the intent with the
    /// exception message; accumulated op errors still apply the state (partial success) and ride in the trace
    /// as the log suffix.
    /// </summary>
    private static SimState StepDsl(
        SimState s, Intent.Dsl d, int step, ImmutableArray<SimEvent>.Builder events, Action<Route, Intent?, string?> trace)
    {
        var random = s.Rng.AtStep(step).AsRandom(SimStream.Dsl);
        OpResult result;
        try
        {
            result = DslInterpreter.RunProgram(s.Game, d.Expression, random);
        }
        catch (InvalidOperationException ex)
        {
            trace(Route.Tool, d, null);
            return Rejected(s, d, ex.Message, step, events);
        }

        var suffix = result.IsOk ? "" : $" — {string.Join("; ", result.Errors)}";
        trace(Route.Tool, d, suffix);
        return ApplyToolResult(s, d, ToolResult.Ok(result.State), random.Rng, step, events);
    }

    /// <summary>
    /// Cycles the active recipe of the facility at <paramref name="c"/>.At (in the RunAt frame, as
    /// <c>ExecuteCycleRecipe(loc)</c> did), rebuilding its slots and re-homing the dumped slot items into the
    /// root bag — or refusing the whole switch when they don't fit (no-loss, the 2026-08-04 deletion fix).
    /// Not a timed action: no tick, no hooks, no TickCount bump; the shell pushes undo and logs.
    /// </summary>
    private static SimState StepCycleRecipe(
        SimState s, Intent.CycleRecipe c, int step, ImmutableArray<SimEvent>.Builder events, Action<Route, Intent?, string?> trace)
    {
        SimState Refuse(Route route, string reason)
        {
            trace(route, null, reason);
            return Rejected(s, c, reason, step, events);
        }

        var g = SwapIn(s.Game, c.At);
        var activeBag = g.ActiveBag;
        if (activeBag.FacilityState is null)
            return Refuse(Route.CycleRecipeRejected, "not in a facility");
        if (s.Content.Recipes.IsDefaultOrEmpty)
            return Refuse(Route.CycleRecipeRejected, "no recipes loaded");

        var facilityRecipes = s.Content.RecipesForFacility(activeBag.EnvironmentType, g.KnownRecipes);
        if (facilityRecipes.Count == 0)
            return Refuse(Route.CycleRecipeRejected, "no recipes for this facility");

        var (updated, dumped) = FacilityLogic.CycleRecipe(activeBag, facilityRecipes);

        var rootGrid = g.RootBag.Grid;
        foreach (var stack in dumped)
        {
            var (grid, unplaced) = rootGrid.AcquireItems(new[] { stack });
            if (unplaced.Count > 0)
                return Refuse(Route.RecipeSwitchNoRoom, "no room to set down the current items");
            rootGrid = grid;
        }

        var newState = g with { Store = g.Store.Set(g.RootBagId, g.RootBag with { Grid = rootGrid }) };
        newState = newState.ReplaceBagById(activeBag.Id, updated,
            stack => stack.WithProperty("Progress", new IntValue(0)));

        var switchedTo = updated.FacilityState!.ActiveRecipeId!;
        var owner = OwnerCell(g, activeBag.Id);
        trace(Route.RecipeSwitched, null, switchedTo);
        return Changed(s, s with { Game = SwapOut(newState, s.Game, c.At) }, c, step, events,
            new Func<int, SimEvent>[] { seq => new SimEvent.RecipeSelected(step, seq, activeBag.Id, switchedTo, owner, true) });
    }

    /// <summary>
    /// Confirms the recipe menu: closes it and sets the facility's active recipe to the selection (the
    /// Crafting Table keeps generic slots, so only <see cref="FacilityState.ActiveRecipeId"/> changes and any
    /// in-progress craft resets). A missing facility just closes the menu. No tick, no hooks.
    /// </summary>
    private static SimState StepConfirmRecipeMenu(
        SimState s, int step, ImmutableArray<SimEvent>.Builder events, Action<Route, Intent?, string?> trace)
    {
        var intent = new Intent.ConfirmRecipeMenu();
        if (s.RecipeMenu is not { } menu)
        {
            trace(Route.Menu, null, null);
            return s;
        }

        var closed = s with { RecipeMenu = null };
        if (menu.RecipeIds.IsEmpty)
        {
            trace(Route.Menu, null, null);
            return Changed(s, closed, intent, step, events);
        }

        var recipeId = menu.RecipeIds[Math.Clamp(menu.SelectedIndex, 0, menu.RecipeIds.Length - 1)];
        var facility = s.Game.Store.GetById(menu.FacilityBagId);
        if (facility?.FacilityState is null)
        {
            trace(Route.SetRecipeNotFacility, null, null);
            return Changed(s, closed, intent, step, events);
        }

        var updated = facility with
        {
            FacilityState = facility.FacilityState with { ActiveRecipeId = recipeId, RecipeId = null }
        };
        var newState = s.Game.ReplaceBagById(menu.FacilityBagId, updated,
            stack => stack.WithProperty("Progress", new IntValue(0)));

        var owner = OwnerCell(s.Game, menu.FacilityBagId);
        trace(Route.SetRecipe, null, recipeId);
        return Changed(s, closed with { Game = newState }, intent, step, events,
            new Func<int, SimEvent>[] { seq => new SimEvent.RecipeSelected(step, seq, menu.FacilityBagId, recipeId, owner, false) });
    }

    /// <summary>
    /// The recipe menu for <paramref name="facility"/>: every recipe it can build under the known set, with
    /// the selection on the facility's current active recipe when it has one.
    /// </summary>
    private static RecipeMenuState OpenRecipeMenu(Bag facility, SimContent content, ImmutableHashSet<string> knownRecipes)
    {
        var recipes = content.RecipesForFacility(facility.EnvironmentType, knownRecipes);
        var ids = recipes.Select(r => r.Id).ToImmutableArray();
        var names = recipes.Select(r => r.Name).ToImmutableArray();

        var active = facility.FacilityState!.ActiveRecipeId;
        var selected = active is not null ? ids.IndexOf(active) : 0;
        if (selected < 0) selected = 0;

        return new RecipeMenuState(facility.Id, facility.EnvironmentType, ids, names, selected);
    }

    /// <summary>Moves the recipe-menu selection by <paramref name="delta"/>, clamped. Unchanged when closed/empty.</summary>
    private static RecipeMenuState? MoveRecipeMenu(RecipeMenuState? menu, int delta)
    {
        if (menu is null || menu.RecipeIds.IsEmpty) return menu;
        var next = Math.Clamp(menu.SelectedIndex + delta, 0, menu.RecipeIds.Length - 1);
        return next == menu.SelectedIndex ? menu : menu with { SelectedIndex = next };
    }

    // ==================== Inline split ====================

    /// <summary>
    /// The split editor for <paramref name="at"/>'s cursor stack, grab count defaulting to half. Null when
    /// the location or its bag is missing, or the cell holds fewer than two items.
    /// </summary>
    private static SplitModeState? BeginSplit(GameState g, LocationId at)
    {
        if (g.Locations.TryGet(at) is not { } loc || ResolveBag(g, loc) is not { } bag)
            return null;

        var cell = bag.Grid.GetCell(loc.Cursor.Position);
        if (cell.IsEmpty || cell.Stack!.Count <= 1)
            return null;

        var total = cell.Stack.Count;
        return new SplitModeState(at, loc.Cursor.Position, total / 2, total);
    }

    /// <summary>The split's grab count changed by <paramref name="delta"/>, clamped to [1, total − 1]. Unchanged when closed.</summary>
    private static SplitModeState? AdjustSplit(SplitModeState? split, int delta)
    {
        if (split is null) return null;
        var next = split.GrabCount + delta;
        if (next < 1) next = 1;
        if (next > split.StackTotal - 1) next = split.StackTotal - 1;
        return next == split.GrabCount ? split : split with { GrabCount = next };
    }

    // ==================== Cursor + dialogue (moved from GameSession) ====================

    /// <summary>
    /// Moves <paramref name="at"/>'s cursor one cell. A move onto an unnavigable tree cell is refused: the
    /// cursor stays put, a bump is recorded and the Nth-bump beat may fire (Bumped = true). A B cursor
    /// coming to rest on an item runs the cursor-rest triggers. Unchanged when the location or bag is missing.
    /// </summary>
    private static (GameState State, bool Bumped) MoveCursor(
        GameState g, LocationId at, Direction direction, DialogueBook beats, Action<UiTrigger> fired)
    {
        if (g.Locations.TryGet(at) is not { } loc || ResolveBag(g, loc) is not { } activeBag)
            return (g, false);

        var newCursor = loc.Cursor.Move(direction, activeBag.Grid.Rows, activeBag.Grid.Columns);

        // Unnavigable (tree) target: refuse the move. Guard on an actual position change so a wrap no-op on
        // a 1-wide/1-tall grid is never mistaken for a bump.
        if (newCursor.Position != loc.Cursor.Position
            && activeBag.Grid.GetCell(newCursor.Position).IsUnnavigable)
            return (RecordTreeBump(g, beats), true);

        var moved = g with { Locations = g.Locations.Set(at, loc with { Cursor = newCursor }) };

        // Inspecting = the inventory (B) cursor resting on an item: reveals the description pane and
        // feeds the Nth-unique-inspect condition. Inert while a dialogue is blocking the world.
        return (at == LocationId.B ? EvaluateCursorRest(moved, beats, fired) : moved, false);
    }

    /// <summary>
    /// Records a tree bump: advances <see cref="DialogueState.TreeBumpCount"/> and enqueues any
    /// <see cref="DialogueTriggerKind.NthTreeBump"/> beat whose threshold the new count reaches. Touches only
    /// the dialogue substate. Inert while a beat is already showing.
    /// </summary>
    private static GameState RecordTreeBump(GameState state, DialogueBook beats)
    {
        if (state.Dialogue.IsActive)
            return state;

        var dialogue = state.Dialogue.Bump();
        dialogue = EnqueueThresholdBeats(dialogue, beats, DialogueTriggerKind.NthTreeBump, dialogue.TreeBumpCount);
        return state with { Dialogue = dialogue };
    }

    /// <summary>
    /// Enqueues every beat of a threshold-counter trigger whose threshold the counter has just reached and
    /// which hasn't already fired (id-ordered, fire-once).
    /// </summary>
    private static DialogueState EnqueueThresholdBeats(DialogueState dialogue, DialogueBook beats, DialogueTriggerKind kind, int count)
    {
        foreach (var beat in beats.WithTrigger(kind))
            if (beat.Trigger.Threshold == count && !dialogue.HasFired(beat.Id))
                dialogue = dialogue.Enqueue(beat.Id);
        return dialogue;
    }

    /// <summary>
    /// Cursor-rest triggers after a B-cursor move: resting on an item fires <see cref="UiTrigger.FirstCursorRest"/>
    /// (and <see cref="UiTrigger.NoticeClock"/> on the Shrine clock slot) and records a unique inspection,
    /// firing any <see cref="DialogueTriggerKind.NthUniqueInspect"/> beat whose threshold it reaches. Returns
    /// the same state when nothing changes. Each trigger that grew the ledger is reported through <paramref name="fired"/>.
    /// </summary>
    private static GameState EvaluateCursorRest(GameState state, DialogueBook beats, Action<UiTrigger> fired)
    {
        if (state.Dialogue.IsActive)
            return state; // the world is blocked while a beat is showing

        var cell = state.CurrentCell;
        if (cell.IsEmpty)
            return state;

        var ui = state.Ui;
        void Fire(UiTrigger trigger)
        {
            var next = ui.Fire(trigger);
            if (!ReferenceEquals(next, ui))
                fired(trigger);
            ui = next;
        }

        Fire(UiTrigger.FirstCursorRest);
        // Resting on the Shrine's Clock slot is where the player notices "it was always ticking"
        // (journey 15:00): the clock readout materializes. Deterministic — keyed to the clock glyph.
        if (cell.Frame is FeatureSlotFrame { Glyph: FeatureSlotFrame.ClockGlyph })
            Fire(UiTrigger.NoticeClock);
        var dialogue = state.Dialogue;

        var typeName = cell.Stack!.ItemType.Name;
        if (!dialogue.InspectedItems.Contains(typeName))
        {
            dialogue = dialogue.Inspect(typeName);
            dialogue = EnqueueThresholdBeats(dialogue, beats, DialogueTriggerKind.NthUniqueInspect, dialogue.UniqueInspectCount);
        }

        if (ReferenceEquals(ui, state.Ui) && dialogue == state.Dialogue)
            return state;
        return state with { Ui = ui, Dialogue = dialogue };
    }

    /// <summary>
    /// Enqueues every not-yet-fired beat with trigger <paramref name="kind"/> (id-ordered, fire-once) — the
    /// first-failed-peek and peek-while-carrying hooks. Touches only the dialogue substate; inert while a
    /// beat is showing. Returns the same state when nothing changes.
    /// </summary>
    private static GameState FireBeats(GameState state, DialogueBook beats, DialogueTriggerKind kind)
    {
        if (state.Dialogue.IsActive)
            return state;

        var dialogue = state.Dialogue;
        foreach (var beat in beats.WithTrigger(kind))
            dialogue = dialogue.Enqueue(beat.Id);
        return dialogue == state.Dialogue ? state : state with { Dialogue = dialogue };
    }

    /// <summary>
    /// Advances the showing dialogue one line, or dismisses it after its last line (a dismissed beat may
    /// reveal chrome). Returns the dismissed beat id, if any. Unchanged when nothing is showing.
    /// </summary>
    private static (GameState State, string? Dismissed) AdvanceDialogue(GameState state, DialogueBook beats)
    {
        var dialogue = state.Dialogue;
        if (!dialogue.IsActive)
            return (state, null);

        var beat = beats.Get(dialogue.ActiveBeatId!);
        var lineCount = beat?.Lines.Length ?? 1;
        var (advanced, dismissed) = dialogue.Advance(lineCount);

        var next = state with { Dialogue = advanced };
        if (dismissed is not null && beat?.Reveals is ChromeElement reveal)
            next = next with { Ui = next.Ui.With(reveal) };
        return (next, dismissed);
    }

    // ==================== Location helpers ====================

    /// <summary>The bag a location is showing (its trail followed from its root bag); null when broken.</summary>
    internal static Bag? ResolveBag(GameState g, Location loc)
    {
        var bagId = loc.BagId;
        foreach (var entry in loc.Breadcrumbs.Reverse())
        {
            var b = g.Store.GetById(bagId);
            if (b is null) break;
            var c = b.Grid.GetCell(entry.CellIndex);
            if (c.Stack?.ContainedBagId is not { } childId) break;
            bagId = childId;
        }
        return g.Store.GetById(bagId);
    }

    /// <summary>The cell holding <paramref name="bagId"/>'s stack (where a facility's progress lives); null for a root bag.</summary>
    internal static CellRef? OwnerCell(GameState g, Guid bagId) =>
        g.Store.GetOwnerOf(bagId) is { } owner ? new CellRef(owner.ParentBagId, owner.CellIndex) : null;

    /// <summary>The cell under <paramref name="at"/>'s cursor; B's cursor cell when the location or its bag is missing.</summary>
    internal static Cell CellAt(GameState g, LocationId at) =>
        g.Locations.TryGet(at) is { } loc && ResolveBag(g, loc) is { } bag
            ? bag.Grid.GetCell(loc.Cursor.Position)
            : g.CurrentCell;

    /// <summary>
    /// A bare pickup = the contextual primary would grab a resting item into the hand: a non-empty, non-bag,
    /// non-output-slot cell with an empty hand at root depth. Mirrors <see cref="GameState.ToolPrimary"/>'s grab branch.
    /// </summary>
    private static bool IsBarePickup(GameState state, Cell cell) =>
        !state.HasItemsInHand
        && !cell.IsEmpty
        && !cell.HasBag
        && cell.Frame is not OutputSlotFrame
        && !state.IsNested;

    /// <summary>
    /// Fixed-inventory pickup (Slice 3): removes the resting item under the B cursor and acquires it into the
    /// toolbar's first available slot (or inside a non-full toolbar bag once its own slots are full). Leaves
    /// any unplaced remainder in the source cell. A full toolbar fails with the item left in place; an empty
    /// cell or a missing toolbar is a no-op. Returns the count placed.
    /// </summary>
    private static (ToolResult Result, int Placed) PickupToToolbar(GameState state)
    {
        var cell = state.CurrentCell;
        if (cell.IsEmpty || state.ToolbarBagId is not { } toolbarId)
            return (ToolResult.Ok(state), 0);

        var item = cell.Stack!;
        var (afterAcquire, remaining) = state.AcquireIntoBagRecursive(toolbarId, item);

        var placed = item.Count - (remaining?.Count ?? 0);
        if (placed == 0)
            return (ToolResult.Fail(state, "Toolbar is full"), 0);

        // The toolbar lives in a different bag, so the acquire never touched B's active bag — clear the
        // source cell (or leave the remainder) on the post-acquire store.
        var sourceBag = afterAcquire.ActiveBag;
        var newCell = remaining is not null ? cell with { Stack = remaining } : cell with { Stack = null };
        var writer = new ImmutableBagWriter(sourceBag.Grid);
        writer.Set(state.Cursor.Position.ToIndex(sourceBag.Grid.Columns), newCell);
        var clearedGrid = writer.Commit();
        var newState = afterAcquire with
        {
            Store = afterAcquire.Store.Set(afterAcquire.ActiveBagId, sourceBag with { Grid = clearedGrid })
        };
        return (ToolResult.Ok(newState), placed);
    }
}
