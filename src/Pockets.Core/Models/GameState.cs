namespace Pockets.Core.Models;

/// <summary>
/// Top-level game state composing the root bag, cursor, hand bag, and known item types.
/// Hand is a real bag that holds grabbed items (true cut). All operations return new instances (immutable).
/// </summary>
public record GameState(
    Bag RootBag,
    Cursor Cursor,
    ImmutableArray<ItemType> ItemTypes,
    Bag HandBag)
{
    /// <summary>
    /// True when the hand bag contains at least one item.
    /// </summary>
    public bool HasItemsInHand => HandBag.Grid.Cells.Any(c => !c.IsEmpty);

    /// <summary>
    /// Returns all item stacks currently in the hand bag.
    /// </summary>
    public IReadOnlyList<ItemStack> HandItems => HandBag.Grid.Cells
        .Where(c => !c.IsEmpty)
        .Select(c => c.Stack!)
        .ToList();

    /// <summary>
    /// Creates an empty hand bag with the given number of slots (1×N grid).
    /// </summary>
    public static Bag CreateHandBag(int handSize = 1) =>
        new Bag(Grid.Create(handSize, 1));

    /// <summary>
    /// Creates the initial Stage 1 game state: 8×4 bag, cursor at origin,
    /// with the given item stacks acquired into the grid.
    /// </summary>
    public static GameState CreateStage1(
        ImmutableArray<ItemType> itemTypes,
        IEnumerable<ItemStack> initialStacks,
        GameConfig? config = null)
    {
        config ??= new GameConfig();
        var bag = new Bag(Grid.Create(8, 4));
        var (filledBag, _) = bag.AcquireItems(initialStacks);
        return new GameState(filledBag, new Cursor(new Position(0, 0)), itemTypes, CreateHandBag(config.HandSize));
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
    /// Grab: remove item from cursor cell and acquire it into the hand bag.
    /// If cursor is empty, no-op success. If hand is full, no-op with error.
    /// Always adds to hand (no toggle cancel).
    /// </summary>
    public ToolResult ToolGrab()
    {
        if (CurrentCell.IsEmpty)
            return ToolResult.Ok(this);

        var stack = CurrentCell.Stack!;
        var (updatedHand, unplaced) = HandBag.AcquireItems(new[] { stack });

        if (unplaced.Count > 0)
            return ToolResult.Fail(this, "Hand is full");

        var grid = RootBag.Grid.SetCell(Cursor.Position, new Cell());
        return ToolResult.Ok(this with
        {
            RootBag = RootBag with { Grid = grid },
            HandBag = updatedHand
        });
    }

    /// <summary>
    /// Drop: place all hand items at cursor cell (merge if same type), then acquire
    /// any remainder from cell 0. No-op if hand is empty. No-op with error if cursor
    /// has a different type or bag is completely full.
    /// </summary>
    public ToolResult ToolDrop()
    {
        if (!HasItemsInHand)
            return ToolResult.Ok(this);

        var handItems = HandItems;
        var grid = RootBag.Grid;
        var cursorCell = grid.GetCell(Cursor.Position);
        var firstItem = handItems[0];

        var remainders = new List<ItemStack>();

        if (cursorCell.IsEmpty)
        {
            var max = firstItem.ItemType.EffectiveMaxStackSize;
            var placeCount = Math.Min(firstItem.Count, max);
            cursorCell = cursorCell with { Stack = firstItem with { Count = placeCount } };
            if (firstItem.Count > placeCount)
                remainders.Add(firstItem with { Count = firstItem.Count - placeCount });
        }
        else if (cursorCell.Stack!.ItemType == firstItem.ItemType)
        {
            var (merged, remainder) = cursorCell.Stack.TryMerge(firstItem);
            cursorCell = cursorCell with { Stack = merged };
            if (remainder is not null)
                remainders.Add(remainder);
        }
        else
        {
            return ToolResult.Fail(this, "Cannot drop: different item type at cursor");
        }

        remainders.AddRange(handItems.Skip(1));
        grid = grid.SetCell(Cursor.Position, cursorCell);

        if (remainders.Count > 0)
        {
            var (updatedGrid, unplaced) = grid.AcquireItems(remainders);
            if (unplaced.Count > 0)
                return ToolResult.Fail(this, "Cannot drop: bag is full");
            grid = updatedGrid;
        }

        var emptyHand = CreateHandBag(HandBag.Grid.Columns);
        return ToolResult.Ok(this with
        {
            RootBag = RootBag with { Grid = grid },
            HandBag = emptyHand
        });
    }

    /// <summary>
    /// Quick split: split cursor cell in half (ceiling left, floor right).
    /// Left stays at cursor, right goes into hand bag.
    /// No-op if cell is empty, count ≤ 1, or hand can't accept the split.
    /// </summary>
    public ToolResult ToolQuickSplit()
    {
        var cell = CurrentCell;
        if (cell.IsEmpty || cell.Stack!.Count <= 1)
            return ToolResult.Ok(this);

        var splitResult = cell.Stack.Split();
        if (splitResult is null)
            return ToolResult.Ok(this);

        var (left, right) = splitResult.Value;

        var (updatedHand, unplaced) = HandBag.AcquireItems(new[] { right });
        if (unplaced.Count > 0)
            return ToolResult.Fail(this, "Hand is full");

        var grid = RootBag.Grid.SetCell(Cursor.Position, cell with { Stack = left });

        return ToolResult.Ok(this with
        {
            RootBag = RootBag with { Grid = grid },
            HandBag = updatedHand
        });
    }

    /// <summary>
    /// Sort: collect all stacks, group by type summing counts, split into max-size stacks,
    /// sort by (Category, Name), empty grid, re-acquire sorted stacks. No-op if grid is empty.
    /// </summary>
    public ToolResult ToolSort()
    {
        var grid = RootBag.Grid;
        var allStacks = grid.Cells
            .Where(c => !c.IsEmpty)
            .Select(c => c.Stack!)
            .ToList();

        if (allStacks.Count == 0)
            return ToolResult.Ok(this);

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

        return ToolResult.Ok(this with { RootBag = RootBag with { Grid = updatedGrid } });
    }

    /// <summary>
    /// Debug tool: pick a random item type and acquire 1 into the grid.
    /// </summary>
    public ToolResult ToolAcquireRandom(Random rng)
    {
        var itemType = ItemTypes[rng.Next(ItemTypes.Length)];
        var stack = new ItemStack(itemType, 1);
        var (updatedBag, _) = RootBag.AcquireItems(new[] { stack });
        return ToolResult.Ok(this with { RootBag = updatedBag });
    }
}
