namespace Pockets.Core.Models;

/// <summary>
/// An openable item containing a Grid of cells. Bags nest inside other bags.
/// Each bag has a stable Id that persists through mutations (via `with` expressions).
/// </summary>
public record Bag(
    Grid Grid,
    string EnvironmentType = "Default",
    string ColorScheme = "Default")
{
    /// <summary>
    /// Stable identity for this bag. Auto-generated if not set.
    /// Preserved through `with` expressions since it's a record property.
    /// </summary>
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>
    /// Places item stacks into this bag's grid. Returns a new Bag with the updated grid
    /// and any stacks that couldn't be placed. Optional skipIndices excludes specific cells.
    /// </summary>
    public (Bag UpdatedBag, IReadOnlyList<ItemStack> Unplaced) AcquireItems(
        IEnumerable<ItemStack> stacks, ImmutableHashSet<int>? skipIndices = null)
    {
        var (updatedGrid, unplaced) = Grid.AcquireItems(stacks, skipIndices);
        return (this with { Grid = updatedGrid }, unplaced);
    }

    /// <summary>
    /// Breadth-first search for a bag with the given Id, starting from this bag.
    /// Searches all ContainedBags in the grid. Returns null if not found.
    /// </summary>
    public Bag? FindBagById(Guid id)
    {
        if (Id == id) return this;

        var queue = new Queue<Bag>();
        queue.Enqueue(this);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            foreach (var cell in current.Grid.Cells)
            {
                if (cell.Stack?.ContainedBag is not { } inner) continue;
                if (inner.Id == id) return inner;
                queue.Enqueue(inner);
            }
        }

        return null;
    }
}
