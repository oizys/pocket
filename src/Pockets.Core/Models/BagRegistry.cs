using System.Collections.Immutable;

namespace Pockets.Core.Models;

/// <summary>
/// Records the parent bag and cell index that owns a nested bag.
/// </summary>
public record BagOwnerInfo(Guid ParentBagId, int CellIndex);

/// <summary>
/// Immutable index of all bags reachable from a root bag, keyed by Bag.Id.
/// Built via BFS traversal. Provides O(1) lookup by Guid and enumeration
/// of all bags for tick dispatch, dirty tracking, etc.
/// Also tracks owner info for each bag (which parent bag + cell index contains it).
/// </summary>
public class BagRegistry
{
    private readonly ImmutableDictionary<Guid, Bag> _bags;
    private readonly ImmutableDictionary<Guid, BagOwnerInfo> _owners;

    private BagRegistry(ImmutableDictionary<Guid, Bag> bags, ImmutableDictionary<Guid, BagOwnerInfo> owners)
    {
        _bags = bags;
        _owners = owners;
    }

    /// <summary>
    /// Builds a registry by BFS-traversing all bags reachable from the root.
    /// Includes the root bag itself and the hand bag.
    /// Tracks owner info (parent bag Id + cell index) for each nested bag.
    /// </summary>
    public static BagRegistry Build(Bag rootBag, Bag handBag)
    {
        var builder = ImmutableDictionary.CreateBuilder<Guid, Bag>();
        var ownerBuilder = ImmutableDictionary.CreateBuilder<Guid, BagOwnerInfo>();
        var queue = new Queue<Bag>();

        queue.Enqueue(rootBag);
        queue.Enqueue(handBag);

        while (queue.Count > 0)
        {
            var bag = queue.Dequeue();
            if (builder.ContainsKey(bag.Id))
                continue;

            builder[bag.Id] = bag;

            for (int i = 0; i < bag.Grid.Cells.Length; i++)
            {
                if (bag.Grid.Cells[i].Stack?.ContainedBag is { } inner)
                {
                    ownerBuilder[inner.Id] = new BagOwnerInfo(bag.Id, i);
                    queue.Enqueue(inner);
                }
            }
        }

        return new BagRegistry(builder.ToImmutable(), ownerBuilder.ToImmutable());
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

    /// <summary>
    /// Returns the owner info (parent bag Id + cell index) for a bag, or null if it's a root/hand bag.
    /// </summary>
    public BagOwnerInfo? GetOwnerOf(Guid bagId) =>
        _owners.TryGetValue(bagId, out var info) ? info : null;
}
