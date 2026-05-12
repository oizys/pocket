namespace Pockets.Core.Models;

/// <summary>
/// Pure input→state pipeline for the game. Maps abstract GameKeys and mouse clicks
/// to GameSession operations. No UI framework dependency — testable with xUnit.
/// Tracks which panel has focus for input routing.
/// </summary>
public class GameController
{
    private GameSession _session;
    private LocationId _focus = LocationId.B;

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

    /// <summary>
    /// Handles a logical key press. Returns a ControllerResult with the updated session,
    /// a status message, and whether the key was handled.
    /// </summary>
    public ControllerResult HandleKey(GameKey key, Random? rng = null)
    {
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
            _session = _session.MoveCursorAt(_focus, direction.Value);
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

        return _session.ExecutePrimary(_focus);
    }

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
