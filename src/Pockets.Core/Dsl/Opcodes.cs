using System.Collections.Immutable;
using Pockets.Core.Models;

namespace Pockets.Core.Dsl;

/// <summary>
/// DSL opcode implementations. Each [Opcode]-decorated static method is discovered
/// by the interpreter via reflection at startup. Opcodes are pure functions:
/// (OpResult, coerced args) → OpResult. State flows through the OpResult chain.
/// </summary>
public static class Opcodes
{
    // ==================== Navigation ====================

    [Opcode("right", DefaultLocation = LocationId.B)]
    public static OpResult Right(OpResult input,
        [Param(AccessLevel.Index)] Position pos)
    {
        var game = input.State;
        var bag = game.ActiveBag;
        var newCursor = game.Cursor.Move(Direction.Right, bag.Grid.Rows, bag.Grid.Columns);
        var bLoc = game.Locations.Get(LocationId.B);
        return input.Chain(game with { Locations = game.Locations.Set(LocationId.B, bLoc with { Cursor = newCursor }) });
    }

    [Opcode("left", DefaultLocation = LocationId.B)]
    public static OpResult Left(OpResult input,
        [Param(AccessLevel.Index)] Position pos)
    {
        var game = input.State;
        var bag = game.ActiveBag;
        var newCursor = game.Cursor.Move(Direction.Left, bag.Grid.Rows, bag.Grid.Columns);
        var bLoc = game.Locations.Get(LocationId.B);
        return input.Chain(game with { Locations = game.Locations.Set(LocationId.B, bLoc with { Cursor = newCursor }) });
    }

    [Opcode("up", DefaultLocation = LocationId.B)]
    public static OpResult Up(OpResult input,
        [Param(AccessLevel.Index)] Position pos)
    {
        var game = input.State;
        var bag = game.ActiveBag;
        var newCursor = game.Cursor.Move(Direction.Up, bag.Grid.Rows, bag.Grid.Columns);
        var bLoc = game.Locations.Get(LocationId.B);
        return input.Chain(game with { Locations = game.Locations.Set(LocationId.B, bLoc with { Cursor = newCursor }) });
    }

    [Opcode("down", DefaultLocation = LocationId.B)]
    public static OpResult Down(OpResult input,
        [Param(AccessLevel.Index)] Position pos)
    {
        var game = input.State;
        var bag = game.ActiveBag;
        var newCursor = game.Cursor.Move(Direction.Down, bag.Grid.Rows, bag.Grid.Columns);
        var bLoc = game.Locations.Get(LocationId.B);
        return input.Chain(game with { Locations = game.Locations.Set(LocationId.B, bLoc with { Cursor = newCursor }) });
    }

    // ==================== Bag navigation ====================

    [Opcode("enter", DefaultLocation = LocationId.B)]
    public static OpResult Enter(OpResult input,
        [Param(AccessLevel.Cell)] Cell cell)
    {
        var result = input.State.EnterBag();
        return result.Success ? input.Chain(result.State) : input.ChainError(result.Error!);
    }

    [Opcode("leave", DefaultLocation = LocationId.B)]
    public static OpResult Leave(OpResult input,
        [Param(AccessLevel.Bag)] Bag bag)
    {
        var result = input.State.LeaveBag();
        return result.Success ? input.Chain(result.State) : input.ChainError(result.Error!);
    }

    // ==================== Inventory operations ====================

    [Opcode("grab", DefaultLocation = LocationId.B)]
    public static OpResult Grab(OpResult input,
        [Param(AccessLevel.Cell, Source = true, DefaultLocation = LocationId.B)] Cell source)
    {
        var result = input.State.ToolGrab();
        return result.Success ? input.Chain(result.State) : input.ChainError(result.Error!);
    }

    [Opcode("drop", DefaultLocation = LocationId.B)]
    public static OpResult Drop(OpResult input,
        [Param(AccessLevel.Cell, Target = true, DefaultLocation = LocationId.B)] Cell target)
    {
        var result = input.State.ToolDrop();
        return result.Success ? input.Chain(result.State) : input.ChainError(result.Error!);
    }

    [Opcode("swap", DefaultLocation = LocationId.B)]
    public static OpResult Swap(OpResult input,
        [Param(AccessLevel.Cell, Source = true, Target = true, DefaultLocation = LocationId.B)] Cell cell)
    {
        var result = input.State.ToolSwap();
        return result.Success ? input.Chain(result.State) : input.ChainError(result.Error!);
    }

    [Opcode("grab-half", DefaultLocation = LocationId.B)]
    public static OpResult GrabHalf(OpResult input,
        [Param(AccessLevel.Cell, Source = true, DefaultLocation = LocationId.B)] Cell source)
    {
        var result = input.State.ToolQuickSplit();
        return result.Success ? input.Chain(result.State) : input.ChainError(result.Error!);
    }

    [Opcode("drop-one", DefaultLocation = LocationId.B)]
    public static OpResult DropOne(OpResult input,
        [Param(AccessLevel.Cell, Target = true, DefaultLocation = LocationId.B)] Cell target)
    {
        var result = input.State.ToolPlaceOne();
        return result.Success ? input.Chain(result.State) : input.ChainError(result.Error!);
    }

    [Opcode("sort", DefaultLocation = LocationId.B)]
    public static OpResult Sort(OpResult input,
        [Param(AccessLevel.Bag, DefaultLocation = LocationId.B)] Bag target)
    {
        var result = input.State.ToolSort();
        return result.Success ? input.Chain(result.State) : input.ChainError(result.Error!);
    }

    [Opcode("split-at", DefaultLocation = LocationId.B)]
    public static OpResult SplitAt(OpResult input,
        [Param(AccessLevel.Cell, Source = true, DefaultLocation = LocationId.B)] Cell source)
    {
        var result = input.State.ToolQuickSplit();
        return result.Success ? input.Chain(result.State) : input.ChainError(result.Error!);
    }

    // ==================== World/harvest operations ====================

    [Opcode("harvest", DefaultLocation = LocationId.B)]
    public static OpResult Harvest(OpResult input,
        [Param(AccessLevel.Cell, Source = true, DefaultLocation = LocationId.B)] Cell target)
    {
        var result = input.State.ToolHarvest();
        return result.Success ? input.Chain(result.State) : input.ChainError(result.Error!);
    }

    // ==================== Context-sensitive ====================

    [Opcode("primary", DefaultLocation = LocationId.B)]
    public static OpResult Primary(OpResult input,
        [Param(AccessLevel.Cell, DefaultLocation = LocationId.B)] Cell cell)
    {
        var result = input.State.ToolPrimary();
        return result.Success ? input.Chain(result.State) : input.ChainError(result.Error!);
    }

    [Opcode("secondary", DefaultLocation = LocationId.B)]
    public static OpResult Secondary(OpResult input,
        [Param(AccessLevel.Cell, DefaultLocation = LocationId.B)] Cell cell)
    {
        var result = input.State.ToolSecondary();
        return result.Success ? input.Chain(result.State) : input.ChainError(result.Error!);
    }

    // ==================== Debug ====================

    [Opcode("acquire-random", DefaultLocation = LocationId.B)]
    public static OpResult AcquireRandom(OpResult input,
        [Param(AccessLevel.Bag, DefaultLocation = LocationId.B)] Bag target)
    {
        var result = input.State.ToolAcquireRandom(new Random());
        return result.Success ? input.Chain(result.State) : input.ChainError(result.Error!);
    }
}
