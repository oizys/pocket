using System.Collections.Immutable;
using Pockets.Core.Models;

namespace Pockets.Core.Rendering;

/// <summary>
/// A single invariant breach found in a <see cref="GameState"/>.
/// </summary>
/// <param name="Rule">Short rule identifier (e.g. "stack-validity", "progressability").</param>
/// <param name="Detail">Human-readable description of the specific breach.</param>
public record InvariantViolation(string Rule, string Detail)
{
    public override string ToString() => $"[{Rule}] {Detail}";
}

/// <summary>
/// The invariant pack the journey runner asserts after every step (agent-testing.md §Prong 2):
///   • stack validity  — count in [1, max]; unique items are exactly 1; cell filters honored.
///   • progressability — no corrupt/stuck structure: cursors in-bounds, bag references resolve.
///   • item conservation — checked across steps by comparing <see cref="Census"/> snapshots.
///
/// Pure and Core-only (no UI framework), so it runs identically behind every driver and is
/// itself unit-testable.
/// </summary>
public static class InvariantChecker
{
    /// <summary>
    /// Runs the stack-validity and progressability invariants over a single state.
    /// Returns an empty list when the state is clean. Never throws on a corrupt state —
    /// a corruption that would throw is reported as a violation instead.
    /// </summary>
    public static IReadOnlyList<InvariantViolation> Check(GameState state)
    {
        var violations = new List<InvariantViolation>();
        CheckStackValidity(state, violations);
        CheckProgressability(state, violations);
        return violations;
    }

    // --- Stack validity ---

    private static void CheckStackValidity(GameState state, List<InvariantViolation> v)
    {
        foreach (var bag in state.Store.All)
        {
            var grid = bag.Grid;
            for (int i = 0; i < grid.Cells.Length; i++)
            {
                var cell = grid.Cells[i];
                if (cell.Stack is not { } stack)
                    continue;

                var name = stack.ItemType.Name;
                var loc = $"{bag.EnvironmentType}#{bag.Id.ToString()[..8]} cell {i}";

                if (stack.Count < 1)
                    v.Add(new("stack-validity", $"{name} at {loc} has non-positive count {stack.Count}"));

                if (stack.ItemType.IsStackable)
                {
                    var max = stack.ItemType.EffectiveMaxStackSize;
                    if (stack.Count > max)
                        v.Add(new("stack-validity", $"{name} at {loc} exceeds max stack ({stack.Count} > {max})"));
                }
                else if (stack.Count != 1)
                {
                    v.Add(new("stack-validity", $"unique {name} at {loc} has count {stack.Count} (must be 1)"));
                }

                if (!cell.Accepts(stack.ItemType))
                    v.Add(new("stack-validity",
                        $"{name} ({stack.ItemType.Category}) violates filter at {loc}"));
            }
        }
    }

    // --- Progressability (no corrupt/stuck structure) ---

    private static void CheckProgressability(GameState state, List<InvariantViolation> v)
    {
        // Root + hand bags must resolve.
        if (state.Store.GetById(state.RootBagId) is null)
            v.Add(new("progressability", "root bag id does not resolve in store"));
        if (state.Store.GetById(state.HandBagId) is null)
            v.Add(new("progressability", "hand bag id does not resolve in store"));

        // Every ContainedBagId reference must resolve — no dangling nested bags.
        foreach (var bag in state.Store.All)
        {
            for (int i = 0; i < bag.Grid.Cells.Length; i++)
            {
                if (bag.Grid.Cells[i].Stack?.ContainedBagId is { } childId
                    && state.Store.GetById(childId) is null)
                {
                    v.Add(new("progressability",
                        $"dangling bag reference: {bag.EnvironmentType} cell {i} → {childId.ToString()[..8]}"));
                }
            }
        }

        // Every present location's cursor must be in-bounds of its resolved active bag.
        foreach (var (id, loc) in state.Locations.Entries)
        {
            var activeBag = ResolveActiveBag(state, loc);
            if (activeBag is null)
            {
                v.Add(new("progressability", $"location {id} does not resolve to a bag"));
                continue;
            }

            var pos = loc.Cursor.Position;
            var grid = activeBag.Grid;
            if (pos.Row < 0 || pos.Row >= grid.Rows || pos.Col < 0 || pos.Col >= grid.Columns)
            {
                v.Add(new("progressability",
                    $"location {id} cursor ({pos.Row},{pos.Col}) out of bounds for {grid.Columns}x{grid.Rows} grid"));
            }
        }
    }

    /// <summary>
    /// Resolves the active bag for a location by following its breadcrumb trail
    /// (mirrors GameSession.MoveCursorAt / GameController.ResolveFocusedCell).
    /// Returns null if the trail is broken.
    /// </summary>
    private static Bag? ResolveActiveBag(GameState state, Location loc)
    {
        var bagId = loc.BagId;
        foreach (var entry in loc.Breadcrumbs.Reverse())
        {
            var bag = state.Store.GetById(bagId);
            if (bag is null) return null;
            var cell = bag.Grid.GetCell(entry.CellIndex);
            if (cell.Stack?.ContainedBagId is not { } childId) break;
            bagId = childId;
        }
        return state.Store.GetById(bagId);
    }

    // --- Item conservation ---

    /// <summary>
    /// Total count of every item across every bag in the store (root, hand, nested,
    /// facility slots), keyed by item-type name. A bag-type item counts as 1 of its own
    /// name; its contents are counted separately in the nested bag's own cells. Used to
    /// assert conservation between consecutive steps.
    /// </summary>
    public static ImmutableSortedDictionary<string, int> Census(GameState state)
    {
        var totals = new Dictionary<string, int>();
        foreach (var bag in state.Store.All)
        {
            foreach (var cell in bag.Grid.Cells)
            {
                if (cell.Stack is not { } stack) continue;
                totals.TryGetValue(stack.ItemType.Name, out var cur);
                totals[stack.ItemType.Name] = cur + stack.Count;
            }
        }
        return totals.ToImmutableSortedDictionary();
    }

    /// <summary>
    /// Compares two censuses and returns a stable, human-readable list of deltas
    /// (empty when conserved). Positive delta = items appeared, negative = items vanished.
    /// </summary>
    public static IReadOnlyList<string> CensusDelta(
        ImmutableSortedDictionary<string, int> before,
        ImmutableSortedDictionary<string, int> after)
    {
        var deltas = new List<string>();
        var names = before.Keys.Union(after.Keys).OrderBy(n => n, System.StringComparer.Ordinal);
        foreach (var name in names)
        {
            before.TryGetValue(name, out var b);
            after.TryGetValue(name, out var a);
            if (a != b)
                deltas.Add($"{name}: {b} → {a} ({(a - b >= 0 ? "+" : "")}{a - b})");
        }
        return deltas;
    }
}
