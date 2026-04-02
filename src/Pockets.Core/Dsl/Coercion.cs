using Pockets.Core.Models;

namespace Pockets.Core.Dsl;

/// <summary>
/// Contextual information about a resolved parameter: the resolved value,
/// which location it came from, and the bag/cell index for write-back.
/// </summary>
public record ResolvedParam(
    object Value,
    LocationId LocationId,
    Guid BagId,
    int CellIndex = -1);

/// <summary>
/// Resolves DSL arguments by coercing values down the access level chain:
/// LocationId → Location → Bag → Cell → Stack.
/// Coercion only goes down (more specific). Going up is an error.
/// </summary>
public static class Coercion
{
    /// <summary>
    /// Resolves a LocationId to the expected access level, returning the resolved
    /// value with context for write-back. Uses the game state's store and locations.
    /// </summary>
    public static ResolvedParam Resolve(LocationId locId, AccessLevel expected, GameState state)
    {
        var location = state.Locations.TryGet(locId)
            ?? throw new InvalidOperationException($"Location {locId} is not active");

        return expected switch
        {
            AccessLevel.Location => new ResolvedParam(location, locId, location.BagId),
            AccessLevel.Bag => ResolveToBag(locId, location, state),
            AccessLevel.Cell => ResolveToCell(locId, location, state),
            AccessLevel.Stack => ResolveToStack(locId, location, state),
            AccessLevel.Index => new ResolvedParam(location.Cursor.Position, locId, ResolveActiveBagId(location, state)),
            _ => throw new InvalidOperationException($"Unknown access level: {expected}")
        };
    }

    /// <summary>
    /// Resolves a location to its active bag (following breadcrumbs).
    /// </summary>
    private static ResolvedParam ResolveToBag(LocationId locId, Location location, GameState state)
    {
        var bagId = ResolveActiveBagId(location, state);
        var bag = state.Store.GetById(bagId)
            ?? throw new InvalidOperationException($"Bag {bagId} not found in store");
        return new ResolvedParam(bag, locId, bagId);
    }

    /// <summary>
    /// Resolves a location to the cell at its cursor position.
    /// </summary>
    private static ResolvedParam ResolveToCell(LocationId locId, Location location, GameState state)
    {
        var bagId = ResolveActiveBagId(location, state);
        var bag = state.Store.GetById(bagId)
            ?? throw new InvalidOperationException($"Bag {bagId} not found in store");
        var cell = bag.Grid.GetCell(location.Cursor.Position);
        var cellIndex = location.Cursor.Position.ToIndex(bag.Grid.Columns);
        return new ResolvedParam(cell, locId, bagId, cellIndex);
    }

    /// <summary>
    /// Resolves a location to the item stack at its cursor position.
    /// Returns error context if the cell is empty.
    /// </summary>
    private static ResolvedParam ResolveToStack(LocationId locId, Location location, GameState state)
    {
        var bagId = ResolveActiveBagId(location, state);
        var bag = state.Store.GetById(bagId)
            ?? throw new InvalidOperationException($"Bag {bagId} not found in store");
        var cell = bag.Grid.GetCell(location.Cursor.Position);
        var cellIndex = location.Cursor.Position.ToIndex(bag.Grid.Columns);
        if (cell.IsEmpty)
            throw new InvalidOperationException(
                $"Cell at {location.Cursor.Position} in location {locId} is empty — cannot resolve to Stack");
        return new ResolvedParam(cell.Stack!, locId, bagId, cellIndex);
    }

    /// <summary>
    /// Follows a location's breadcrumbs to find the active (leaf) bag id.
    /// </summary>
    private static Guid ResolveActiveBagId(Location location, GameState state)
    {
        var bagId = location.BagId;
        foreach (var entry in location.Breadcrumbs.Reverse())
        {
            var bag = state.Store.GetById(bagId);
            if (bag is null) break;
            var cell = bag.Grid.GetCell(entry.CellIndex);
            if (cell.Stack?.ContainedBagId is not { } childId) break;
            bagId = childId;
        }
        return bagId;
    }
}
