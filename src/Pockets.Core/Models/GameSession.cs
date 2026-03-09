using System.Collections.Immutable;

namespace Pockets.Core.Models;

/// <summary>
/// Wraps GameState with undo history and action log. GameState stays as pure domain state;
/// GameSession manages history, dispatches tools, and records actions.
/// MoveCursor is not undoable. Failed tools are not pushed to undo stack but errors are logged.
/// </summary>
public record GameSession(
    GameState Current,
    ImmutableStack<GameState> UndoStack,
    ImmutableList<string> ActionLog,
    int MaxUndoDepth = 1000)
{
    /// <summary>
    /// Creates a new session with empty undo history.
    /// </summary>
    public static GameSession New(GameState initialState, int maxUndoDepth = 1000) =>
        new(initialState, ImmutableStack<GameState>.Empty, ImmutableList<string>.Empty, maxUndoDepth);

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
    /// Moves the cursor. Not undoable, not logged.
    /// </summary>
    public GameSession MoveCursor(Direction direction) =>
        this with { Current = Current.MoveCursor(direction) };

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
        return ApplyResult(result, () => "Leave: returned to parent bag");
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
    /// Execute Sort tool on current state.
    /// </summary>
    public GameSession ExecuteSort()
    {
        var result = Current.ToolSort();
        return ApplyResult(result, () => "Sort: reorganized bag");
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

        var newStack = PushWithLimit(UndoStack, Current);
        return this with
        {
            Current = result.State,
            UndoStack = newStack,
            ActionLog = ActionLog.Add(formatLog())
        };
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
