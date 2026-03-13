namespace Pockets.Core.Models;

/// <summary>
/// Pure input→state pipeline for the game. Maps abstract GameKeys and mouse clicks
/// to GameSession operations. No UI framework dependency — testable with xUnit.
/// Modal split is excluded (it requires UI-specific dialog).
/// </summary>
public class GameController
{
    private GameSession _session;

    public GameController(GameSession session)
    {
        _session = session;
    }

    /// <summary>
    /// The current game session state.
    /// </summary>
    public GameSession Session => _session;

    /// <summary>
    /// Handles a logical key press. Returns a ControllerResult with the updated session,
    /// a status message, and whether the key was handled.
    /// </summary>
    public ControllerResult HandleKey(GameKey key, Random? rng = null)
    {
        // Direction keys
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
            _session = _session.MoveCursor(direction.Value);
            return ControllerResult.Handle(_session, $"Move: {direction.Value}");
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

        // Action keys
        GameSession? newSession = key switch
        {
            GameKey.Primary => _session.ExecutePrimary(),
            GameKey.Secondary => _session.ExecuteSecondary(),
            GameKey.QuickSplit => _session.ExecuteQuickSplit(),
            GameKey.Sort => _session.ExecuteSort(),
            GameKey.AcquireRandom => _session.ExecuteAcquireRandom(rng ?? new Random()),
            GameKey.CycleRecipe => _session.ExecuteCycleRecipe(),
            GameKey.LeaveBag => _session.ExecuteLeaveBag(),
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
        _session = _session.ExecuteLeaveBag();
        return ControllerResult.Handle(_session, "Back (click)");
    }

    /// <summary>
    /// Replaces the session directly. Used for UI-specific operations like modal split
    /// where the dialog result feeds back into the session outside the normal HandleKey flow.
    /// </summary>
    public void SetSession(GameSession session)
    {
        _session = session;
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
