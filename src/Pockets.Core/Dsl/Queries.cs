using Pockets.Core.Models;

namespace Pockets.Core.Dsl;

/// <summary>
/// Query opcodes — read state and push a value onto the stack without mutating state.
/// Each method takes GameState and returns the value to push.
/// The interpreter wraps this into OpResult.WithPushed automatically.
/// </summary>
public static class Queries
{
    [Query("hand-empty?")]
    public static object HandEmpty(GameState state) =>
        !state.HasItemsInHand;

    [Query("cell-empty?")]
    public static object CellEmpty(GameState state) =>
        state.CurrentCell.IsEmpty;

    [Query("cell-has-bag?")]
    public static object CellHasBag(GameState state) =>
        state.CurrentCell.HasBag;

    [Query("nested?")]
    public static object Nested(GameState state) =>
        state.IsNested;

    [Query("same-type?")]
    public static object SameType(GameState state)
    {
        if (!state.HasItemsInHand || state.CurrentCell.IsEmpty)
            return false;
        return state.HandItems[0].ItemType == state.CurrentCell.Stack!.ItemType;
    }

    [Query("output-slot?")]
    public static object OutputSlot(GameState state) =>
        state.CurrentCell.Frame is OutputSlotFrame;

    [Query("cell-count")]
    public static object CellCount(GameState state) =>
        state.CurrentCell.Stack?.Count ?? 0;
}
