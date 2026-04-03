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

        // Action keys
        GameSession? newSession = key switch
        {
            GameKey.Primary => ExecuteFocusedPrimary(),
            GameKey.Secondary => _session.ExecuteSecondary(),
            GameKey.QuickSplit => _session.ExecuteQuickSplit(),
            GameKey.Sort => _session.ExecuteSort(),
            GameKey.AcquireRandom => _session.ExecuteAcquireRandom(rng ?? new Random()),
            GameKey.CycleRecipe => _session.ExecuteCycleRecipe(),
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
    /// Executes Primary action with panel-aware bag opening.
    /// If the cursor cell in the focused panel contains a facility bag → open as C.
    /// If it contains a wilderness bag → open as W.
    /// Otherwise, delegate to normal ToolPrimary.
    /// </summary>
    private GameSession ExecuteFocusedPrimary()
    {
        var state = _session.Current;
        var cell = state.CurrentCell;

        // Only attempt panel-open when focused on B and the cell has a bag
        if (_focus == LocationId.B && cell.HasBag && !state.HasItemsInHand)
        {
            var bagId = cell.Stack!.ContainedBagId!.Value;
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

        return _session.ExecutePrimary();
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
    /// Handles a mouse click on a grid cell. Moves cursor to the position,
    /// then executes the appropriate action based on click type.
    /// </summary>
    public ControllerResult HandleGridClick(Position pos, ClickType type)
    {
        _session = _session.MoveCursor(_session.Current, pos);

        var newSession = type switch
        {
            ClickType.Primary => _session.ExecutePrimary(),
            ClickType.Secondary => _session.ExecuteSecondary(),
            _ => _session
        };

        _session = newSession;

        var clickName = type == ClickType.Primary ? "LClick" : "RClick";
        return ControllerResult.Handle(_session, $"{clickName}: ({pos.Row},{pos.Col})");
    }

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
