using System.Collections.Immutable;
using Pockets.Core.Models;

namespace Pockets.Core.Dsl;

/// <summary>
/// DSL opcode implementations. Each [Opcode]-decorated static method is discovered
/// by the interpreter via reflection at startup. Methods receive pre-coerced,
/// strongly-typed arguments and return DslResult.
/// </summary>
public static class Opcodes
{
    // ==================== Navigation ====================

    [Opcode("right", DefaultLocation = LocationId.B)]
    public static DslResult Right(DslState state,
        [Param(AccessLevel.Index)] Position pos)
    {
        var game = state.Game;
        var bag = game.ActiveBag;
        var newCursor = game.Cursor.Move(Direction.Right, bag.Grid.Rows, bag.Grid.Columns);
        var bLoc = game.Locations.Get(LocationId.B);
        var newState = game with { Locations = game.Locations.Set(LocationId.B, bLoc with { Cursor = newCursor }) };
        return DslResult.Ok(newState);
    }

    [Opcode("left", DefaultLocation = LocationId.B)]
    public static DslResult Left(DslState state,
        [Param(AccessLevel.Index)] Position pos)
    {
        var game = state.Game;
        var bag = game.ActiveBag;
        var newCursor = game.Cursor.Move(Direction.Left, bag.Grid.Rows, bag.Grid.Columns);
        var bLoc = game.Locations.Get(LocationId.B);
        var newState = game with { Locations = game.Locations.Set(LocationId.B, bLoc with { Cursor = newCursor }) };
        return DslResult.Ok(newState);
    }

    [Opcode("up", DefaultLocation = LocationId.B)]
    public static DslResult Up(DslState state,
        [Param(AccessLevel.Index)] Position pos)
    {
        var game = state.Game;
        var bag = game.ActiveBag;
        var newCursor = game.Cursor.Move(Direction.Up, bag.Grid.Rows, bag.Grid.Columns);
        var bLoc = game.Locations.Get(LocationId.B);
        var newState = game with { Locations = game.Locations.Set(LocationId.B, bLoc with { Cursor = newCursor }) };
        return DslResult.Ok(newState);
    }

    [Opcode("down", DefaultLocation = LocationId.B)]
    public static DslResult Down(DslState state,
        [Param(AccessLevel.Index)] Position pos)
    {
        var game = state.Game;
        var bag = game.ActiveBag;
        var newCursor = game.Cursor.Move(Direction.Down, bag.Grid.Rows, bag.Grid.Columns);
        var bLoc = game.Locations.Get(LocationId.B);
        var newState = game with { Locations = game.Locations.Set(LocationId.B, bLoc with { Cursor = newCursor }) };
        return DslResult.Ok(newState);
    }

    // ==================== Bag navigation ====================

    [Opcode("enter", DefaultLocation = LocationId.B)]
    public static DslResult Enter(DslState state,
        [Param(AccessLevel.Cell)] Cell cell)
    {
        var game = state.Game;
        var result = game.EnterBag();
        return result.Success ? DslResult.Ok(result.State) : DslResult.Fail(game, result.Error!);
    }

    [Opcode("leave", DefaultLocation = LocationId.B)]
    public static DslResult Leave(DslState state,
        [Param(AccessLevel.Bag)] Bag bag)
    {
        var game = state.Game;
        var result = game.LeaveBag();
        return result.Success ? DslResult.Ok(result.State) : DslResult.Fail(game, result.Error!);
    }

    // ==================== Inventory operations ====================

    [Opcode("grab", DefaultLocation = LocationId.B)]
    public static DslResult Grab(DslState state,
        [Param(AccessLevel.Cell, Source = true, DefaultLocation = LocationId.B)] Cell source)
    {
        var game = state.Game;
        var result = game.ToolGrab();
        return result.Success ? DslResult.Ok(result.State) : DslResult.Fail(game, result.Error!);
    }

    [Opcode("drop", DefaultLocation = LocationId.B)]
    public static DslResult Drop(DslState state,
        [Param(AccessLevel.Cell, Target = true, DefaultLocation = LocationId.B)] Cell target)
    {
        var game = state.Game;
        var result = game.ToolDrop();
        return result.Success ? DslResult.Ok(result.State) : DslResult.Fail(game, result.Error!);
    }

    [Opcode("swap", DefaultLocation = LocationId.B)]
    public static DslResult Swap(DslState state,
        [Param(AccessLevel.Cell, Source = true, Target = true, DefaultLocation = LocationId.B)] Cell cell)
    {
        var game = state.Game;
        var result = game.ToolSwap();
        return result.Success ? DslResult.Ok(result.State) : DslResult.Fail(game, result.Error!);
    }

    [Opcode("grab-half", DefaultLocation = LocationId.B)]
    public static DslResult GrabHalf(DslState state,
        [Param(AccessLevel.Cell, Source = true, DefaultLocation = LocationId.B)] Cell source)
    {
        var game = state.Game;
        var result = game.ToolQuickSplit();
        return result.Success ? DslResult.Ok(result.State) : DslResult.Fail(game, result.Error!);
    }

    [Opcode("drop-one", DefaultLocation = LocationId.B)]
    public static DslResult DropOne(DslState state,
        [Param(AccessLevel.Cell, Target = true, DefaultLocation = LocationId.B)] Cell target)
    {
        var game = state.Game;
        var result = game.ToolPlaceOne();
        return result.Success ? DslResult.Ok(result.State) : DslResult.Fail(game, result.Error!);
    }

    [Opcode("sort", DefaultLocation = LocationId.B)]
    public static DslResult Sort(DslState state,
        [Param(AccessLevel.Bag, DefaultLocation = LocationId.B)] Bag target)
    {
        var game = state.Game;
        var result = game.ToolSort();
        return result.Success ? DslResult.Ok(result.State) : DslResult.Fail(game, result.Error!);
    }

    [Opcode("split-at", DefaultLocation = LocationId.B)]
    public static DslResult SplitAt(DslState state,
        [Param(AccessLevel.Cell, Source = true, DefaultLocation = LocationId.B)] Cell source)
    {
        // "16 split-at" — the int 16 was consumed by ResolveArgs since it's not a LocationId.
        // Actually, ResolveArgs only pops LocationIds. The int stays on the stack.
        // We need to handle this specially. For now, use a fixed default.
        var game = state.Game;
        var result = game.ToolQuickSplit(); // fallback to half-split
        return result.Success ? DslResult.Ok(result.State) : DslResult.Fail(game, result.Error!);
    }

    // ==================== World/harvest operations ====================

    [Opcode("harvest", DefaultLocation = LocationId.B)]
    public static DslResult Harvest(DslState state,
        [Param(AccessLevel.Cell, Source = true, DefaultLocation = LocationId.B)] Cell target)
    {
        var game = state.Game;
        var result = game.ToolHarvest();
        return result.Success ? DslResult.Ok(result.State) : DslResult.Fail(game, result.Error!);
    }

    // ==================== Context-sensitive ====================

    [Opcode("primary", DefaultLocation = LocationId.B)]
    public static DslResult Primary(DslState state,
        [Param(AccessLevel.Cell, DefaultLocation = LocationId.B)] Cell cell)
    {
        var game = state.Game;
        var result = game.ToolPrimary();
        return result.Success ? DslResult.Ok(result.State) : DslResult.Fail(game, result.Error!);
    }

    [Opcode("secondary", DefaultLocation = LocationId.B)]
    public static DslResult Secondary(DslState state,
        [Param(AccessLevel.Cell, DefaultLocation = LocationId.B)] Cell cell)
    {
        var game = state.Game;
        var result = game.ToolSecondary();
        return result.Success ? DslResult.Ok(result.State) : DslResult.Fail(game, result.Error!);
    }

    // ==================== Debug ====================

    [Opcode("acquire-random", DefaultLocation = LocationId.B)]
    public static DslResult AcquireRandom(DslState state,
        [Param(AccessLevel.Bag, DefaultLocation = LocationId.B)] Bag target)
    {
        var game = state.Game;
        var result = game.ToolAcquireRandom(new Random());
        return result.Success ? DslResult.Ok(result.State) : DslResult.Fail(game, result.Error!);
    }
}
