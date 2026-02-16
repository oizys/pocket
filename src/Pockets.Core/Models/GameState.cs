namespace Pockets.Core.Models;

/// <summary>
/// Top-level game state composing the root bag, cursor, and known item types.
/// Hand tracks grabbed cell positions. All operations return new instances (immutable).
/// </summary>
public record GameState(
    Bag RootBag,
    Cursor Cursor,
    ImmutableArray<ItemType> ItemTypes,
    ImmutableHashSet<Position>? Hand = null)
{
    /// <summary>
    /// The set of grabbed positions, or empty if nothing is grabbed.
    /// </summary>
    public ImmutableHashSet<Position> ActiveHand => Hand ?? ImmutableHashSet<Position>.Empty;

    /// <summary>
    /// True when the hand holds at least one grabbed position.
    /// </summary>
    public bool HasItemsInHand => Hand is not null && Hand.Count > 0;
    /// <summary>
    /// Creates the initial Stage 1 game state: 8×4 bag, cursor at origin,
    /// with the given item stacks acquired into the grid.
    /// </summary>
    public static GameState CreateStage1(
        ImmutableArray<ItemType> itemTypes,
        IEnumerable<ItemStack> initialStacks)
    {
        var bag = new Bag(Grid.Create(8, 4));
        var (filledBag, _) = bag.AcquireItems(initialStacks);
        return new GameState(filledBag, new Cursor(new Position(0, 0)), itemTypes);
    }

    /// <summary>
    /// Returns a new GameState with the cursor moved one step in the given direction.
    /// </summary>
    public GameState MoveCursor(Direction direction) =>
        this with { Cursor = Cursor.Move(direction, RootBag.Grid.Rows, RootBag.Grid.Columns) };

    /// <summary>
    /// Returns the cell at the current cursor position.
    /// </summary>
    public Cell CurrentCell => RootBag.Grid.GetCell(Cursor.Position);

    /// <summary>
    /// Toggle grab: if hand is empty and cursor cell has items, grab cursor position.
    /// If hand is non-empty, cancel grab (clear hand). Empty cell with empty hand is no-op.
    /// </summary>
    public GameState ToolGrab()
    {
        if (HasItemsInHand)
            return this with { Hand = null };

        if (CurrentCell.IsEmpty)
            return this;

        return this with { Hand = ImmutableHashSet.Create(Cursor.Position) };
    }

    /// <summary>
    /// Drop: collect items from hand positions, empty those cells, place items into
    /// the cursor cell first (merge if same type), then acquire any remainder from cell 0.
    /// Empty hand is no-op.
    /// </summary>
    public GameState ToolDrop()
    {
        if (!HasItemsInHand)
            return this;

        var grid = RootBag.Grid;
        var items = Hand!
            .Select(pos => grid.GetCell(pos).Stack)
            .Where(s => s is not null)
            .Cast<ItemStack>()
            .ToList();

        var clearedGrid = Hand!.Aggregate(grid, (g, pos) => g.SetCell(pos, new Cell()));

        var cursorCell = clearedGrid.GetCell(Cursor.Position);
        var remainders = new List<ItemStack>();

        foreach (var item in items)
        {
            if (cursorCell.IsEmpty && cursorCell.Accepts(item.ItemType))
            {
                var max = item.ItemType.EffectiveMaxStackSize;
                var placeCount = Math.Min(item.Count, max);
                cursorCell = cursorCell with { Stack = item with { Count = placeCount } };
                if (item.Count > placeCount)
                    remainders.Add(item with { Count = item.Count - placeCount });
            }
            else if (!cursorCell.IsEmpty && cursorCell.Stack!.ItemType == item.ItemType)
            {
                var (merged, remainder) = cursorCell.Stack.TryMerge(item);
                cursorCell = cursorCell with { Stack = merged };
                if (remainder is not null)
                    remainders.Add(remainder);
            }
            else
            {
                remainders.Add(item);
            }
        }

        clearedGrid = clearedGrid.SetCell(Cursor.Position, cursorCell);
        var (updatedGrid, _) = clearedGrid.AcquireItems(remainders);

        return this with { RootBag = RootBag with { Grid = updatedGrid }, Hand = null };
    }

    /// <summary>
    /// Quick split: split cursor cell in half (ceiling left, floor right).
    /// Left stays at cursor, right is placed into the grid (skipping cursor) and
    /// marked as grabbed in the Hand.
    /// No-op if cell is empty or count ≤ 1.
    /// </summary>
    public GameState ToolQuickSplit()
    {
        var cell = CurrentCell;
        if (cell.IsEmpty || cell.Stack!.Count <= 1)
            return this;

        var splitResult = cell.Stack.Split();
        if (splitResult is null)
            return this;

        var (left, right) = splitResult.Value;
        var cursorIndex = Cursor.Position.ToIndex(RootBag.Grid.Columns);
        var gridBefore = RootBag.Grid.SetCell(Cursor.Position, cell with { Stack = left });
        var skipIndices = ImmutableHashSet.Create(cursorIndex);
        var (gridAfter, _) = gridBefore.AcquireItems(new[] { right }, skipIndices);

        var grabbedPositions = Enumerable.Range(0, gridAfter.Cells.Length)
            .Where(i => i != cursorIndex && gridBefore.Cells[i] != gridAfter.Cells[i])
            .Select(i => Position.FromIndex(i, RootBag.Grid.Columns))
            .ToImmutableHashSet();

        return this with
        {
            RootBag = RootBag with { Grid = gridAfter },
            Hand = grabbedPositions.Count > 0 ? grabbedPositions : null
        };
    }

    /// <summary>
    /// Sort: collect all stacks, group by type summing counts, split into max-size stacks,
    /// sort by (Category, Name), empty grid, re-acquire sorted stacks. No-op if grid is empty.
    /// </summary>
    public GameState ToolSort()
    {
        var grid = RootBag.Grid;
        var allStacks = grid.Cells
            .Where(c => !c.IsEmpty)
            .Select(c => c.Stack!)
            .ToList();

        if (allStacks.Count == 0)
            return this;

        var sorted = allStacks
            .GroupBy(s => s.ItemType)
            .SelectMany(g =>
            {
                var total = g.Sum(s => s.Count);
                var max = g.Key.EffectiveMaxStackSize;
                var stacks = new List<ItemStack>();
                while (total > 0)
                {
                    var count = Math.Min(total, max);
                    stacks.Add(new ItemStack(g.Key, count));
                    total -= count;
                }
                return stacks;
            })
            .OrderBy(s => s.ItemType.Category)
            .ThenBy(s => s.ItemType.Name)
            .ToList();

        var emptyGrid = Grid.Create(grid.Columns, grid.Rows);
        var (updatedGrid, _) = emptyGrid.AcquireItems(sorted);

        return this with { RootBag = RootBag with { Grid = updatedGrid } };
    }

    /// <summary>
    /// Debug tool: pick a random item type and acquire 1 into the grid.
    /// </summary>
    public GameState ToolAcquireRandom(Random rng)
    {
        var itemType = ItemTypes[rng.Next(ItemTypes.Length)];
        var stack = new ItemStack(itemType, 1);
        var (updatedBag, _) = RootBag.AcquireItems(new[] { stack });
        return this with { RootBag = updatedBag };
    }
}
