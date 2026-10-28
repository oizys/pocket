using Pockets.Core.Models;

namespace Pockets.Core.Sim;

/// <summary>
/// The pure step function (two-layer #23, Step contract; the plan's <c>Sim.Step</c>, named
/// <c>Simulation</c> because a class may not share its namespace's name without shadowing it).
/// Slice 1.2: extracted by <i>moving</i> <c>GameSession.ApplyResult</c> / <c>TickFacilities</c> /
/// <c>PlantLogic.TickPlants</c> / <c>RunTransitionHooks</c>, keeping their exact order:
/// apply the tool → (if time advances) tick facilities → tick plants → run the transition hooks.
/// Slice 1.3: every controller path is a data <see cref="Intent"/> dispatched here (see
/// <c>Simulation.Intents.cs</c>); the session is the shell around it: undo, action log, intent log.
/// </summary>
public static partial class Simulation
{
    /// <summary>
    /// Applies <paramref name="intents"/> in order and returns the next state plus the step's coarse
    /// events. Every event carries <c>Step = s.TickCount + 1</c> and a step-global <c>Seq</c>.
    /// Per intent:
    /// <list type="bullet">
    /// <item>Tool-style intents (the tools, the routed panel verbs, <see cref="Intent.Dsl"/>): a failed tool
    /// emits only <see cref="SimEvent.IntentRejected"/> (state unchanged); a no-op success (tool state
    /// record-equal to the pre-state) emits nothing and does not tick; otherwise
    /// <see cref="SimEvent.IntentApplied"/>, then, in rogue mode, the tick phase, then the hooks, and
    /// <c>TickCount + 1</c>.</item>
    /// <item><see cref="Intent.Tick"/>: tick phase then hooks; if the result is record-equal to the
    /// pre-state it is a no-op (no events, no TickCount change).</item>
    /// <item>Cursor, dialogue, split, recipe-menu and clock intents: they change their L1 field directly and
    /// emit <see cref="SimEvent.IntentApplied"/> when anything changed. No tick, no hooks, no TickCount bump
    /// (exactly as before they were intents).</item>
    /// </list>
    /// Every random draw comes from <see cref="SimState.Rng"/> positioned at the step (Slice 1.1): the
    /// AcquireRandom type from <see cref="SimStream.Acquire"/>, DSL draws from <see cref="SimStream.Dsl"/>,
    /// recipe outputs from <see cref="SimStream.Craft"/>. The advanced RNG is kept only when the intent changes
    /// state. <see cref="Intent.Undo"/> is the session shell's (it owns the snapshots) and throws here.
    /// </summary>
    public static StepResult<SimState> Step(SimState s, IReadOnlyList<Intent> intents) =>
        StepTraced(s, intents, null);

    /// <summary>
    /// <see cref="Step"/>, also reporting how each intent resolved (one <see cref="StepTrace"/> per
    /// non-composite intent, in order) for the session shell's log lines and the controller's focus.
    /// </summary>
    internal static StepResult<SimState> StepTraced(SimState s, IReadOnlyList<Intent> intents, List<StepTrace>? traces)
    {
        var step = s.TickCount + 1;
        var events = ImmutableArray.CreateBuilder<SimEvent>();
        foreach (var intent in intents)
            s = StepOne(s, intent, step, events, traces);
        return new StepResult<SimState>(s, events.ToImmutable());
    }

    /// <summary>
    /// Applies one tool's result as a player action: a failure emits only
    /// <see cref="SimEvent.IntentRejected"/>; a no-op success emits nothing; otherwise
    /// <see cref="SimEvent.IntentApplied"/>, the tick phase in rogue mode (P1-D2), the transition hooks,
    /// then <paramref name="after"/> on the hooked state, and <c>TickCount + 1</c>. <paramref name="rng"/> is
    /// the step's RNG (already advanced by any draw the tool made); it is committed with the state.
    /// </summary>
    private static SimState ApplyToolResult(
        SimState s, Intent intent, ToolResult result, SimRng rng, int step,
        ImmutableArray<SimEvent>.Builder events, Func<GameState, GameState>? after = null)
    {
        var before = s.Game;
        if (!result.Success)
        {
            events.Add(new SimEvent.IntentRejected(step, events.Count, intent, result.Error ?? ""));
            return s;
        }

        // No-op success: state didn't change, nothing ticks and nothing is recorded.
        if (result.State == before)
            return s;

        var pending = new List<Func<int, SimEvent>> { seq => new SimEvent.IntentApplied(step, seq, intent) };

        // In rogue mode, tick facilities and plants after each action. In realtime, ticks are external.
        var newState = s.Content.TickMode == TickMode.Rogue
            ? AdvanceTime(result.State, before, s.Content, step, ref rng, pending)
            : result.State;

        // Chrome-as-state + structural beats + known-recipes + minimap zones: the shared transition
        // hooks. They run after every state-changing action (tool actions, panel open/close,
        // enter/leave, clock ticks), so the demo profile's chrome grows identically on every
        // frontend/driver and a craft completing during a tick fires exactly like a player action.
        newState = RunTransitionHooks(before, newState, s.Content, trigger =>
            pending.Add(seq => new SimEvent.UiTriggerFired(step, seq, trigger)));

        if (after is not null)
            newState = after(newState);

        foreach (var make in pending)
            events.Add(make(events.Count));

        return s with { Game = newState, TickCount = s.TickCount + 1, Rng = rng };
    }

    /// <summary>
    /// Runs <paramref name="tool"/> at location <paramref name="at"/>: the tools are written against B, so
    /// the location's info is swapped into B (<see cref="SwapIn"/>), the whole tool step (tool, tick, hooks)
    /// runs in that frame, and B is restored afterwards (<see cref="SwapOut"/>) — moved verbatim from
    /// <c>GameSession.RunAt</c>. The tool receives the step's RNG and returns it, advanced if it drew.
    /// </summary>
    private static SimState ApplyToolAt(
        SimState s, Intent intent, LocationId at, Func<GameState, SimRng, (ToolResult Result, SimRng Rng)> tool,
        int step, ImmutableArray<SimEvent>.Builder events)
    {
        var swapped = s with { Game = SwapIn(s.Game, at) };
        var (result, rng) = tool(swapped.Game, s.Rng.AtStep(step));
        var applied = ApplyToolResult(swapped, intent, result, rng, step, events);
        return ReferenceEquals(applied, swapped) ? s : applied with { Game = SwapOut(applied.Game, s.Game, at) };
    }

    /// <summary><see cref="ApplyToolAt(SimState, Intent, LocationId, Func{GameState, SimRng, ValueTuple{ToolResult, SimRng}}, int, ImmutableArray{SimEvent}.Builder)"/> for a tool that draws nothing.</summary>
    private static SimState ApplyToolAt(
        SimState s, Intent intent, LocationId at, Func<GameState, ToolResult> tool,
        int step, ImmutableArray<SimEvent>.Builder events) =>
        ApplyToolAt(s, intent, at, (g, rng) => (tool(g), rng), step, events);

    /// <summary>
    /// The state the B-written tools see when run at <paramref name="at"/>: <paramref name="at"/>'s location
    /// info installed as B. Identity for B. The shell also uses it to name a tool's log line and to take the
    /// undo snapshot exactly as <c>RunAt</c> did (it pushed the swapped state).
    /// </summary>
    internal static GameState SwapIn(GameState g, LocationId at) =>
        at == LocationId.B ? g : g with { Locations = g.Locations.Set(LocationId.B, g.Locations.Get(at)) };

    /// <summary>
    /// Undoes <see cref="SwapIn"/> after a tool ran: B gets back <paramref name="pre"/>'s B info and
    /// <paramref name="at"/> takes whatever B became. Identity for B.
    /// </summary>
    private static GameState SwapOut(GameState after, GameState pre, LocationId at) =>
        at == LocationId.B
            ? after
            : after with
            {
                Locations = after.Locations
                    .Set(LocationId.B, pre.Locations.Get(LocationId.B))
                    .Set(at, after.Locations.Get(LocationId.B))
            };

    /// <summary>
    /// A clock tick: the tick phase then the hooks. A tick that changed nothing is a no-op (checked after
    /// the hooks, as <c>Tick()</c> always did): no events, no TickCount change.
    /// </summary>
    private static SimState ApplyTick(SimState s, Intent intent, int step, ImmutableArray<SimEvent>.Builder events)
    {
        var before = s.Game;
        var rng = s.Rng.AtStep(step);
        var pending = new List<Func<int, SimEvent>>();
        var newState = AdvanceTime(before, before, s.Content, step, ref rng, pending);
        newState = RunTransitionHooks(before, newState, s.Content, trigger =>
            pending.Add(seq => new SimEvent.UiTriggerFired(step, seq, trigger)));
        if (newState == before)
            return s;

        foreach (var make in pending)
            events.Add(make(events.Count));
        return s with { Game = newState, TickCount = s.TickCount + 1, Rng = rng };
    }

    /// <summary>
    /// A direct L1 change that is not a timed action (cursor, dialogue, modal state, clock readout, recipe
    /// switch): emits <see cref="SimEvent.IntentApplied"/> when <paramref name="next"/> differs from
    /// <paramref name="s"/>; no tick, no hooks, no TickCount bump.
    /// </summary>
    private static SimState Changed(SimState s, SimState next, Intent intent, int step, ImmutableArray<SimEvent>.Builder events)
    {
        if (next == s)
            return s;
        events.Add(new SimEvent.IntentApplied(step, events.Count, intent));
        return next;
    }

    /// <summary>A refusal that changes nothing: emits only <see cref="SimEvent.IntentRejected"/>.</summary>
    private static SimState Rejected(SimState s, Intent intent, string reason, int step, ImmutableArray<SimEvent>.Builder events)
    {
        events.Add(new SimEvent.IntentRejected(step, events.Count, intent, reason));
        return s;
    }

    /// <summary>
    /// The time phase: tick facilities (emitting <see cref="SimEvent.TimeAdvanced"/> and one
    /// <see cref="SimEvent.CraftCompleted"/> per finished craft, in facility order), then plants.
    /// <paramref name="preStep"/> supplies the known-recipe set, exactly as the session read
    /// <c>Current.KnownRecipes</c> (the pre-action state) while ticking the post-tool state. Recipe
    /// outputs draw from <paramref name="rng"/>'s <see cref="SimStream.Craft"/> stream, which is advanced in place.
    /// </summary>
    private static GameState AdvanceTime(
        GameState state, GameState preStep, SimContent content, int step, ref SimRng rng,
        List<Func<int, SimEvent>> pending)
    {
        pending.Add(seq => new SimEvent.TimeAdvanced(step, seq));
        var craftRandom = rng.AsRandom(SimStream.Craft);
        state = TickFacilities(state, preStep.KnownRecipes, content, craftRandom, (facilityId, recipeId) =>
            pending.Add(seq => new SimEvent.CraftCompleted(step, seq, facilityId, recipeId)));
        rng = craftRandom.Rng;
        return PlantLogic.TickPlants(state);
    }

    /// <summary>
    /// All facilities in a deterministic, GUID-free order: env → active recipe → owning cell index (all
    /// pure functions of state). The single source of facility ordering for both the tick loop and the
    /// action-queue projection, so the completion log and the queue panel never disagree — bag ids are
    /// process-fresh GUIDs, so the store's dictionary order would otherwise diverge across drivers.
    /// </summary>
    public static IEnumerable<Bag> OrderedFacilities(GameState state) =>
        state.Store.Facilities
            .OrderBy(f => f.EnvironmentType, StringComparer.Ordinal)
            .ThenBy(f => f.FacilityState?.ActiveRecipeId ?? f.FacilityState?.RecipeId ?? "", StringComparer.Ordinal)
            .ThenBy(f => state.Store.GetOwnerOf(f.Id)?.CellIndex ?? 0);

    /// <summary>
    /// Ticks all facility bags found via the BagRegistry.
    /// Updates each facility in-place within the bag tree.
    /// Uses FacilityRecipeMap when available, falls back to RecipeRegistry.
    /// Reports each craft completion (facility id, recipe id) through <paramref name="completed"/>. Output
    /// factories draw from <paramref name="random"/> in facility order.
    /// </summary>
    private static GameState TickFacilities(
        GameState state, ImmutableHashSet<string> knownRecipes, SimContent content, Random random,
        Action<Guid, string> completed)
    {
        if (content.Recipes.IsDefaultOrEmpty)
            return state;

        var facilities = OrderedFacilities(state).ToList();
        if (facilities.Count == 0)
            return state;

        foreach (var facility in facilities)
        {
            var facilityRecipes = content.RecipesForFacility(facility.EnvironmentType, knownRecipes);
            if (facilityRecipes.Count == 0)
                continue;

            // Read current progress from the owning ItemStack's properties (shared owner lookup).
            int currentProgress = state.Store.GetOwnerStack(facility.Id)?.GetInt("Progress") ?? 0;

            var wasCrafting = facility.FacilityState?.RecipeId;
            var (ticked, newProgress, newBags) = FacilityLogic.Tick(facility, currentProgress, facilityRecipes, random);
            if (ticked == facility && newProgress == currentProgress)
                continue;

            // Detect craft completion: was crafting, now reset to null
            if (wasCrafting is not null && ticked.FacilityState?.RecipeId is null)
                completed(facility.Id, wasCrafting);

            // Register any newly created bags from the output factory
            if (newBags.Count > 0)
                state = state with { Store = state.Store.AddRange(newBags) };

            // Update both the facility bag and the owning stack's progress property
            var progressValue = newProgress;
            state = state.ReplaceBagById(facility.Id, ticked,
                stack => stack.WithProperty("Progress", new IntValue(progressValue)));
        }

        return state;
    }

    /// <summary>
    /// Runs the shared state→state transition hooks after any successful mutation, whether from a
    /// player action or a facility tick: chrome triggers, structural dialogue beats, known-recipe
    /// learning, and minimap zone entry. Each trigger that grows the UI ledger is reported through
    /// <paramref name="fired"/>.
    /// </summary>
    private static GameState RunTransitionHooks(
        GameState before, GameState after, SimContent content, Action<UiTrigger> fired)
    {
        after = ApplyUiTriggers(before, after, fired);
        after = FireStructuralDialogue(before, after, content.Beats);
        after = RegisterKnownRecipes(after);
        after = RegisterZoneEntry(before, after);
        return after;
    }

    /// <summary>
    /// Reveals chrome for the gameplay transitions between two states. Structural (not per-tool):
    ///   • hand OR toolbar gained an item ⇒ FirstPickup   (Toolbar)
    ///   • breadcrumb depth increased     ⇒ FirstEnter    (Breadcrumbs)
    ///   • a C/W look-in panel opened     ⇒ FirstPeek     (LookInOverlay)
    /// Each Fire is idempotent, so this runs on every successful action and only the FIRST
    /// occurrence flips a flag (and only that one is reported through <paramref name="fired"/>).
    /// The toolbar arm makes the demo's pickup-to-toolbar routing (Slice 3) light the toolbar the
    /// same way a classic grab-into-hand does.
    /// </summary>
    private static GameState ApplyUiTriggers(GameState before, GameState after, Action<UiTrigger> fired)
    {
        var ui = after.Ui;

        // Fire + report only when the ledger actually grew (UiLedger.Fire returns the same instance otherwise).
        void Fire(UiTrigger trigger)
        {
            var next = ui.Fire(trigger);
            if (!ReferenceEquals(next, ui))
                fired(trigger);
            ui = next;
        }

        if ((!before.HasItemsInHand && after.HasItemsInHand)
            || ToolbarItemCount(after) > ToolbarItemCount(before))
            Fire(UiTrigger.FirstPickup);

        if (after.BreadcrumbStack.Count() > before.BreadcrumbStack.Count())
            Fire(UiTrigger.FirstEnter);

        if (PanelNewlyOpen(before, after, LocationId.C) || PanelNewlyOpen(before, after, LocationId.W))
            Fire(UiTrigger.FirstPeek);

        // Crossing into the Shrine bag materializes the Shrine slots view (journey 12:00).
        if (JustEnteredShrine(before, after))
            Fire(UiTrigger.EnterShrine);

        // A newly-locked feature slot = a core was just slotted: game-wide fullness pips turn on
        // (journey 28:00 — the first unlock that rewires rendering everywhere).
        if (LockedFeatureSlotCount(after) > LockedFeatureSlotCount(before))
            Fire(UiTrigger.CoreSlotted);

        // A Crafting Table just began crafting (RecipeId null→set) ⇒ FirstTimedAction (journey 24:00):
        // the action-queue panel materializes to show the timed work. Scoped to the Crafting Table
        // because that is the demo's designated first *shown* timed craft (the earlier Slice-3 Workshop
        // craft teaches toolbar-sourced grab-for-move, not the queue). Detected structurally so it fires
        // the same way whether the craft starts during a scripted tick (advanceTime) or any other path.
        if (CraftingTableActiveCount(after) > CraftingTableActiveCount(before))
            Fire(UiTrigger.FirstTimedAction);

        // A Quiet Compass just appeared in the world (craft complete) ⇒ CompassCrafted (journey 26:00):
        // the minimap/radar materializes. Counting the item is robust to where the compass lands.
        if (CompassCount(after) > CompassCount(before))
            Fire(UiTrigger.CompassCrafted);

        return ReferenceEquals(ui, after.Ui) ? after : after with { Ui = ui };
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

    /// <summary>Enqueues the slotting-resolution beat when a feature slot is newly locked (core slotted).</summary>
    private static GameState FireStructuralDialogue(GameState before, GameState after, DialogueBook beats)
    {
        if (LockedFeatureSlotCount(after) <= LockedFeatureSlotCount(before))
            return after;

        var dialogue = after.Dialogue;
        foreach (var beat in beats.WithTrigger(DialogueTriggerKind.CoreSlotted))
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
}
