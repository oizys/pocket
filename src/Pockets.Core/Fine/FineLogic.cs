using Pockets.Core.Models;
using Pockets.Core.Sim;

namespace Pockets.Core.Fine;

/// <summary>The pure operations on <see cref="FineState"/> that the session shell runs (P2-D9, P2-D11).</summary>
public static class FineLogic
{
    /// <summary>
    /// Reconciles the fine state after a Step that produced <paramref name="events"/> and <paramref name="next"/>.
    /// Any L1 move of a stack clears its offset (P2-D11): a cell named as a departure or arrival by StackMoved,
    /// StackSplit, StackMerged, StackRemoved or Harvested (cell and yields) loses it, every offset of a
    /// BagRearranged bag is dropped, and so is any offset whose cell is empty or gone in <paramref name="next"/>.
    /// CursorMoved re-anchors that body to its cell centre (keeping velocity and held direction); BagEntered and
    /// BagLeft re-anchor every body of the location. A body that becomes rest is removed. Returns the same
    /// instance when nothing changes. Note for 2.7: a CursorMoved that originates from the integrator must not
    /// re-anchor the body.
    /// </summary>
    public static FineState Reconcile(FineState fine, IEnumerable<SimEvent> events, GameState next)
    {
        if (fine.IsEmpty) return fine;

        var dropped = new HashSet<CellRef>();
        var rearranged = new HashSet<Guid>();
        var reanchorBodies = new HashSet<(LocationId, Guid)>();
        var reanchorLocs = new HashSet<LocationId>();
        foreach (var e in events)
            switch (e)
            {
                case SimEvent.StackMoved m: dropped.Add(m.From); dropped.Add(m.To); break;
                case SimEvent.StackSplit s: dropped.Add(s.From); dropped.Add(s.To); break;
                case SimEvent.StackMerged g: dropped.Add(g.From); dropped.Add(g.To); break;
                case SimEvent.StackRemoved r: dropped.Add(r.Cell); break;
                case SimEvent.Harvested h:
                    dropped.Add(h.Cell);
                    foreach (var y in h.Yields) dropped.Add(y.Cell);
                    break;
                case SimEvent.BagRearranged b: rearranged.Add(b.Bag); break;
                case SimEvent.CursorMoved c: reanchorBodies.Add((c.Loc, c.Bag)); break;
                case SimEvent.BagEntered en: reanchorLocs.Add(en.Loc); break;
                case SimEvent.BagLeft l: reanchorLocs.Add(l.Loc); break;
            }

        var result = fine;
        foreach (var cell in fine.Offsets.Keys.Where(c => dropped.Contains(c) || rearranged.Contains(c.Bag) || !Occupied(next, c)))
            result = result.WithOffset(cell, 0, 0);
        foreach (var key in fine.Bodies.Keys.Where(k => reanchorBodies.Contains(k) || reanchorLocs.Contains(k.Loc)))
            result = result.WithBody(key.Loc, key.Bag, fine.Bodies[key] with { Px = 0, Py = 0 });
        return result;
    }

    /// <summary>True when <paramref name="cell"/> exists in <paramref name="game"/> and holds a stack.</summary>
    private static bool Occupied(GameState game, CellRef cell) =>
        game.Store.GetById(cell.Bag) is { } bag
        && cell.Index >= 0 && cell.Index < bag.Grid.Cells.Length
        && !bag.Grid.Cells[cell.Index].IsEmpty;

    /// <summary>
    /// Sets the offset of cell <paramref name="index"/> of <paramref name="at"/>'s active bag (test-only, P2-D11).
    /// A no-op (the same instance) when the location has no bag, the index is out of range, or the cell is empty.
    /// </summary>
    public static FineState SetOffset(FineState fine, GameState game, LocationId at, int index, int dx, int dy)
    {
        if (game.Locations.TryGet(at) is null) return fine;
        var bag = game.ActiveBagAt(at);
        if (index < 0 || index >= bag.Grid.Cells.Length || bag.Grid.Cells[index].IsEmpty) return fine;
        return fine.WithOffset(new CellRef(bag.Id, index), dx, dy);
    }
}
