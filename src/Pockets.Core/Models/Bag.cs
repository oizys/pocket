namespace Pockets.Core.Models;

/// <summary>
/// An openable item containing a Grid of cells. Bags nest inside other bags.
/// </summary>
public record Bag(
    Grid Grid,
    string EnvironmentType = "Default",
    string ColorScheme = "Default")
{
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
}
