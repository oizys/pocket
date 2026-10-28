using Pockets.Core.Sim;

namespace Pockets.Core.Models;

/// <summary>
/// Pure input→state pipeline for the game. Maps abstract GameKeys, mouse clicks, DSL and the clock to
/// <see cref="Intent"/>s and hands them to <see cref="GameSession.Apply"/> (Slice 1.3) — it never changes
/// the session any other way. No UI framework dependency — testable with xUnit. Tracks which panel has
/// focus for input routing; focus is frontend-only state, so every intent names its target location.
/// </summary>
public class GameController
{
    private GameSession _session;
    private LocationId _focus = LocationId.B;

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

    /// <summary>
    /// One facility craft tick per this many milliseconds of game time (Slice 7). Crafting progress is
    /// clock-driven: the compass's Duration is measured in these whole-second ticks. The parity harness
    /// only advances game time via scripted <c>advanceTime</c>, so a craft can never drift forward on a
    /// plain key press — the "realtime, deterministically" contract from the Slice-5 clock.
    /// </summary>
    public const int TickLengthMs = 1000;

    /// <summary>
    /// Copies the injected clock's reading onto the session (the clock-readout value) AND advances
    /// facility crafting by one tick per whole-second boundary crossed since the last sync. Both the
    /// scripted path (<see cref="AdvanceClock"/>) and a live wall-clock frontend (which calls this each
    /// refresh) drive timed crafts identically; the harness stays deterministic because it only ever
    /// moves the clock through scripted <c>advanceTime</c>.
    /// </summary>
    public ControllerResult SyncClock() =>
        Collect(SyncClockCore);

    private ControllerResult SyncClockCore()
    {
        var before = _session.Elapsed;
        var now = _clock.Elapsed;
        Send(new Intent.SetElapsed(now));

        var ticks = (long)(now.TotalMilliseconds / TickLengthMs) - (long)(before.TotalMilliseconds / TickLengthMs);
        for (long t = 0; t < ticks; t++)
            Send(new Intent.Tick());

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
    /// Handles a logical key press: maps it (with the modal state and the panel focus) to one
    /// <see cref="Intent"/> naming its target location, hands it to the session, and updates the focus
    /// and the status message from how the step resolved. Returns a ControllerResult with the updated
    /// session, a status message, whether the key was handled, and the step's events. Frontends own no randomness:
    /// AcquireRandom draws inside the step from the session's SimRng (Slice 1.1).
    /// </summary>
    public ControllerResult HandleKey(GameKey key) =>
        Collect(() => HandleKeyCore(key));

    private ControllerResult HandleKeyCore(GameKey key)
    {
        // Dialogue is modal-lite: while a beat is showing, Primary advances/dismisses it and every
        // other key is swallowed (handled, no state change) — the input's first job is turning pages,
        // before movement exists, and nothing leaks to the cursor underneath.
        if (_session.Current.Dialogue.IsActive)
        {
            if (key == GameKey.Primary)
            {
                Send(new Intent.AdvanceDialogue());
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
                    Send(new Intent.AdjustSplit(-1));
                    return ControllerResult.Handle(_session, $"Split: grab {_session.SplitMode?.GrabCount}");
                case GameKey.Right:
                    Send(new Intent.AdjustSplit(+1));
                    return ControllerResult.Handle(_session, $"Split: grab {_session.SplitMode?.GrabCount}");
                case GameKey.Confirm:
                    Send(new Intent.CommitSplit());
                    return ControllerResult.Handle(_session, "Split: commit");
                case GameKey.Cancel:
                    Send(new Intent.CancelSplit());
                    return ControllerResult.Handle(_session, "Split: cancel");
                default:
                    return ControllerResult.Handle(_session, "Split: in progress (←/→ adjust, Enter, Esc)");
            }
        }

        // Recipe menu is modal-lite (like split mode): while it is open it owns its keys — ↑/↓ move the
        // selection, Enter/Primary select (set the facility's recipe), Esc/Q close. Every other key is
        // swallowed. This is a real modal (Aaron reversed the no-modal rule; see the drift report).
        if (_session.RecipeMenu is not null)
        {
            switch (key)
            {
                case GameKey.Up:
                    Send(new Intent.MoveRecipeMenu(-1));
                    return ControllerResult.Handle(_session, $"Recipe menu: {_session.RecipeMenu?.SelectedIndex}");
                case GameKey.Down:
                    Send(new Intent.MoveRecipeMenu(+1));
                    return ControllerResult.Handle(_session, $"Recipe menu: {_session.RecipeMenu?.SelectedIndex}");
                case GameKey.Confirm:
                case GameKey.Primary:
                    Send(new Intent.ConfirmRecipeMenu());
                    return ControllerResult.Handle(_session, "Recipe menu: selected");
                case GameKey.Cancel:
                case GameKey.LeaveBag:
                    Send(new Intent.CloseRecipeMenu());
                    return ControllerResult.Handle(_session, "Recipe menu: closed");
                default:
                    return ControllerResult.Handle(_session, "Recipe menu: ↑/↓ select, Enter set, Esc close");
            }
        }

        // Begin inline split for the focused cursor cell.
        if (key == GameKey.BeginSplit)
        {
            Send(new Intent.BeginSplit(_focus));
            if (_session.SplitMode is not null)
                return ControllerResult.Handle(_session,
                    $"Split: grab {_session.SplitMode!.GrabCount} / {_session.SplitMode.StackTotal}");
            return ControllerResult.Handle(_session, "Split: nothing to split");
        }

        // Focus cycling (frontend-only: no intent)
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
            // A move refused by an unnavigable tree emits Bumped(Unnavigable) on every bump; the frontends
            // play their shake/flash from it (the fire-once axe-absence beat is handled inside the step).
            Send(new Intent.Move(_focus, direction.Value));
            return ControllerResult.Handle(_session, $"Move: {direction.Value} ({_focus})");
        }

        // Undo
        if (key == GameKey.Undo)
        {
            if (!_session.CanUndo)
                return ControllerResult.Handle(_session, "Nothing to undo");
            Send(new Intent.Undo());
            return ControllerResult.Handle(_session, "Undo");
        }

        // Look-in peek (C) — a generic look-in overlay, one deep for the demo.
        if (key == GameKey.Peek)
            return ExecutePeek();

        // Leave/close — if focused on C or W, close that panel. Otherwise leave bag.
        if (key == GameKey.LeaveBag)
        {
            Send(new Intent.Leave(_focus));
            if (FollowFocus())
                return ControllerResult.Handle(_session, $"Closed panel, focus → B");
            return ControllerResult.Handle(_session, "Action: LeaveBag");
        }

        // Action keys — most operate on the focused panel; AcquireRandom is a debug
        // tool tied to the root bag and stays B-only.
        Intent? intent = key switch
        {
            GameKey.Primary => new Intent.Primary(_focus),
            GameKey.Secondary => new Intent.Secondary(_focus),
            GameKey.QuickSplit => new Intent.QuickSplit(_focus),
            GameKey.Sort => new Intent.Sort(_focus),
            GameKey.AcquireRandom => new Intent.AcquireRandom(LocationId.B),
            GameKey.RecipeMenu => new Intent.OpenRecipeMenu(_focus),
            _ => null
        };

        if (intent is not null)
        {
            Send(intent);
            FollowFocus();
            return ControllerResult.Handle(_session, $"Action: {key}");
        }

        return ControllerResult.Unhandled(_session);
    }

    /// <summary>The events of the last intent <see cref="Send"/> handed to the session (Slice 1.5).</summary>
    private ImmutableArray<SimEvent> _lastEvents = ImmutableArray<SimEvent>.Empty;

    /// <summary>Every event of the public call in progress, in order (Slice 1.6): <see cref="ControllerResult.Events"/>.</summary>
    private readonly ImmutableArray<SimEvent>.Builder _callEvents = ImmutableArray.CreateBuilder<SimEvent>();

    /// <summary>
    /// Runs one public input call and puts every event its intents emitted on the result (Slice 1.6). A call
    /// can send several intents (a clock sync is SetElapsed plus one Tick per second crossed), so the events
    /// are collected across all of them; the frontends play their one-shot cues from these.
    /// </summary>
    private ControllerResult Collect(Func<ControllerResult> run)
    {
        _callEvents.Clear();
        var result = run();
        return result with { Events = _callEvents.ToImmutable() };
    }

    /// <summary>
    /// Hands one intent to the session (the only way the controller changes it) and returns how the
    /// step resolved, for the status message. Its events are kept in <see cref="_lastEvents"/> (focus is read
    /// from them) and added to the call's <see cref="ControllerResult.Events"/>.
    /// </summary>
    private StepTrace? Send(Intent intent)
    {
        var (session, trace, events) = _session.ApplyTraced(intent);
        _session = session;
        _lastEvents = events;
        _callEvents.AddRange(events);
        return trace;
    }

    /// <summary>
    /// Moves focus by the last intent's panel events, the way the controller always did: a C or W panel
    /// that opened takes focus (a facility or a peeked bag → C, a wilderness → W); a panel that closed
    /// returns focus to B; anything else leaves focus where it is. Returns true when a panel closed.
    /// </summary>
    private bool FollowFocus()
    {
        foreach (var e in _lastEvents)
        {
            switch (e)
            {
                case SimEvent.PanelOpened { Loc: LocationId.C or LocationId.W } opened:
                    _focus = opened.Loc;
                    return false;
                case SimEvent.PanelClosed:
                    _focus = LocationId.B;
                    return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Look-in peek (C): sends <see cref="Intent.Peek"/> at the focused panel and reports the outcome.
    /// Distinct from Primary/E, which ENTERS a plain bag via breadcrumbs — peek lets the player look into
    /// and arrange a bag without going anywhere (journey 7:00, the Chest). Closing a panel returns focus to
    /// B; opening the overlay focuses C (FirstPeek → LookInOverlay fires structurally in the step's hooks).
    /// An <see cref="Bag.EnterOnly"/> bag REFUSES the peek: nothing opens, the first-failed-peek beat fires
    /// once, and a <see cref="SimEvent.Bumped"/> (<see cref="BumpReason.FailedPeek"/>) is emitted for the shake/flash — the refused peek is the
    /// only tell (no glyph — RATIFIED). E still enters the bag normally.
    /// </summary>
    private ControllerResult ExecutePeek()
    {
        var at = _focus;
        var trace = Send(new Intent.Peek(at));
        FollowFocus();
        switch (trace?.Route)
        {
            case Route.PeekClosePanel:
                return ControllerResult.Handle(_session, $"Peek: closed {at}");
            case Route.PeekNothingToClose:
                return ControllerResult.Handle(_session, "Peek: nothing to close");
            case Route.PeekNoBag:
                return ControllerResult.Handle(_session, "Peek: nothing to look into");
            case Route.PeekBagNotFound:
                return ControllerResult.Handle(_session, "Peek: bag not found");
            case Route.PeekRefused:
                return ControllerResult.Handle(_session, "Can't just peek at this one — press E to enter.");
            case Route.PeekCloseBag:
                return ControllerResult.Handle(_session, $"Peek: closed {trace.Detail}");
            case Route.PeekOpen:
                return ControllerResult.Handle(_session, $"Peek: {trace.Detail}");
            default:
                return ControllerResult.Handle(_session, "Peek: could not open");
        }
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
    /// Handles a mouse click on a grid cell in a specific panel: focus switches to the clicked panel (when
    /// it is open), then one <see cref="Intent.Click"/> places that panel's cursor and runs Primary or
    /// Secondary there.
    /// </summary>
    public ControllerResult HandleGridClick(LocationId panelId, Position pos, ClickType type) =>
        Collect(() => HandleGridClickCore(panelId, pos, type));

    private ControllerResult HandleGridClickCore(LocationId panelId, Position pos, ClickType type)
    {
        // Auto-switch focus to the clicked panel
        if (_session.Current.Locations.Has(panelId))
            _focus = panelId;

        Send(new Intent.Click(_focus, pos, type));
        if (type == ClickType.Primary)
            FollowFocus();

        var clickName = type == ClickType.Primary ? "LClick" : "RClick";
        return ControllerResult.Handle(_session, $"{clickName} {_focus}: ({pos.Row},{pos.Col})");
    }

    /// <summary>
    /// Backward-compatible overload for B panel clicks.
    /// </summary>
    public ControllerResult HandleGridClick(Position pos, ClickType type) =>
        HandleGridClick(LocationId.B, pos, type);

    /// <summary>
    /// Handles a click on the back/leave-bag button (<see cref="Intent.Leave"/> at the focused panel).
    /// </summary>
    public ControllerResult HandleBackClick() =>
        Collect(HandleBackClickCore);

    private ControllerResult HandleBackClickCore()
    {
        Send(new Intent.Leave(_focus));
        if (FollowFocus())
            return ControllerResult.Handle(_session, "Closed panel");
        return ControllerResult.Handle(_session, "Back (click)");
    }

    /// <summary>
    /// Executes a DSL expression string (<see cref="Intent.Dsl"/>).
    /// </summary>
    public ControllerResult HandleDsl(string dslExpression) =>
        Collect(() => HandleDslCore(dslExpression));

    /// <summary>
    /// Test-only (P2-D11): sets the L2a offset of cell <paramref name="index"/> of <paramref name="at"/>'s active bag
    /// (<see cref="Intent.SetFineOffset"/>). No Step runs; the shell changes the session's fine state.
    /// </summary>
    public ControllerResult SetFineOffset(LocationId at, int index, int dx, int dy) =>
        Collect(() =>
        {
            Send(new Intent.SetFineOffset(at, index, dx, dy));
            return ControllerResult.Handle(_session, "Fine offset");
        });

    private ControllerResult HandleDslCore(string dslExpression)
    {
        Send(new Intent.Dsl(dslExpression));
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
    public ControllerResult Tick() =>
        Collect(TickCore);

    private ControllerResult TickCore()
    {
        var before = _session;
        Send(new Intent.Tick());
        var changed = _session != before;
        return new ControllerResult(_session, changed ? "Tick" : null, changed);
    }
}
