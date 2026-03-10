using System.Collections.Immutable;

namespace Pockets.Core.Models;

/// <summary>
/// Immutable index of all bags reachable from a root bag, keyed by Bag.Id.
/// Built via BFS traversal. Provides O(1) lookup by Guid and enumeration
/// of all bags for tick dispatch, dirty tracking, etc.
/// </summary>
public class BagRegistry
{
    private readonly ImmutableDictionary<Guid, Bag> _bags;

    private BagRegistry(ImmutableDictionary<Guid, Bag> bags)
    {
        _bags = bags;
    }

    /// <summary>
    /// Builds a registry by BFS-traversing all bags reachable from the root.
    /// Includes the root bag itself and the hand bag.
    /// </summary>
    public static BagRegistry Build(Bag rootBag, Bag handBag)
    {
        var builder = ImmutableDictionary.CreateBuilder<Guid, Bag>();
        var queue = new Queue<Bag>();

        queue.Enqueue(rootBag);
        queue.Enqueue(handBag);

        while (queue.Count > 0)
        {
            var bag = queue.Dequeue();
            if (builder.ContainsKey(bag.Id))
                continue;

            builder[bag.Id] = bag;

            foreach (var cell in bag.Grid.Cells)
            {
                if (cell.Stack?.ContainedBag is { } inner)
                    queue.Enqueue(inner);
            }
        }

        return new BagRegistry(builder.ToImmutable());
    }

    /// <summary>
    /// Look up a bag by its Id. Returns null if not found.
    /// </summary>
    public Bag? GetById(Guid id) =>
        _bags.TryGetValue(id, out var bag) ? bag : null;

    /// <summary>
    /// Returns all bags in the registry.
    /// </summary>
    public IEnumerable<Bag> All => _bags.Values;

    /// <summary>
    /// Returns all bags that have a FacilityState (i.e. are facilities).
    /// </summary>
    public IEnumerable<Bag> Facilities =>
        _bags.Values.Where(b => b.FacilityState is not null);

    /// <summary>
    /// Total number of bags in the registry.
    /// </summary>
    public int Count => _bags.Count;

    /// <summary>
    /// Returns true if a bag with the given Id exists in the registry.
    /// </summary>
    public bool Contains(Guid id) => _bags.ContainsKey(id);
}
