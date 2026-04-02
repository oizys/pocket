namespace Pockets.Core.Models;

/// <summary>
/// Top-level game state: a flat bag store, a set of named locations (cursors into the store),
/// and the known item types. All operations return new instances (immutable).
/// Tools operate on the B (bag/inventory) location by default.
/// </summary>
public record GameState(
    BagStore Store,
    LocationMap Locations,
    ImmutableArray<ItemType> ItemTypes)
{
    // ==================== Location accessors ====================

    /// <summary>
    /// The B (inventory) location. Always present.
    /// </summary>
    private Location BLoc => Locations.Get(LocationId.B);

    /// <summary>
    /// The H (hand) location. Always present.
    /// </summary>
    private Location HLoc => Locations.Get(LocationId.H);

    // ==================== Backward-compatible computed properties ====================

    /// <summary>
    /// The root bag Id for the inventory location.
    /// </summary>
    public Guid RootBagId => BLoc.BagId;

    /// <summary>
    /// The hand bag Id.
    /// </summary>
    public Guid HandBagId => HLoc.BagId;

    /// <summary>
    /// The current cursor in the inventory location.
    /// </summary>
    public Cursor Cursor => BLoc.Cursor;

    /// <summary>
    /// The breadcrumb stack for the inventory location.
    /// </summary>
    public ImmutableStack<BreadcrumbEntry> BreadcrumbStack => BLoc.Breadcrumbs;

    /// <summary>
    /// True when inside a nested bag in the inventory location.
    /// </summary>
    public bool IsNested => BLoc.IsNested;

    /// <summary>
    /// The root bag, resolved from the store.
    /// </summary>
    public Bag RootBag => Store.GetById(RootBagId)!;

    /// <summary>
    /// The hand bag, resolved from the store.
    /// </summary>
    public Bag HandBag => Store.GetById(HandBagId)!;

    // ==================== Active bag resolution ====================

    /// <summary>
    /// The Id of the bag currently being viewed — follows breadcrumbs from root to leaf.
    /// </summary>
    public Guid ActiveBagId
    {
        get
        {
            var bagId = RootBagId;
            foreach (var entry in BreadcrumbStack.Reverse())
            {
                var bag = Store.GetById(bagId)!;
                var cell = bag.Grid.GetCell(entry.CellIndex);
                if (cell.Stack?.ContainedBagId is not { } childId)
                    break;
                bagId = childId;
            }
            return bagId;
        }
    }

    /// <summary>
    /// The bag currently being viewed — follows breadcrumbs from root to leaf.
    /// </summary>
    public Bag ActiveBag => Store.GetById(ActiveBagId)!;

    /// <summary>
    /// Returns a new GameState with the active bag replaced in the store.
    /// </summary>
    private GameState WithActiveBag(Bag newActiveBag) =>
        this with { Store = Store.Set(ActiveBagId, newActiveBag) };

    // ==================== Hand helpers ====================

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

    // ==================== Location mutation helpers ====================

    /// <summary>
    /// Returns a new GameState with the B location's cursor updated.
    /// </summary>
    private GameState WithCursor(Cursor cursor) =>
        this with { Locations = Locations.Set(LocationId.B, BLoc with { Cursor = cursor }) };

    /// <summary>
    /// Returns a new GameState with the B location's breadcrumbs updated.
    /// </summary>
    private GameState WithBreadcrumbs(ImmutableStack<BreadcrumbEntry> breadcrumbs) =>
        this with { Locations = Locations.Set(LocationId.B, BLoc with { Breadcrumbs = breadcrumbs }) };

    /// <summary>
    /// Returns a new GameState with the B location's cursor and breadcrumbs updated.
    /// </summary>
    private GameState WithBLocation(Cursor cursor, ImmutableStack<BreadcrumbEntry> breadcrumbs) =>
        this with { Locations = Locations.Set(LocationId.B, BLoc with { Cursor = cursor, Breadcrumbs = breadcrumbs }) };

    /// <summary>
    /// Returns a new GameState with the hand bag replaced in the store.
    /// </summary>
    private GameState WithHandBag(Bag newHand) =>
        this with { Store = Store.Set(HandBagId, newHand) };

    // ==================== Factory ====================

    /// <summary>
    /// Creates the initial Stage 1 game state: 8×4 bag, cursor at origin,
    /// with the given item stacks acquired into the grid.
    /// Any extra bags (referenced by ContainedBagId in the stacks) must be passed via extraBags.
    /// </summary>
    public static GameState CreateStage1(
        ImmutableArray<ItemType> itemTypes,
        IEnumerable<ItemStack> initialStacks,
        GameConfig? config = null,
        IEnumerable<Bag>? extraBags = null)
    {
        config ??= new GameConfig();
        var bag = new Bag(Grid.Create(8, 4));
        var (filledBag, _) = bag.AcquireItems(initialStacks);
        var handBag = CreateHandBag(config.HandSize);

        var store = BagStore.Empty.Add(filledBag).Add(handBag);
        if (extraBags is not null)
            store = store.AddRange(extraBags);

        var locations = LocationMap.Create(handBag.Id, filledBag.Id);

        return new GameState(store, locations, itemTypes);
    }

    // ==================== Navigation ====================

    /// <summary>
    /// Returns a new GameState with the cursor moved one step in the given direction,
    /// wrapping within the active bag's grid.
    /// </summary>
    public GameState MoveCursor(Direction direction) =>
        WithCursor(Cursor.Move(direction, ActiveBag.Grid.Rows, ActiveBag.Grid.Columns));

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

        return ToolResult.Ok(WithBLocation(new Cursor(new Position(0, 0)), newBreadcrumbs));
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

        return ToolResult.Ok(WithBLocation(top.SavedCursor, poppedStack));
    }

    /// <summary>
    /// The breadcrumb path as a list of bag names/descriptions for display.
    /// </summary>
    public IReadOnlyList<string> BreadcrumbPath
    {
        get
        {
            var path = new List<string> { RootBag.EnvironmentType };
            var bagId = RootBagId;
            foreach (var entry in BreadcrumbStack.Reverse())
            {
                var bag = Store.GetById(bagId)!;
                var cell = bag.Grid.GetCell(entry.CellIndex);
                if (cell.Stack?.ContainedBagId is not { } childId) break;
                var name = cell.Stack.ItemType.Name;
                path.Add(name);
                bagId = childId;
            }
            return path;
        }
    }

    // ==================== Bag replacement ====================

    /// <summary>
    /// Replaces a bag in the store by its Id. Optionally transforms the owning ItemStack
    /// (e.g. to update progress properties) by scanning for the parent cell.
    /// </summary>
    public GameState ReplaceBagById(Guid bagId, Bag replacement, Func<ItemStack, ItemStack>? ownerTransform = null)
    {
        var newStore = Store.Set(bagId, replacement);

        if (ownerTransform is not null)
        {
            var ownerInfo = Store.GetOwnerOf(bagId);
            if (ownerInfo is not null)
            {
                var parentBag = newStore.GetById(ownerInfo.ParentBagId)!;
                var cell = parentBag.Grid.GetCell(ownerInfo.CellIndex);
                if (cell.Stack is not null)
                {
                    var updatedStack = ownerTransform(cell.Stack);
                    var updatedCell = cell with { Stack = updatedStack };
                    var updatedParent = parentBag with { Grid = parentBag.Grid.SetCell(ownerInfo.CellIndex, updatedCell) };
                    newStore = newStore.Set(ownerInfo.ParentBagId, updatedParent);
                }
            }
        }

        return this with { Store = newStore };
    }

    // ==================== Tools (operate on ActiveBag) ====================

    /// <summary>
    /// Primary action (left-click / key 1). Context-sensitive.
    /// </summary>
    public ToolResult ToolPrimary()
    {
        var cell = CurrentCell;

        if (cell.Frame is OutputSlotFrame && !cell.IsEmpty && !HasItemsInHand)
            return ToolGrab();

        if (cell.HasBag)
            return EnterBag();

        if (!HasItemsInHand)
        {
            if (cell.IsEmpty)
                return ToolResult.Ok(this);
            return IsNested ? ToolHarvest() : ToolGrab();
        }

        if (cell.IsEmpty)
            return ToolDrop();
        if (cell.Stack!.ItemType == HandItems[0].ItemType)
            return ToolDrop();
        return ToolSwap();
    }

    /// <summary>
    /// Secondary action (right-click / key 2). Half/one variant.
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

        var activeBag = ActiveBag;
        var grid = activeBag.Grid.SetCell(Cursor.Position, CurrentCell with { Stack = handStack });

        var emptyHand = CreateHandBag(HandBag.Grid.Columns);
        var (newHand, unplaced) = emptyHand.AcquireItems(new[] { cellStack });
        if (unplaced.Count > 0)
            return ToolResult.Fail(this, "Cannot swap: hand cannot hold this item");

        var newStore = Store
            .Set(ActiveBagId, activeBag with { Grid = grid })
            .Set(HandBagId, newHand);
        return ToolResult.Ok(this with { Store = newStore });
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
        var grid = activeBag.Grid.SetCell(Cursor.Position, cell with { Stack = newCellStack });

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

        var newStore = Store
            .Set(ActiveBagId, activeBag with { Grid = grid })
            .Set(HandBagId, updatedHand);
        return ToolResult.Ok(this with { Store = newStore });
    }

    /// <summary>
    /// Grab: remove item from cursor cell and acquire it into the hand bag.
    /// </summary>
    public ToolResult ToolGrab()
    {
        if (CurrentCell.Frame is PlanterFrame && !CurrentCell.IsEmpty && PlantLogic.IsGrown(CurrentCell.Stack!))
            return ToolHarvestPlant();

        if (CurrentCell.IsEmpty)
            return ToolResult.Ok(this);

        var stack = CurrentCell.Stack!;
        var (updatedHand, unplaced) = HandBag.AcquireItems(new[] { stack });

        if (unplaced.Count > 0)
            return ToolResult.Fail(this, "Hand is full");

        var activeBag = ActiveBag;
        var grid = activeBag.Grid.SetCell(Cursor.Position, CurrentCell with { Stack = null });

        var newStore = Store
            .Set(ActiveBagId, activeBag with { Grid = grid })
            .Set(HandBagId, updatedHand);
        return ToolResult.Ok(this with { Store = newStore });
    }

    /// <summary>
    /// Harvests produce from a fully grown plant.
    /// </summary>
    private ToolResult ToolHarvestPlant()
    {
        var plant = CurrentCell.Stack!;
        var produceName = plant.GetString("Produce");
        if (produceName is null)
            return ToolResult.Fail(this, "Plant has no Produce property");

        var produceType = ItemTypes.FirstOrDefault(t => t.Name == produceName);
        if (produceType is null)
            return ToolResult.Fail(this, $"Unknown produce type: {produceName}");

        var yield = plant.GetInt("Yield") ?? 3;
        var produceStack = new ItemStack(produceType, yield);

        var (updatedHand, unplaced) = HandBag.AcquireItems(new[] { produceStack });
        if (unplaced.Count > 0)
            return ToolResult.Fail(this, "Hand is full");

        var resetPlant = plant.WithProperty("Progress", new IntValue(0));
        var activeBag = ActiveBag;
        var grid = activeBag.Grid.SetCell(Cursor.Position, CurrentCell with { Stack = resetPlant });

        var newStore = Store
            .Set(ActiveBagId, activeBag with { Grid = grid })
            .Set(HandBagId, updatedHand);
        return ToolResult.Ok(this with { Store = newStore });
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

        if (!cursorCell.Accepts(firstItem.ItemType))
            return ToolResult.Fail(this, "Cannot drop: cell does not accept this item");

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
        var newStore = Store
            .Set(ActiveBagId, activeBag with { Grid = grid })
            .Set(HandBagId, emptyHand);
        return ToolResult.Ok(this with { Store = newStore });
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

        var newStore = Store
            .Set(ActiveBagId, activeBag with { Grid = grid })
            .Set(HandBagId, updatedHand);
        return ToolResult.Ok(this with { Store = newStore });
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

        var newStore = Store
            .Set(ActiveBagId, activeBag with { Grid = grid })
            .Set(HandBagId, updatedHand);
        return ToolResult.Ok(this with { Store = newStore });
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
                var bagItems = g.Where(s => s.ContainedBagId is not null).ToList();
                var total = g.Where(s => s.ContainedBagId is null).Sum(s => s.Count);
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
    /// into the parent bag. Only works when inside a nested bag.
    /// </summary>
    public ToolResult ToolHarvest()
    {
        if (!IsNested)
            return ToolResult.Fail(this, "Not in a bag");

        var cell = CurrentCell;
        if (cell.IsEmpty)
            return ToolResult.Ok(this);

        var item = cell.Stack!;
        var activeBagId = ActiveBagId;

        // 1. Clear cursor cell in active bag
        var activeBag = ActiveBag;
        var clearedGrid = activeBag.Grid.SetCell(Cursor.Position, cell with { Stack = null });
        var updatedStore = Store.Set(activeBagId, activeBag with { Grid = clearedGrid });

        // 2. Find parent bag
        var parentBagId = RootBagId;
        var innerBagCellIndex = 0;
        var entries = BreadcrumbStack.Reverse().ToList();

        var currentId = RootBagId;
        for (int i = 0; i < entries.Count; i++)
        {
            var currentBag = updatedStore.GetById(currentId)!;
            var entryCell = currentBag.Grid.GetCell(entries[i].CellIndex);
            if (i == entries.Count - 1)
            {
                parentBagId = currentId;
                innerBagCellIndex = entries[i].CellIndex;
                break;
            }
            currentId = entryCell.Stack!.ContainedBagId!.Value;
        }

        // 3. Acquire harvested item into parent, skipping the cell that holds the inner bag
        var parentBag = updatedStore.GetById(parentBagId)!;
        var (updatedParentGrid, unplaced) = parentBag.Grid.AcquireItems(
            new[] { new ItemStack(item.ItemType, item.Count) },
            ImmutableHashSet.Create(innerBagCellIndex));

        if (unplaced.Count > 0)
            return ToolResult.Fail(this, "Parent bag is full");

        updatedStore = updatedStore.Set(parentBagId, parentBag with { Grid = updatedParentGrid });

        return ToolResult.Ok(this with { Store = updatedStore });
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
