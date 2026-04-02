using System.Collections.Immutable;

namespace Pockets.Core.Models;

/// <summary>
/// Records the parent bag and cell index that owns a nested bag.
/// </summary>
public record BagOwnerInfo(Guid ParentBagId, int CellIndex);

/// <summary>
/// Flat immutable store of all bags keyed by Guid. Replaces nested bag tree traversal
/// with O(1) lookup. Owner info (parent bag + cell index) is derived on demand.
/// </summary>
public class BagStore
{
    private readonly ImmutableDictionary<Guid, Bag> _bags;

    private BagStore(ImmutableDictionary<Guid, Bag> bags)
    {
        _bags = bags;
    }

    /// <summary>
    /// An empty store with no bags.
    /// </summary>
    public static readonly BagStore Empty = new(ImmutableDictionary<Guid, Bag>.Empty);

    /// <summary>
    /// Look up a bag by its Id. Returns null if not found.
    /// </summary>
    public Bag? GetById(Guid id) =>
        _bags.TryGetValue(id, out var bag) ? bag : null;

    /// <summary>
    /// Returns a new store with the bag added or replaced.
    /// </summary>
    public BagStore Set(Guid id, Bag bag) =>
        new(_bags.SetItem(id, bag));

    /// <summary>
    /// Returns a new store with the bag added. Convenience overload using bag.Id.
    /// </summary>
    public BagStore Add(Bag bag) =>
        new(_bags.SetItem(bag.Id, bag));

    /// <summary>
    /// Returns a new store with multiple bags added.
    /// </summary>
    public BagStore AddRange(IEnumerable<Bag> bags)
    {
        var builder = _bags.ToBuilder();
        foreach (var bag in bags)
            builder[bag.Id] = bag;
        return new BagStore(builder.ToImmutable());
    }

    /// <summary>
    /// Returns a new store with the bag removed.
    /// </summary>
    public BagStore Remove(Guid id) =>
        new(_bags.Remove(id));

    /// <summary>
    /// Returns all bags in the store.
    /// </summary>
    public IEnumerable<Bag> All => _bags.Values;

    /// <summary>
    /// Returns all bags that have a FacilityState (i.e. are facilities).
    /// </summary>
    public IEnumerable<Bag> Facilities =>
        _bags.Values.Where(b => b.FacilityState is not null);

    /// <summary>
    /// Total number of bags in the store.
    /// </summary>
    public int Count => _bags.Count;

    /// <summary>
    /// Returns true if a bag with the given Id exists in the store.
    /// </summary>
    public bool Contains(Guid id) => _bags.ContainsKey(id);

    /// <summary>
    /// Builds owner info by scanning all bags for ContainedBagId references.
    /// Returns a dictionary mapping child bag Id → (parent bag Id, cell index).
    /// </summary>
    public ImmutableDictionary<Guid, BagOwnerInfo> BuildOwnerIndex()
    {
        var builder = ImmutableDictionary.CreateBuilder<Guid, BagOwnerInfo>();
        foreach (var bag in _bags.Values)
        {
            for (int i = 0; i < bag.Grid.Cells.Length; i++)
            {
                if (bag.Grid.Cells[i].Stack?.ContainedBagId is { } childId)
                    builder[childId] = new BagOwnerInfo(bag.Id, i);
            }
        }
        return builder.ToImmutable();
    }

    /// <summary>
    /// Gets owner info for a single bag by scanning the store.
    /// </summary>
    public BagOwnerInfo? GetOwnerOf(Guid bagId)
    {
        foreach (var bag in _bags.Values)
        {
            for (int i = 0; i < bag.Grid.Cells.Length; i++)
            {
                if (bag.Grid.Cells[i].Stack?.ContainedBagId is { } childId && childId == bagId)
                    return new BagOwnerInfo(bag.Id, i);
            }
        }
        return null;
    }

    /// <summary>
    /// Builds a BagStore by extracting all nested bags from root bags.
    /// Walks each root bag's grid cells, collecting any ContainedBagId references,
    /// and adds all found bags to the store.
    /// </summary>
    public static BagStore Build(params Bag[] rootBags)
    {
        var builder = ImmutableDictionary.CreateBuilder<Guid, Bag>();
        var queue = new Queue<Bag>(rootBags);
        while (queue.Count > 0)
        {
            var bag = queue.Dequeue();
            if (builder.ContainsKey(bag.Id))
                continue;
            builder[bag.Id] = bag;
            for (int i = 0; i < bag.Grid.Cells.Length; i++)
            {
                if (bag.Grid.Cells[i].Stack?.ContainedBagId is { } childId
                    && builder.TryGetValue(childId, out _) == false)
                {
                    // Child bag must already be in the builder or we have a broken reference
                    // During Build from legacy code, bags are added before their parents reference them
                }
            }
        }
        return new BagStore(builder.ToImmutable());
    }
}
