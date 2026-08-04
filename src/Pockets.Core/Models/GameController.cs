namespace Pockets.Core.Models;

/// <summary>
/// A transient, non-serialized presentation cue the frontends read after an input to play a
/// one-shot affordance. NOT part of the game state or the parity view-model (so it never perturbs
/// checkpoints/goldens) — it lives on the per-frontend <see cref="GameController"/> and is consumed
/// on read (<see cref="GameController.ConsumeFeedbackPulse"/>). The demo's only pulse today is the
/// enter-only <see cref="FailedPeek"/> shake/flash; the beat teaches the rule once, the pulse fires
/// every time so the tell survives after the dialogue is spent.
/// </summary>
public enum FeedbackPulse
{
    None,
    FailedPeek,

    /// <summary>
    /// A cursor move refused by an unnavigable tree cell (Slice 6, journey 15:30). Fired every time
    /// the player bumps a tree — "solid, no path" — while the fire-once axe-absence beat only plays
    /// on the Nth bump. The frontends play a one-shot shake/flash, same posture as <see cref="FailedPeek"/>.
    /// </summary>
    Bump
}

/// <summary>
/// Pure input→state pipeline for the game. Maps abstract GameKeys and mouse clicks
/// to GameSession operations. No UI framework dependency — testable with xUnit.
/// Tracks which panel has focus for input routing.
/// </summary>
public class GameController
{
    private GameSession _session;
    private LocationId _focus = LocationId.B;
    private FeedbackPulse _feedbackPulse = FeedbackPulse.None;

    /// <summary>
    /// The injected game clock (Slice 5). Defaults to a <see cref="VirtualGameClock"/> so every
    /// controller — the parity harness included — is deterministic out of the box: game time moves
    /// only through <see cref="AdvanceClock"/> (scripted). Live frontends inject a
    /// <see cref="SystemGameClock"/> via <see cref="SetClock"/> and call <see cref="SyncClock"/>.
    /// </summary>
    private IGameClock _clock = new VirtualGameClock();

    /// <summary>
    /// Order in which Tab cycles through panels.
    /// Only panels present in the current LocationMap are included.
    /// </summary>
    private static readonly LocationId[] FocusCycleOrder = { LocationId.T, LocationId.C, LocationId.B, LocationId.W };

    public GameController(GameSession session)
    {
        _session = session;
    }

    /// <summary>
    /// The current game session state.
    /// </summary>
    public GameSession Session => _session;

    /// <summary>
    /// Which panel currently has focus for input routing.
    /// </summary>
    public LocationId Focus => _focus;

    /// <summary>The injected game clock (Slice 5).</summary>
    public IGameClock Clock => _clock;

    /// <summary>
    /// Injects a game clock (a live <see cref="SystemGameClock"/> for wall-clock frontends) and
    /// immediately syncs its reading onto the session. Live builds call this once at startup.
    /// </summary>
    public void SetClock(IGameClock clock)
    {
        _clock = clock;
        SyncClock();
    }

    /// <summary>Copies the injected clock's current reading onto the session (the clock-readout value).</summary>
    public ControllerResult SyncClock()
    {
        _session = _session.WithElapsed(_clock.Elapsed);
        return ControllerResult.Handle(_session, "Clock: sync");
    }

    /// <summary>
    /// Advances the game clock by <paramref name="delta"/> and syncs it onto the session. This is the
    /// scripted-advance path (the journey runner's <c>advanceTime</c> / the debug <c>advanceTime</c>
    /// action) that keeps harness time fully deterministic. A no-op on a wall-clock
    /// <see cref="SystemGameClock"/>, whose time is driven by <see cref="SyncClock"/> instead.
    /// </summary>
    public ControllerResult AdvanceClock(TimeSpan delta)
    {
        _clock.Advance(delta);
        return SyncClock();
    }

    /// <summary>
    /// Reads and clears the pending one-shot presentation cue (see <see cref="FeedbackPulse"/>).
    /// Frontends call this once per UI refresh; it returns <see cref="FeedbackPulse.None"/> when
    /// there is nothing to play. Consume-on-read keeps the cue from leaking into a later frame.
    /// </summary>
    public FeedbackPulse ConsumeFeedbackPulse()
    {
        var pulse = _feedbackPulse;
        _feedbackPulse = FeedbackPulse.None;
        return pulse;
    }

    /// <summary>
    /// Handles a logical key press. Returns a ControllerResult with the updated session,
    /// a status message, and whether the key was handled.
    /// </summary>
    public ControllerResult HandleKey(GameKey key, Random? rng = null)
    {
        // One-shot presentation cues are per-key: clear any un-consumed pulse before handling.
        _feedbackPulse = FeedbackPulse.None;

        // Dialogue is modal-lite: while a beat is showing, Primary advances/dismisses it and every
        // other key is swallowed (handled, no state change) — the input's first job is turning pages,
        // before movement exists, and nothing leaks to the cursor underneath.
        if (_session.Current.Dialogue.IsActive)
        {
            if (key == GameKey.Primary)
            {
                _session = _session.AdvanceDialogue();
                var showing = _session.Current.Dialogue.IsActive ? "advance" : "dismiss";
                return ControllerResult.Handle(_session, $"Dialogue: {showing}");
            }
            return ControllerResult.Handle(_session, "Dialogue: showing (Primary to continue)");
        }

        // Inline split mode owns its keys: ←/→ adjust, Enter commit, Esc cancel.
        // Every other key is swallowed while in split mode so it behaves like a
        // focused mini-mode without being a true modal.
        if (_session.SplitMode is not null)
        {
            switch (key)
            {
                case GameKey.Left:
                    _session = _session.AdjustSplit(-1);
                    return ControllerResult.Handle(_session, $"Split: grab {_session.SplitMode?.GrabCount}");
                case GameKey.Right:
                    _session = _session.AdjustSplit(+1);
                    return ControllerResult.Handle(_session, $"Split: grab {_session.SplitMode?.GrabCount}");
                case GameKey.Confirm:
                    _session = _session.CommitSplit();
                    return ControllerResult.Handle(_session, "Split: commit");
                case GameKey.Cancel:
                    _session = _session.CancelSplit();
                    return ControllerResult.Handle(_session, "Split: cancel");
                default:
                    return ControllerResult.Handle(_session, "Split: in progress (←/→ adjust, Enter, Esc)");
            }
        }

        // Begin inline split for the focused cursor cell.
        if (key == GameKey.BeginSplit)
        {
            var maybeNew = _session.BeginSplit(_focus);
            if (maybeNew.SplitMode is not null)
            {
                _session = maybeNew;
                return ControllerResult.Handle(_session,
                    $"Split: grab {_session.SplitMode!.GrabCount} / {_session.SplitMode.StackTotal}");
            }
            return ControllerResult.Handle(_session, "Split: nothing to split");
        }

        // Focus cycling
        if (key == GameKey.FocusNext)
        {
            CycleFocus(1);
            return ControllerResult.Handle(_session, $"Focus: {_focus}");
        }
        if (key == GameKey.FocusPrev)
        {
            CycleFocus(-1);
            return ControllerResult.Handle(_session, $"Focus: {_focus}");
        }

        // Direction keys — move cursor in the focused panel
        var direction = key switch
        {
            GameKey.Up => (Direction?)Direction.Up,
            GameKey.Down => (Direction?)Direction.Down,
            GameKey.Left => (Direction?)Direction.Left,
            GameKey.Right => (Direction?)Direction.Right,
            _ => null
        };

        if (direction is not null)
        {
            var (moved, treeBumped) = _session.TryMoveCursorAt(_focus, direction.Value);
            _session = moved;
            // A move refused by an unnavigable tree queues the one-shot bump shake/flash (the fire-once
            // axe-absence beat is handled inside the session; this cue fires on every bump).
            if (treeBumped) _feedbackPulse = FeedbackPulse.Bump;
            return ControllerResult.Handle(_session, $"Move: {direction.Value} ({_focus})");
        }

        // Undo
        if (key == GameKey.Undo)
        {
            var undone = _session.Undo();
            if (undone is not null)
            {
                _session = undone;
                return ControllerResult.Handle(_session, "Undo");
            }
            return ControllerResult.Handle(_session, "Nothing to undo");
        }

        // Look-in peek (C) — a generic look-in overlay, one deep for the demo. Opens/closes the
        // C container panel for the cursor bag (any peekable bag, plain chest included); on an
        // enter-only bag it is refused with a failed-peek affordance instead of opening.
        if (key == GameKey.Peek)
            return ExecutePeek();

        // Leave/close — if focused on C or W, close that panel. Otherwise leave bag.
        if (key == GameKey.LeaveBag)
        {
            if (_focus is LocationId.C or LocationId.W)
            {
                var closeResult = _session.Current.ClosePanel(_focus);
                if (closeResult.Success)
                {
                    _session = _session.ApplyToolResult(closeResult, () => $"Close: {_focus}");
                    _focus = LocationId.B;
                    return ControllerResult.Handle(_session, $"Closed panel, focus → B");
                }
            }
            _session = _session.ExecuteLeaveBag();
            return ControllerResult.Handle(_session, "Action: LeaveBag");
        }

        // Action keys — most operate on the focused panel; AcquireRandom is a debug
        // tool tied to the root bag and stays B-only.
        GameSession? newSession = key switch
        {
            GameKey.Primary => ExecuteFocusedPrimary(),
            GameKey.Secondary => _session.ExecuteSecondary(_focus),
            GameKey.QuickSplit => _session.ExecuteQuickSplit(_focus),
            GameKey.Sort => _session.ExecuteSort(_focus),
            GameKey.AcquireRandom => _session.ExecuteAcquireRandom(rng ?? new Random()),
            GameKey.CycleRecipe => _session.ExecuteCycleRecipe(_focus),
            _ => null
        };

        if (newSession is not null)
        {
            _session = newSession;
            return ControllerResult.Handle(_session, $"Action: {key}");
        }

        return ControllerResult.Unhandled(_session);
    }

    /// <summary>
    /// Executes Primary action with panel-aware bag opening and routing.
    /// On B with a bag cell: opens as C/W if not already open (no-op if already open).
    /// On any other panel: temporarily swaps B with the focused panel so the
    /// existing tool methods (which read from B) operate on the right cell.
    /// </summary>
    private GameSession ExecuteFocusedPrimary()
    {
        var state = _session.Current;

        // Resolve the focused panel's cursor cell
        var focusedCell = ResolveFocusedCell(state);

        // Toggle: clicking a bag in B that's already open as C/W closes that panel
        if (_focus == LocationId.B && focusedCell.HasBag)
        {
            var bagId = focusedCell.Stack!.ContainedBagId!.Value;
            var bag = state.Store.GetById(bagId);

            if (bag is not null)
            {
                // If this bag is already open as C or W, close that panel
                if (state.Locations.TryGet(LocationId.C) is { } cLoc && cLoc.BagId == bagId)
                {
                    var closeResult = state.ClosePanel(LocationId.C);
                    if (closeResult.Success)
                    {
                        _session = _session.ApplyToolResult(closeResult, () => $"Close: {bag.EnvironmentType}");
                        return _session;
                    }
                }
                if (state.Locations.TryGet(LocationId.W) is { } wLoc && wLoc.BagId == bagId)
                {
                    var closeResult = state.ClosePanel(LocationId.W);
                    if (closeResult.Success)
                    {
                        _session = _session.ApplyToolResult(closeResult, () => $"Close: {bag.EnvironmentType}");
                        return _session;
                    }
                }
            }
        }

        // Open as C/W when clicking a bag in B (only if not already open and hand is empty)
        if (_focus == LocationId.B && focusedCell.HasBag && !state.HasItemsInHand)
        {
            var bagId = focusedCell.Stack!.ContainedBagId!.Value;
            var bag = state.Store.GetById(bagId);

            if (bag is not null)
            {
                if (GameState.IsFacilityBag(bag))
                {
                    var result = state.OpenAsContainer(bagId);
                    if (result.Success)
                    {
                        _session = _session.ApplyToolResult(result, () => $"Open Container: {bag.EnvironmentType}");
                        _focus = LocationId.C;
                        return _session;
                    }
                }
                else if (GameState.IsWildernessType(bag.EnvironmentType))
                {
                    var result = state.OpenAsWorld(bagId);
                    if (result.Success)
                    {
                        _session = _session.ApplyToolResult(result, () => $"Open World: {bag.EnvironmentType}");
                        _focus = LocationId.W;
                        return _session;
                    }
                }
            }
        }

        // Fixed-inventory routing (Slice 3): in the demo profile a bare pickup in the inventory (B)
        // flies to the toolbar instead of the hand. Gated on _focus == B so grabbing FROM the toolbar
        // or a facility panel keeps the classic grab-into-hand (the "grab-for-move" cut buffer stays
        // reachable). Non-demo profiles have ToolbarPickup == false → this is never taken.
        if (_focus == LocationId.B && state.ToolbarPickup && IsBarePickup(state, focusedCell))
            return _session.ExecutePickupToToolbar();

        return _session.ExecutePrimary(_focus);
    }

    /// <summary>
    /// Look-in peek (C): opens or closes a generic one-deep look-in overlay (the C container panel)
    /// over the cursor bag. Distinct from Primary/E, which ENTERS a plain bag via breadcrumbs —
    /// peek lets the player look into and arrange a bag without going anywhere (journey 7:00, the Chest).
    /// Works on ANY peekable bag, plain chests included, so it isn't limited to the facility/wilderness
    /// bags Primary already routes to C/W.
    ///
    /// Toggle + refusal rules:
    ///   • focused on a look-in panel (C/W) → C closes it (mirrors Q);
    ///   • cursor bag already peeked (open as C) → C closes it;
    ///   • cursor bag is <see cref="Bag.EnterOnly"/> → the peek is REFUSED: no panel opens, the cursor
    ///     and world are untouched, the first-failed-peek beat fires once, and a
    ///     <see cref="FeedbackPulse.FailedPeek"/> shake/flash is queued for the frontends. The refused
    ///     peek is the only tell (no glyph — RATIFIED). E still enters the bag normally.
    ///   • otherwise → open the bag as a C look-in overlay (FirstPeek → LookInOverlay fires structurally
    ///     in <see cref="GameSession.ApplyUiTriggers"/> when C newly opens), focus moves to C.
    /// </summary>
    private ControllerResult ExecutePeek()
    {
        var state = _session.Current;

        // A focused look-in panel: C toggles it closed, same as Q.
        if (_focus is LocationId.C or LocationId.W)
        {
            var closeResult = state.ClosePanel(_focus);
            if (closeResult.Success)
            {
                var closed = _focus;
                _session = _session.ApplyToolResult(closeResult, () => $"Close peek: {closed}");
                _focus = LocationId.B;
                return ControllerResult.Handle(_session, $"Peek: closed {closed}");
            }
            return ControllerResult.Handle(_session, "Peek: nothing to close");
        }

        var cell = ResolveFocusedCell(state);
        if (!cell.HasBag)
            return ControllerResult.Handle(_session, "Peek: nothing to look into");

        var bagId = cell.Stack!.ContainedBagId!.Value;
        var bag = state.Store.GetById(bagId);
        if (bag is null)
            return ControllerResult.Handle(_session, "Peek: bag not found");

        // Enter-only: refuse the peek. The failed peek is the affordance — fire the beat once, queue
        // the shake, and leave everything else exactly as it was (a true no-op on the world/cursor).
        if (bag.EnterOnly)
        {
            _session = _session.FireFailedPeek();
            _feedbackPulse = FeedbackPulse.FailedPeek;
            return ControllerResult.Handle(_session, "Can't just peek at this one — press E to enter.");
        }

        // Cursor bag already peeked → toggle it closed.
        if (state.Locations.TryGet(LocationId.C) is { } cLoc && cLoc.BagId == bagId)
        {
            var closeResult = state.ClosePanel(LocationId.C);
            if (closeResult.Success)
            {
                _session = _session.ApplyToolResult(closeResult, () => $"Close peek: {bag.EnvironmentType}");
                _focus = LocationId.B;
                return ControllerResult.Handle(_session, $"Peek: closed {bag.EnvironmentType}");
            }
        }

        // Open the generic look-in overlay.
        var openResult = state.OpenAsContainer(bagId);
        if (openResult.Success)
        {
            _session = _session.ApplyToolResult(openResult, () => $"Peek: {bag.EnvironmentType}");
            _focus = LocationId.C;
            // Capacity-absence beat (Slice 5): peering into a bag while the hand is carrying something
            // fires the "look inside every single one" line once (deterministic, fire-once).
            if (_session.Current.HasItemsInHand)
                _session = _session.FirePeekWhileCarrying();
            return ControllerResult.Handle(_session, $"Peek: {bag.EnvironmentType}");
        }
        return ControllerResult.Handle(_session, "Peek: could not open");
    }

    /// <summary>
    /// A bare pickup = the contextual Primary would grab a resting item into the hand: a non-empty,
    /// non-bag, non-output-slot cell with an empty hand at root depth. (A bag enters, an output slot
    /// or nested cell has its own verb, a full hand drops/swaps — none of those are "picking up".)
    /// Mirrors <see cref="GameState.ToolPrimary"/>'s grab branch so routing and semantics stay in lockstep.
    /// </summary>
    private static bool IsBarePickup(GameState state, Cell cell) =>
        !state.HasItemsInHand
        && !cell.IsEmpty
        && !cell.HasBag
        && cell.Frame is not OutputSlotFrame
        && !state.IsNested;

    /// <summary>
    /// Resolves the cell at the focused panel's cursor.
    /// </summary>
    private Cell ResolveFocusedCell(GameState state)
    {
        var loc = state.Locations.TryGet(_focus);
        if (loc is null) return state.CurrentCell;

        var bagId = loc.BagId;
        foreach (var entry in loc.Breadcrumbs.Reverse())
        {
            var b = state.Store.GetById(bagId);
            if (b is null) break;
            var c = b.Grid.GetCell(entry.CellIndex);
            if (c.Stack?.ContainedBagId is not { } childId) break;
            bagId = childId;
        }
        var bag = state.Store.GetById(bagId);
        if (bag is null) return state.CurrentCell;
        return bag.Grid.GetCell(loc.Cursor.Position);
    }

    /// <summary>
    /// Cycles focus to the next (or previous) open panel.
    /// </summary>
    private void CycleFocus(int direction)
    {
        var open = FocusCycleOrder
            .Where(id => _session.Current.Locations.Has(id))
            .ToList();

        if (open.Count <= 1) return;

        var currentIdx = open.IndexOf(_focus);
        if (currentIdx < 0) currentIdx = 0;

        var nextIdx = (currentIdx + direction + open.Count) % open.Count;
        _focus = open[nextIdx];
    }

    /// <summary>
    /// Handles a mouse click on a grid cell in a specific panel.
    /// Switches focus to the clicked panel, moves its cursor, then executes the action.
    /// </summary>
    public ControllerResult HandleGridClick(LocationId panelId, Position pos, ClickType type)
    {
        // Auto-switch focus to the clicked panel
        if (_session.Current.Locations.Has(panelId))
            _focus = panelId;

        // Move the cursor of the focused panel
        var loc = _session.Current.Locations.TryGet(_focus);
        if (loc is not null)
        {
            var newLoc = loc with { Cursor = new Cursor(pos) };
            var newState = _session.Current with
            {
                Locations = _session.Current.Locations.Set(_focus, newLoc)
            };
            _session = _session with { Current = newState };
        }

        _session = type switch
        {
            ClickType.Primary => ExecuteFocusedPrimary(),
            ClickType.Secondary => _session.ExecuteSecondary(_focus),
            _ => _session
        };

        var clickName = type == ClickType.Primary ? "LClick" : "RClick";
        return ControllerResult.Handle(_session, $"{clickName} {_focus}: ({pos.Row},{pos.Col})");
    }

    /// <summary>
    /// Backward-compatible overload for B panel clicks.
    /// </summary>
    public ControllerResult HandleGridClick(Position pos, ClickType type) =>
        HandleGridClick(LocationId.B, pos, type);

    /// <summary>
    /// Handles a click on the back/leave-bag button.
    /// </summary>
    public ControllerResult HandleBackClick()
    {
        if (_focus is LocationId.C or LocationId.W)
        {
            var closeResult = _session.Current.ClosePanel(_focus);
            if (closeResult.Success)
            {
                _session = _session.ApplyToolResult(closeResult, () => $"Close: {_focus}");
                _focus = LocationId.B;
                return ControllerResult.Handle(_session, "Closed panel");
            }
        }
        _session = _session.ExecuteLeaveBag();
        return ControllerResult.Handle(_session, "Back (click)");
    }

    /// <summary>
    /// Executes a DSL expression string.
    /// </summary>
    public ControllerResult HandleDsl(string dslExpression)
    {
        _session = _session.Execute(dslExpression);
        return ControllerResult.Handle(_session, dslExpression);
    }

    /// <summary>
    /// Replaces the session directly.
    /// </summary>
    public void SetSession(GameSession session)
    {
        _session = session;
    }

    /// <summary>
    /// Sets focus to a specific panel.
    /// </summary>
    public void SetFocus(LocationId panelId)
    {
        _focus = panelId;
    }

    /// <summary>
    /// Advances all facilities by one tick (for realtime mode).
    /// </summary>
    public ControllerResult Tick()
    {
        var before = _session;
        _session = _session.Tick();
        var changed = _session != before;
        return new ControllerResult(_session, changed ? "Tick" : null, changed);
    }
}
