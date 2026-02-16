namespace Pockets.Core.Models;

/// <summary>
/// An immutable X×Y collection of Cells. Row-major layout.
/// </summary>
public record Grid(int Columns, int Rows, ImmutableArray<Cell> Cells)
{
    /// <summary>
    /// Creates an empty grid of the given dimensions, all cells empty.
    /// </summary>
    public static Grid Create(int columns, int rows)
    {
        var cells = Enumerable.Repeat(new Cell(), columns * rows).ToImmutableArray();
        return new Grid(columns, rows, cells);
    }

    /// <summary>
    /// Returns the cell at the given position.
    /// </summary>
    public Cell GetCell(Position pos) => Cells[pos.ToIndex(Columns)];

    /// <summary>
    /// Returns the cell at the given flat index.
    /// </summary>
    public Cell GetCell(int index) => Cells[index];

    /// <summary>
    /// Returns a new Grid with the cell at the given position replaced.
    /// </summary>
    public Grid SetCell(Position pos, Cell cell) =>
        this with { Cells = Cells.SetItem(pos.ToIndex(Columns), cell) };

    /// <summary>
    /// Returns a new Grid with the cell at the given flat index replaced.
    /// </summary>
    public Grid SetCell(int index, Cell cell) =>
        this with { Cells = Cells.SetItem(index, cell) };

    /// <summary>
    /// Places item stacks into the grid using the acquisition algorithm.
    /// Each stack scans cells 0..N-1, skipping filtered/mismatched cells,
    /// merging into matching stacks, and filling empty cells. Each stack restarts from cell 0.
    /// Optional skipIndices excludes specific cell indices from placement.
    /// Returns the updated grid and any stacks that couldn't be placed.
    /// </summary>
    public (Grid UpdatedGrid, IReadOnlyList<ItemStack> Unplaced) AcquireItems(
        IEnumerable<ItemStack> stacks, ImmutableHashSet<int>? skipIndices = null)
    {
        var builder = Cells.ToBuilder();
        var unplaced = new List<ItemStack>();

        foreach (var stack in stacks)
        {
            var remaining = stack;

            for (int i = 0; i < builder.Count && remaining is not null; i++)
            {
                if (skipIndices?.Contains(i) == true)
                    continue;

                var cell = builder[i];

                if (!cell.Accepts(remaining.ItemType))
                    continue;

                if (cell.IsEmpty)
                {
                    var max = remaining.ItemType.EffectiveMaxStackSize;
                    var placeCount = Math.Min(remaining.Count, max);
                    builder[i] = cell with { Stack = remaining with { Count = placeCount } };
                    var excess = remaining.Count - placeCount;
                    remaining = excess > 0 ? remaining with { Count = excess } : null;
                }
                else if (cell.Stack!.ItemType == remaining.ItemType)
                {
                    var (merged, remainder) = cell.Stack.TryMerge(remaining);
                    builder[i] = cell with { Stack = merged };
                    remaining = remainder;
                }
            }

            if (remaining is not null)
                unplaced.Add(remaining);
        }

        var updatedGrid = this with { Cells = builder.MoveToImmutable() };
        return (updatedGrid, unplaced);
    }
}
