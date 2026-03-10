namespace Pockets.Core.Models;

/// <summary>
/// Top-level game state composing the root bag, cursor, hand bag, breadcrumbs, and known item types.
/// Hand is a real bag that holds grabbed items (true cut). Breadcrumbs enable nested bag navigation.
/// All operations return new instances (immutable). Tools operate on the active bag (leaf of breadcrumb trail).
/// </summary>
public record GameState(
    Bag RootBag,
    Cursor Cursor,
    ImmutableArray<ItemType> ItemTypes,
    Bag HandBag,
    ImmutableStack<BreadcrumbEntry>? Breadcrumbs = null)
{
    /// <summary>
    /// The breadcrumb stack, never null.
    /// </summary>
    public ImmutableStack<BreadcrumbEntry> BreadcrumbStack =>
        Breadcrumbs ?? ImmutableStack<BreadcrumbEntry>.Empty;

    /// <summary>
    /// True when inside a nested bag (breadcrumb stack is non-empty).
    /// </summary>
    public bool IsNested => Breadcrumbs is not null && !Breadcrumbs.IsEmpty;

    /// <summary>
    /// The bag currently being viewed — follows breadcrumbs from root to leaf.
    /// </summary>
    public Bag ActiveBag
    {
        get
        {
            var bag = RootBag;
            foreach (var entry in BreadcrumbStack.Reverse())
            {
                var cell = bag.Grid.GetCell(entry.CellIndex);
                if (cell.Stack?.ContainedBag is null)
                    break;
                bag = cell.Stack.ContainedBag;
            }
            return bag;
        }
    }

    /// <summary>
    /// Returns a new GameState with the active bag replaced, propagating changes
    /// back up the breadcrumb trail to the root.
    /// </summary>
    private GameState WithActiveBag(Bag newActiveBag)
    {
        if (!IsNested)
            return this with { RootBag = newActiveBag };

        // Rebuild from leaf to root using breadcrumb entries
        var entries = BreadcrumbStack.Reverse().ToList();
        var currentBag = newActiveBag;

        for (int i = entries.Count - 1; i >= 0; i--)
        {
            var parentBag = i == 0
                ? RootBag
                : GetBagAtDepth(i);
            var cell = parentBag.Grid.GetCell(entries[i].CellIndex);
            var updatedCell = cell with { Stack = cell.Stack! with { ContainedBag = currentBag } };
            var updatedGrid = parentBag.Grid.SetCell(entries[i].CellIndex, updatedCell);
            currentBag = parentBag with { Grid = updatedGrid };
        }

        return this with { RootBag = currentBag };
    }

    /// <summary>
    /// Gets the bag at the specified depth in the breadcrumb trail (0 = root).
    /// </summary>
    private Bag GetBagAtDepth(int depth)
    {
        var bag = RootBag;
        var entries = BreadcrumbStack.Reverse().ToList();
        for (int i = 0; i < depth && i < entries.Count; i++)
        {
            var cell = bag.Grid.GetCell(entries[i].CellIndex);
            if (cell.Stack?.ContainedBag is null) break;
            bag = cell.Stack.ContainedBag;
        }
        return bag;
    }

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
    /// Returns a new GameState with the cursor moved one step in the given direction,
    /// wrapping within the active bag's grid.
    /// </summary>
    public GameState MoveCursor(Direction direction) =>
        this with { Cursor = Cursor.Move(direction, ActiveBag.Grid.Rows, ActiveBag.Grid.Columns) };

    /// <summary>
    /// Returns the cell at the current cursor position in the active bag.
    /// </summary>
    public Cell CurrentCell => ActiveBag.Grid.GetCell(Cursor.Position);

    /// <summary>
    /// Context-sensitive interaction at cursor cell. Dispatches to:
    /// - EnterBag if cursor cell contains a bag
    /// - Harvest if nested and cursor cell has an item (non-bag)
    /// Returns no-op if nothing to interact with.
    /// </summary>
    public ToolResult Interact()
    {
        var cell = CurrentCell;
        if (cell.HasBag)
            return EnterBag();
        if (IsNested && !cell.IsEmpty)
            return ToolHarvest();
        return ToolResult.Ok(this);
    }

    /// <summary>
    /// Enter the bag at the cursor cell. Pushes current cursor onto breadcrumb stack
    /// and resets cursor to (0,0) in the inner bag.
    /// No-op if cursor cell doesn't contain a bag.
    /// </summary>
    public ToolResult EnterBag()
    {
        var cell = CurrentCell;
        if (!cell.HasBag)
            return ToolResult.Fail(this, "No bag at cursor");

        var cellIndex = Cursor.Position.ToIndex(ActiveBag.Grid.Columns);
        var entry = new BreadcrumbEntry(cellIndex, Cursor);
        var newBreadcrumbs = BreadcrumbStack.Push(entry);

        return ToolResult.Ok(this with
        {
            Cursor = new Cursor(new Position(0, 0)),
            Breadcrumbs = newBreadcrumbs
        });
    }

    /// <summary>
    /// Leave the current bag, returning to the parent. Pops the breadcrumb stack
    /// and restores the saved cursor position.
    /// No-op if at root bag (nothing to leave).
    /// </summary>
    public ToolResult LeaveBag()
    {
        if (!IsNested)
            return ToolResult.Fail(this, "Already at root bag");

        var top = BreadcrumbStack.Peek();
        var poppedStack = BreadcrumbStack.Pop();

        return ToolResult.Ok(this with
        {
            Cursor = top.SavedCursor,
            Breadcrumbs = poppedStack.IsEmpty ? null : poppedStack
        });
    }

    /// <summary>
    /// The breadcrumb path as a list of bag names/descriptions for display.
    /// </summary>
    public IReadOnlyList<string> BreadcrumbPath
    {
        get
        {
            var path = new List<string> { RootBag.EnvironmentType };
            var bag = RootBag;
            foreach (var entry in BreadcrumbStack.Reverse())
            {
                var cell = bag.Grid.GetCell(entry.CellIndex);
                if (cell.Stack?.ContainedBag is null) break;
                var name = cell.Stack.ItemType.Name;
                path.Add(name);
                bag = cell.Stack.ContainedBag;
            }
            return path;
        }
    }

    // ==================== Tools (operate on ActiveBag) ====================

    /// <summary>
    /// Primary action (left-click / key 1). Context-sensitive:
    /// - Cell has bag → enter bag
    /// - Nested + empty hand + occupied cell → harvest to parent
    /// - Empty hand + occupied cell → grab full stack
    /// - Full hand + empty cell → drop all
    /// - Full hand + same type → merge (overflow stays in hand)
    /// - Full hand + different type → swap
    /// </summary>
    public ToolResult ToolPrimary()
    {
        var cell = CurrentCell;

        // Interact first: enter bags (always), harvest when nested
        if (cell.HasBag)
            return EnterBag();

        if (!HasItemsInHand)
        {
            if (cell.IsEmpty)
                return ToolResult.Ok(this);
            // Nested non-bag item → harvest; root → grab
            return IsNested ? ToolHarvest() : ToolGrab();
        }

        // Hand is full
        if (cell.IsEmpty)
            return ToolDrop();
        if (cell.Stack!.ItemType == HandItems[0].ItemType)
            return ToolDrop(); // merge
        return ToolSwap();
    }

    /// <summary>
    /// Secondary action (right-click / key 2). Half/one variant:
    /// - Empty hand + occupied cell → grab half
    /// - Full hand + empty cell → place one
    /// - Full hand + same type → place one
    /// </summary>
    public ToolResult ToolSecondary()
    {
        var cell = CurrentCell;

        if (!HasItemsInHand)
        {
            if (cell.IsEmpty || cell.Stack!.Count <= 1)
                return ToolResult.Ok(this);
            return ToolQuickSplit();
        }

        // Hand is full: place one
        if (cell.IsEmpty || cell.Stack!.ItemType == HandItems[0].ItemType)
            return ToolPlaceOne();

        return ToolResult.Ok(this);
    }

    /// <summary>
    /// Swap: exchange hand contents with cursor cell contents.
    /// </summary>
    public ToolResult ToolSwap()
    {
        if (!HasItemsInHand || CurrentCell.IsEmpty)
            return ToolResult.Ok(this);

        var cellStack = CurrentCell.Stack!;
        var handStack = HandItems[0];

        // Put hand item in cell, cell item in hand
        var activeBag = ActiveBag;
        var grid = activeBag.Grid.SetCell(Cursor.Position, new Cell(handStack));

        var emptyHand = CreateHandBag(HandBag.Grid.Columns);
        var (newHand, unplaced) = emptyHand.AcquireItems(new[] { cellStack });
        if (unplaced.Count > 0)
            return ToolResult.Fail(this, "Cannot swap: hand cannot hold this item");

        return ToolResult.Ok(WithActiveBag(activeBag with { Grid = grid }) with
        {
            HandBag = newHand
        });
    }

    /// <summary>
    /// Place one item from hand into cursor cell (empty or same type).
    /// </summary>
    public ToolResult ToolPlaceOne()
    {
        if (!HasItemsInHand)
            return ToolResult.Ok(this);

        var handStack = HandItems[0];
        var cell = CurrentCell;

        ItemStack newCellStack;
        if (cell.IsEmpty)
        {
            newCellStack = handStack with { Count = 1 };
        }
        else if (cell.Stack!.ItemType == handStack.ItemType)
        {
            if (cell.Stack.Count >= cell.Stack.ItemType.EffectiveMaxStackSize)
                return ToolResult.Fail(this, "Stack is full");
            newCellStack = cell.Stack with { Count = cell.Stack.Count + 1 };
        }
        else
        {
            return ToolResult.Ok(this);
        }

        var activeBag = ActiveBag;
        var grid = activeBag.Grid.SetCell(Cursor.Position, new Cell(newCellStack));

        // Reduce hand by 1
        Bag updatedHand;
        if (handStack.Count <= 1)
        {
            updatedHand = CreateHandBag(HandBag.Grid.Columns);
        }
        else
        {
            var reducedStack = handStack with { Count = handStack.Count - 1 };
            updatedHand = CreateHandBag(HandBag.Grid.Columns);
            (updatedHand, _) = updatedHand.AcquireItems(new[] { reducedStack });
        }

        return ToolResult.Ok(WithActiveBag(activeBag with { Grid = grid }) with
        {
            HandBag = updatedHand
        });
    }

    /// <summary>
    /// Grab: remove item from cursor cell and acquire it into the hand bag.
    /// </summary>
    public ToolResult ToolGrab()
    {
        if (CurrentCell.IsEmpty)
            return ToolResult.Ok(this);

        var stack = CurrentCell.Stack!;
        var (updatedHand, unplaced) = HandBag.AcquireItems(new[] { stack });

        if (unplaced.Count > 0)
            return ToolResult.Fail(this, "Hand is full");

        var activeBag = ActiveBag;
        var grid = activeBag.Grid.SetCell(Cursor.Position, new Cell());
        return ToolResult.Ok(WithActiveBag(activeBag with { Grid = grid }) with
        {
            HandBag = updatedHand
        });
    }

    /// <summary>
    /// Drop: place all hand items at cursor cell, remainder acquires from cell 0.
    /// </summary>
    public ToolResult ToolDrop()
    {
        if (!HasItemsInHand)
            return ToolResult.Ok(this);

        var handItems = HandItems;
        var activeBag = ActiveBag;
        var grid = activeBag.Grid;
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
        return ToolResult.Ok(WithActiveBag(activeBag with { Grid = grid }) with
        {
            HandBag = emptyHand
        });
    }

    /// <summary>
    /// Quick split: split cursor cell in half, right goes to hand.
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

        var activeBag = ActiveBag;
        var grid = activeBag.Grid.SetCell(Cursor.Position, cell with { Stack = left });

        return ToolResult.Ok(WithActiveBag(activeBag with { Grid = grid }) with
        {
            HandBag = updatedHand
        });
    }

    /// <summary>
    /// Modal split: split cursor cell with a specified left count, right goes to hand.
    /// </summary>
    public ToolResult ToolModalSplit(int leftCount)
    {
        var cell = CurrentCell;
        if (cell.IsEmpty || cell.Stack!.Count <= 1)
            return ToolResult.Ok(this);

        var splitResult = cell.Stack.Split(leftCount);
        if (splitResult is null)
            return ToolResult.Fail(this, "Invalid split amount");

        var (left, right) = splitResult.Value;

        var (updatedHand, unplaced) = HandBag.AcquireItems(new[] { right });
        if (unplaced.Count > 0)
            return ToolResult.Fail(this, "Hand is full");

        var activeBag = ActiveBag;
        var grid = activeBag.Grid.SetCell(Cursor.Position, cell with { Stack = left });

        return ToolResult.Ok(WithActiveBag(activeBag with { Grid = grid }) with
        {
            HandBag = updatedHand
        });
    }

    /// <summary>
    /// Sort: collect all stacks, sort by (Category, Name), merge, re-acquire.
    /// </summary>
    public ToolResult ToolSort()
    {
        var activeBag = ActiveBag;
        var grid = activeBag.Grid;
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
                // Preserve items with ContainedBag as-is (unique bag items)
                var bagItems = g.Where(s => s.ContainedBag is not null).ToList();
                var total = g.Where(s => s.ContainedBag is null).Sum(s => s.Count);
                var max = g.Key.EffectiveMaxStackSize;
                var stacks = new List<ItemStack>(bagItems);
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

        return ToolResult.Ok(WithActiveBag(activeBag with { Grid = updatedGrid }));
    }

    /// <summary>
    /// Harvest: remove item from cursor cell in the active (inner) bag and acquire it
    /// into the parent bag. Only works when inside a nested bag. Skips the cell that
    /// holds the inner bag so it won't be overwritten.
    /// </summary>
    public ToolResult ToolHarvest()
    {
        if (!IsNested)
            return ToolResult.Fail(this, "Not in a bag");

        var cell = CurrentCell;
        if (cell.IsEmpty)
            return ToolResult.Ok(this);

        var item = cell.Stack!;

        // 1. Clear cursor cell in active bag and propagate to root
        var activeBag = ActiveBag;
        var clearedGrid = activeBag.Grid.SetCell(Cursor.Position, new Cell());
        var state = WithActiveBag(activeBag with { Grid = clearedGrid });

        // 2. Find parent bag in the updated tree
        var entries = state.BreadcrumbStack.Reverse().ToList();
        var parentDepth = entries.Count - 1;
        var parentBag = parentDepth == 0 ? state.RootBag : state.GetBagAtDepth(parentDepth);
        var innerBagCellIndex = entries[^1].CellIndex;

        // 3. Acquire harvested item into parent, skipping the cell that holds the inner bag
        var (updatedParentGrid, unplaced) = parentBag.Grid.AcquireItems(
            new[] { new ItemStack(item.ItemType, item.Count) },
            ImmutableHashSet.Create(innerBagCellIndex));

        if (unplaced.Count > 0)
            return ToolResult.Fail(this, "Parent bag is full");

        // 4. Update parent bag in tree
        if (parentDepth == 0)
        {
            return ToolResult.Ok(state with { RootBag = parentBag with { Grid = updatedParentGrid } });
        }
        else
        {
            // Deep nesting: rebuild from parent level to root
            var updatedParent = parentBag with { Grid = updatedParentGrid };
            var currentBag = updatedParent;
            for (int i = parentDepth - 1; i >= 0; i--)
            {
                var ancestor = i == 0
                    ? state.RootBag
                    : state.GetBagAtDepth(i);
                var ancestorCell = ancestor.Grid.GetCell(entries[i].CellIndex);
                var updatedCell = ancestorCell with { Stack = ancestorCell.Stack! with { ContainedBag = currentBag } };
                var updatedGrid = ancestor.Grid.SetCell(entries[i].CellIndex, updatedCell);
                currentBag = ancestor with { Grid = updatedGrid };
            }
            return ToolResult.Ok(state with { RootBag = currentBag });
        }
    }

    /// <summary>
    /// Debug tool: pick a random item type and acquire 1 into the active bag.
    /// </summary>
    public ToolResult ToolAcquireRandom(Random rng)
    {
        var itemType = ItemTypes[rng.Next(ItemTypes.Length)];
        var stack = new ItemStack(itemType, 1);
        var activeBag = ActiveBag;
        var (updatedBag, _) = activeBag.AcquireItems(new[] { stack });
        return ToolResult.Ok(WithActiveBag(updatedBag));
    }
}
