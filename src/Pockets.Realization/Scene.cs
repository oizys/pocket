using Pockets.Core.Models;
using Pockets.Core.Sim;

namespace Pockets.Realization;

/// <summary>
/// The realized picture of a state: one <see cref="Pose"/> per drawn entity. Rest poses put everything at its
/// cell centre plus the L2a offsets of an <see cref="IFineView"/>; with no fine offsets the scene at n = 1 is
/// equivalent to the view model. Body facing defaults to S and later comes from the timeline (slice 2.2)
/// through the <c>facings</c> argument of <see cref="FromState"/>.
/// </summary>
/// <param name="Poses">The poses, bodies before the stacks of the same location, in <c>shown</c> order.</param>
public sealed record Scene(ImmutableArray<Pose> Poses)
{
    /// <summary>
    /// The rest scene of <paramref name="state"/> for the <paramref name="shown"/> locations, in that order.
    /// Absent locations are skipped; a bag shown by several locations emits its stacks once (first occurrence)
    /// while each location still gets its own body when its bag is a world bag.
    /// </summary>
    public static Scene FromState(GameState state, IReadOnlyList<LocationId> shown, IFineView? fine = null,
        IReadOnlyDictionary<LocationId, Facing>? facings = null)
    {
        var view = fine ?? FineView.Empty;
        var seen = new HashSet<Guid>();
        var poses = shown
            .Where(loc => state.Locations.TryGet(loc) is not null)
            .SelectMany(loc =>
            {
                var bag = state.ActiveBagAt(loc);
                var cursor = state.Locations.Get(loc).Cursor.Position;
                var (px, py) = view.BodyOffset(loc, bag.Id);
                var body = IsWorldBag(bag)
                    ? new[]
                    {
                        new Pose(new EntityKey.Body(loc), bag.Id, cursor.Col, cursor.Row, px, py, null,
                            facings is not null && facings.TryGetValue(loc, out var f) ? f : Facing.S)
                    }
                    : Array.Empty<Pose>();
                var stacks = seen.Add(bag.Id)
                    ? bag.Grid.Cells
                        .Select((cell, i) => (cell, i))
                        .Where(t => !t.cell.IsEmpty)
                        .Select(t =>
                        {
                            var at = new CellRef(bag.Id, t.i);
                            var (dx, dy) = view.StackOffset(at);
                            var pos = Position.FromIndex(t.i, bag.Grid.Columns);
                            return new Pose(new EntityKey.Stack(at), bag.Id, pos.Col, pos.Row, dx, dy, null, null);
                        })
                    : Enumerable.Empty<Pose>();
                return body.Concat(stacks);
            })
            .ToImmutableArray();
        var columns = shown
            .Where(loc => state.Locations.TryGet(loc) is not null)
            .Select(loc => state.ActiveBagAt(loc))
            .GroupBy(bag => bag.Id)
            .ToImmutableDictionary(g => g.Key, g => g.First().Grid.Columns);
        return new Scene(poses) { Columns = columns };
    }

    /// <summary>The grid column count of every shown bag, keyed by bag id.</summary>
    public ImmutableDictionary<Guid, int> Columns { get; init; } = ImmutableDictionary<Guid, int>.Empty;

    /// <summary>
    /// True when the bag is a world bag, which carries a body (the plan defines the body by world bag). The
    /// demo's Quiet 1 wilderness has environment "Quiet 1" (not a wilderness type name) but is enter-only, and
    /// the demo walks it as B, so enter-only counts as a world bag too.
    /// </summary>
    public static bool IsWorldBag(Bag bag) => GameState.IsWildernessType(bag.EnvironmentType) || bag.EnterOnly;

    /// <summary>The pose of <paramref name="key"/>, or null when the scene has none.</summary>
    public Pose? Find(EntityKey key) => Poses.Cast<Pose?>().FirstOrDefault(p => p!.Value.Entity == key);
}
