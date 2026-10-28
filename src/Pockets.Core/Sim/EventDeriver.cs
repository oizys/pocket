using Pockets.Core.Models;

namespace Pockets.Core.Sim;

/// <summary>
/// Turns what a player tool did into semantic events (Slice 1.5). The tools themselves
/// (<see cref="GameState.ToolPrimary"/> and friends) return only a new state, and rewriting them to
/// emit would risk the golden-neutral rule, so <see cref="Simulation.Step"/> derives each tool's events
/// from the state it was handed and the state it got back, <i>knowing the intent</i> — that is where
/// the cause and direction come from. Rules (facility, plant) and hooks emit their own events directly.
/// <para>
/// Everything here is deterministic across processes: bags are visited in a canonical, GUID-free order
/// (<see cref="CanonicalOrder"/>) and cells by index, so the event order never depends on dictionary order.
/// </para>
/// </summary>
internal static class EventDeriver
{
    /// <summary>The location ids in canonical event order.</summary>
    private static readonly LocationId[] LocationOrder =
        { LocationId.H, LocationId.T, LocationId.B, LocationId.W, LocationId.C };

    // ==================== Locations ====================

    /// <summary>
    /// Emits the panel, enter/leave and cursor events for every location that differs between
    /// <paramref name="pre"/> and <paramref name="post"/>. <paramref name="frame"/> is the location a tool ran
    /// at: the tools are written against B, so in that swapped frame slot B stands for <paramref name="frame"/>
    /// and the stale <paramref name="frame"/> slot is ignored (<see cref="Simulation.SwapIn"/>).
    /// </summary>
    internal static void Locations(GameState pre, GameState post, LocationId frame, int step, Action<Func<int, SimEvent>> emit)
    {
        if (ReferenceEquals(pre.Locations, post.Locations))
            return;

        foreach (var id in LocationOrder)
        {
            if (frame != LocationId.B && id == frame)
                continue;
            var label = id == LocationId.B ? frame : id;
            var a = pre.Locations.TryGet(id);
            var b = post.Locations.TryGet(id);
            if (a == b)
                continue;

            if (b is null)
            {
                var closed = a!.BagId;
                emit(seq => new SimEvent.PanelClosed(step, seq, label, closed));
                continue;
            }

            var bag = Simulation.ResolveBag(post, b)?.Id ?? b.BagId;
            if (a is null || a.BagId != b.BagId)
            {
                emit(seq => new SimEvent.PanelOpened(step, seq, label, b.BagId));
                continue;
            }

            var before = a.Breadcrumbs.Count();
            var after = b.Breadcrumbs.Count();
            if (after > before)
                emit(seq => new SimEvent.BagEntered(step, seq, label, bag, after));
            else if (after < before)
                emit(seq => new SimEvent.BagLeft(step, seq, label, bag, after));
            else if (a.Cursor != b.Cursor)
            {
                var (from, to) = (a.Cursor.Position, b.Cursor.Position);
                emit(seq => new SimEvent.CursorMoved(step, seq, label, bag, from, to));
            }
        }
    }

    // ==================== Stacks ====================

    /// <summary>One cell's change in item content: <paramref name="Count"/> of <paramref name="Item"/> left or arrived.</summary>
    private sealed class Delta(CellRef cell, ItemStack kind, int count, ItemStack? before, ItemStack? after)
    {
        public CellRef Cell { get; } = cell;
        public ItemStack Kind { get; } = kind;
        public int Remaining { get; set; } = count;
        public ItemStack? Before { get; } = before;
        public ItemStack? After { get; } = after;
    }

    /// <summary>One matched transfer: <paramref name="Count"/> items from a loss to a gain.</summary>
    private readonly record struct Transfer(Delta From, Delta To, int Count);

    /// <summary>
    /// Emits the stack events for every cell whose item content differs between <paramref name="pre"/> and
    /// <paramref name="post"/> (bags present in both, keyed by store id). Items that left one cell and arrived
    /// in another are matched by kind (item type and contained bag) into moves, splits and merges; the cause
    /// comes from <paramref name="intent"/> and the direction (into the hand = grab or split, out of it = drop or
    /// slot, both ways = swap). Unmatched arrivals are <see cref="SimEvent.Acquired"/>, unmatched departures
    /// <see cref="SimEvent.StackRemoved"/>. A harvest (a nested item into its parent, or a grown plant's produce)
    /// is one <see cref="SimEvent.Harvested"/>. A sort is one <see cref="SimEvent.BagRearranged"/> for the sorted
    /// bag. A change in properties or frame only (same item, same count) is not a stack event: the explicit
    /// event that caused it names the cell (facility progress, a recipe selection).
    /// <paramref name="frameState"/> is the pre-state in the tool's own frame (B swapped to its location).
    /// </summary>
    internal static void Stacks(GameState pre, GameState post, GameState frameState, Intent intent, int step, Action<Func<int, SimEvent>> emit)
    {
        if (ReferenceEquals(pre.Store, post.Store))
            return;

        var changed = ChangedBags(pre.Store, post.Store);
        if (changed.Count == 0)
            return;

        var sortedBag = intent is Intent.Sort ? frameState.ActiveBagId : (Guid?)null;
        var losses = new List<Delta>();
        var gains = new List<Delta>();
        var touched = new List<CellRef>();

        foreach (var id in CanonicalOrder(post, changed))
        {
            var a = pre.Store.GetById(id)!.Grid;
            var b = post.Store.GetById(id)!.Grid;
            if (id == sortedBag)
            {
                var permutation = Permutation(a, b);
                emit(seq => new SimEvent.BagRearranged(step, seq, id, permutation));
                continue;
            }

            var n = Math.Max(a.Cells.Length, b.Cells.Length);
            for (var i = 0; i < n; i++)
            {
                var s = i < a.Cells.Length ? a.Cells[i].Stack : null;
                var t = i < b.Cells.Length ? b.Cells[i].Stack : null;
                if (s == t)
                    continue;

                var cell = new CellRef(id, i);
                if (s is not null && t is not null && SameKind(s, t))
                {
                    if (t.Count < s.Count) losses.Add(new Delta(cell, s, s.Count - t.Count, s, t));
                    else if (t.Count > s.Count) gains.Add(new Delta(cell, t, t.Count - s.Count, s, t));
                    else touched.Add(cell);
                    continue;
                }
                if (s is not null) losses.Add(new Delta(cell, s, s.Count, s, t));
                if (t is not null) gains.Add(new Delta(cell, t, t.Count, s, t));
            }
        }

        // Match departures to arrivals of the same kind, in canonical order.
        var transfers = new List<Transfer>();
        foreach (var loss in losses)
            foreach (var gain in gains)
            {
                if (loss.Remaining == 0) break;
                if (gain.Remaining == 0 || !SameKind(loss.Kind, gain.Kind)) continue;
                var count = Math.Min(loss.Remaining, gain.Remaining);
                loss.Remaining -= count;
                gain.Remaining -= count;
                transfers.Add(new Transfer(loss, gain, count));
            }

        var hand = frameState.HandBagId;
        var harvestCell = HarvestCell(frameState, intent, transfers, gains, touched, hand);
        if (harvestCell is { } harvested)
        {
            var yields = transfers.Where(t => t.From.Cell == harvested)
                .Select(t => new Placed(t.To.Kind.ItemType, t.Count, t.To.Cell))
                .Concat(gains.Where(g => g.Remaining > 0).Select(g => new Placed(g.Kind.ItemType, g.Remaining, g.Cell)))
                .ToImmutableArray();
            emit(seq => new SimEvent.Harvested(step, seq, harvested, yields));
            transfers.RemoveAll(t => t.From.Cell == harvested);
            gains.ForEach(g => g.Remaining = 0);
        }

        foreach (var t in transfers)
        {
            var cause = Cause(intent, t, transfers, hand, post);
            var (item, from, to, count) = (t.From.Kind.ItemType, t.From.Cell, t.To.Cell, t.Count);
            if (t.To.Before is { } existing && SameKind(existing, t.To.Kind))
                emit(seq => new SimEvent.StackMerged(step, seq, item, count, from, to, cause));
            else if (t.From.After is { } kept && SameKind(kept, t.From.Kind))
            {
                var left = kept.Count;
                emit(seq => new SimEvent.StackSplit(step, seq, item, from, to, left, count, cause));
            }
            else
                emit(seq => new SimEvent.StackMoved(step, seq, item, count, from, to, cause));
        }

        var source = SourceCause(intent);
        foreach (var g in gains.Where(g => g.Remaining > 0))
        {
            var (item, count, cell) = (g.Kind.ItemType, g.Remaining, g.Cell);
            emit(seq => new SimEvent.Acquired(step, seq, item, count, cell, source));
        }
        foreach (var l in losses.Where(l => l.Remaining > 0))
        {
            var (item, count, cell) = (l.Kind.ItemType, l.Remaining, l.Cell);
            emit(seq => new SimEvent.StackRemoved(step, seq, item, count, cell, source));
        }
    }

    /// <summary>Same item kind: the same item type and the same contained bag (a bag item is its own kind).</summary>
    private static bool SameKind(ItemStack a, ItemStack b) =>
        a.ItemType == b.ItemType && a.ContainedBagId == b.ContainedBagId;

    /// <summary>
    /// The harvested cell, when this tool step was a harvest: either a grown plant (its cell changed only in
    /// properties — growth reset — and produce arrived from nowhere), or a nested bag's cursor item that went
    /// to the parent bag rather than the hand. Null otherwise.
    /// </summary>
    private static CellRef? HarvestCell(
        GameState frame, Intent intent, List<Transfer> transfers, List<Delta> gains, List<CellRef> touched, Guid hand)
    {
        if (intent is not (Intent.Harvest or Intent.Interact or Intent.ToolPrimary or Intent.Primary or Intent.Grab))
            return null;

        var cursor = new CellRef(frame.ActiveBagId, frame.Cursor.Position.ToIndex(frame.ActiveBag.Grid.Columns));
        if (touched.Contains(cursor) && gains.Any(g => g.Remaining > 0) && transfers.Count == 0)
            return cursor;
        if (frame.IsNested && transfers.Count > 0
            && transfers.All(t => t.From.Cell == cursor && t.To.Cell.Bag != hand && t.To.Cell.Bag != cursor.Bag))
            return cursor;
        return null;
    }

    /// <summary>
    /// Why a matched transfer happened: the intent decides for DSL, recipe switches and AcquireRandom;
    /// otherwise the direction does — both ways between the same two cells is a swap, into the hand is a grab
    /// (or a split when the source kept some), out of the hand is a drop (or a slotting into a feature slot),
    /// and a Primary that moved between two non-hand bags is the toolbar pickup.
    /// </summary>
    private static StackCause Cause(Intent intent, Transfer t, List<Transfer> all, Guid hand, GameState post)
    {
        switch (intent)
        {
            case Intent.Dsl: return StackCause.Dsl;
            case Intent.CycleRecipe: return StackCause.RecipeSwitch;
            case Intent.AcquireRandom: return StackCause.Acquire;
        }

        if (all.Any(o => o.From.Cell == t.To.Cell && o.To.Cell == t.From.Cell))
            return StackCause.Swap;
        if (t.To.Cell.Bag == hand)
            return t.From.After is { } kept && SameKind(kept, t.From.Kind) ? StackCause.Split : StackCause.Grab;
        if (t.From.Cell.Bag == hand)
            return post.Store.GetById(t.To.Cell.Bag)?.Grid.GetCell(t.To.Cell.Index).Frame is FeatureSlotFrame
                ? StackCause.Slot
                : StackCause.Drop;
        return intent is Intent.Primary ? StackCause.Pickup : StackCause.Grab;
    }

    /// <summary>The source named on an unmatched arrival or departure.</summary>
    private static StackCause SourceCause(Intent intent) => intent switch
    {
        Intent.AcquireRandom => StackCause.Acquire,
        Intent.Dsl => StackCause.Dsl,
        Intent.CycleRecipe => StackCause.RecipeSwitch,
        Intent.Harvest or Intent.Interact => StackCause.Harvest,
        _ => StackCause.Grab
    };

    /// <summary>
    /// A sort's permutation: for each new cell, the old index of the first not-yet-claimed stack of the same
    /// kind (or the first of that kind once all are claimed — stacks merged), -1 for an empty cell.
    /// </summary>
    private static ImmutableArray<int> Permutation(Grid before, Grid after)
    {
        var claimed = new bool[before.Cells.Length];
        var result = ImmutableArray.CreateBuilder<int>(after.Cells.Length);
        foreach (var cell in after.Cells)
        {
            if (cell.Stack is not { } t)
            {
                result.Add(-1);
                continue;
            }
            int first = -1, free = -1;
            for (var j = 0; j < before.Cells.Length; j++)
            {
                if (before.Cells[j].Stack is not { } s || !SameKind(s, t)) continue;
                if (first < 0) first = j;
                if (!claimed[j]) { free = j; break; }
            }
            if (free >= 0) claimed[free] = true;
            result.Add(free >= 0 ? free : first);
        }
        return result.MoveToImmutable();
    }

    // ==================== Dialogue ====================

    /// <summary>
    /// Emits <see cref="SimEvent.DialogueQueued"/> for every beat that fired between <paramref name="pre"/> and
    /// <paramref name="post"/>, in queue order. (Advancing is emitted by the dispatcher, which knows the beat.)
    /// </summary>
    internal static void Dialogue(DialogueState pre, DialogueState post, int step, Action<Func<int, SimEvent>> emit)
    {
        if (pre == post)
            return;
        foreach (var beat in post.Queue.Where(id => !pre.HasFired(id)))
            emit(seq => new SimEvent.DialogueQueued(step, seq, beat));
    }

    // ==================== Canonical bag order ====================

    /// <summary>The ids of bags present in both stores whose grid changed (by reference).</summary>
    private static List<Guid> ChangedBags(BagStore pre, BagStore post)
    {
        var changed = new List<Guid>();
        foreach (var (id, bag) in post.Entries)
            if (pre.GetById(id) is { } old && !ReferenceEquals(old.Grid, bag.Grid))
                changed.Add(id);
        return changed;
    }

    /// <summary>
    /// <paramref name="ids"/> in a canonical, GUID-free order: by the bag's place in the world — its root
    /// (the hand first, then the toolbar, then other roots by environment and size) and then the path of
    /// cell indices from that root down to it.
    /// </summary>
    internal static IEnumerable<Guid> CanonicalOrder(GameState g, IReadOnlyList<Guid> ids)
    {
        if (ids.Count <= 1)
            return ids;

        var owners = g.Store.BuildOwnerIndex();
        var hand = g.HandBagId;
        var toolbar = g.ToolbarBagId;
        var keys = ids.ToDictionary(id => id, id =>
        {
            var path = new List<int>();
            var current = id;
            var guard = 0;
            while (owners.TryGetValue(current, out var owner) && guard++ < 64)
            {
                path.Insert(0, owner.CellIndex);
                current = owner.ParentBagId;
            }
            var root = g.Store.GetById(current);
            var rank = current == hand ? 0 : current == toolbar ? 1 : 2;
            return (rank, env: root?.EnvironmentType ?? "", size: root?.Grid.Cells.Length ?? 0, path);
        });

        return ids.OrderBy(id => keys[id].rank)
            .ThenBy(id => keys[id].env, StringComparer.Ordinal)
            .ThenBy(id => keys[id].size)
            .ThenBy(id => keys[id].path, PathComparer.Instance);
    }

    /// <summary>Lexicographic order on cell-index paths (a prefix sorts first).</summary>
    private sealed class PathComparer : IComparer<List<int>>
    {
        public static readonly PathComparer Instance = new();

        public int Compare(List<int>? x, List<int>? y)
        {
            for (var i = 0; i < Math.Min(x!.Count, y!.Count); i++)
                if (x[i] != y[i]) return x[i].CompareTo(y[i]);
            return x.Count.CompareTo(y.Count);
        }
    }
}
