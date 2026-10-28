using Pockets.Core.Models;
using Pockets.Core.Sim;

namespace Pockets.Core.Tests.Sim;

/// <summary>
/// The Phase 1 completeness contract's coverage check (P1-D1, Slice 1.5): every cell (hand and toolbar slots
/// included — they are cells of the hand and toolbar bags) and every location cursor that differs between a
/// step's pre- and post-state must be <i>named</i> by at least one of the step's events. Bags created or removed
/// in the step are out of scope: the event that placed or took the stack holding them names that cell.
/// <para>
/// What each event names is spelled out here, case by case, rather than found by reflection, so the check
/// also pins what each event means.
/// </para>
/// </summary>
internal static class EventCoverage
{
    /// <summary>The changes in <paramref name="pre"/> → <paramref name="post"/> no event names (empty = covered).</summary>
    public static IReadOnlyList<string> Uncovered(SimState pre, SimState post, IReadOnlyList<SimEvent> events)
    {
        var cells = new HashSet<CellRef>();
        var bags = new HashSet<Guid>();
        var locations = new HashSet<LocationId>();
        foreach (var e in events)
            Name(e, cells, bags, locations);

        var missing = new List<string>();
        foreach (var (id, after) in post.Game.Store.Entries)
        {
            if (pre.Game.Store.GetById(id) is not { } before || ReferenceEquals(before.Grid, after.Grid))
                continue;
            if (bags.Contains(id))
                continue;
            var n = Math.Max(before.Grid.Cells.Length, after.Grid.Cells.Length);
            for (var i = 0; i < n; i++)
            {
                var a = i < before.Grid.Cells.Length ? before.Grid.Cells[i] : null;
                var b = i < after.Grid.Cells.Length ? after.Grid.Cells[i] : null;
                if (a != b && !cells.Contains(new CellRef(id, i)))
                    missing.Add($"cell {after.EnvironmentType}[{i}]: {Describe(a)} → {Describe(b)}");
            }
        }

        foreach (var id in Enum.GetValues<LocationId>())
            if (pre.Game.Locations.TryGet(id) != post.Game.Locations.TryGet(id) && !locations.Contains(id))
                missing.Add($"location {id}");

        return missing;
    }

    /// <summary>Records what one event names: cells, whole bags, locations.</summary>
    private static void Name(SimEvent e, HashSet<CellRef> cells, HashSet<Guid> bags, HashSet<LocationId> locations)
    {
        switch (e)
        {
            case SimEvent.CursorMoved m: locations.Add(m.Loc); break;
            case SimEvent.BagEntered m: locations.Add(m.Loc); break;
            case SimEvent.BagLeft m: locations.Add(m.Loc); break;
            case SimEvent.PanelOpened m: locations.Add(m.Loc); break;
            case SimEvent.PanelClosed m: locations.Add(m.Loc); break;

            case SimEvent.StackMoved m: cells.Add(m.From); cells.Add(m.To); break;
            case SimEvent.StackSplit m: cells.Add(m.From); cells.Add(m.To); break;
            case SimEvent.StackMerged m: cells.Add(m.From); cells.Add(m.To); break;
            case SimEvent.StackRemoved m: cells.Add(m.Cell); break;
            case SimEvent.Acquired m: cells.Add(m.Cell); break;
            case SimEvent.BagRearranged m: bags.Add(m.Bag); break;

            case SimEvent.CraftStarted m: AddOwner(m.Owner); break;
            case SimEvent.CraftProgressed m: AddOwner(m.Owner); break;
            case SimEvent.CraftAborted m: AddOwner(m.Owner); break;
            case SimEvent.CraftCompleted m:
                AddOwner(m.Owner);
                cells.UnionWith(m.Consumed);
                cells.UnionWith(m.Outputs.Select(o => o.Cell));
                break;
            case SimEvent.RecipeSelected m:
                AddOwner(m.Owner);
                if (m.SlotsRebuilt) bags.Add(m.Facility);
                break;

            case SimEvent.PlantGrew m: cells.Add(m.Cell); break;
            case SimEvent.Harvested m: cells.Add(m.Cell); cells.UnionWith(m.Yields.Select(y => y.Cell)); break;
            case SimEvent.RecipeLearned m: cells.Add(m.Card); break;
        }

        void AddOwner(CellRef? owner)
        {
            if (owner is { } c) cells.Add(c);
        }
    }

    /// <summary>A short text of a cell's stack for failure messages.</summary>
    private static string Describe(Cell? c) =>
        c?.Stack is { } s ? $"{s.Count}×{s.ItemType.Name}" : c is null ? "(none)" : "empty";
}
