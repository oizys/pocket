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
    /// <summary>
    /// Chrome-as-state: which UI elements exist right now. Defaults to everything-on so non-demo
    /// profiles (and every existing stage/test) see no behavior change. The demo profile starts
    /// this near-empty and grows it through gameplay triggers. Mutated only via
    /// <see cref="FireUiTrigger"/> / <see cref="UiLedger.Fire"/>, never by a frontend.
    /// </summary>
    public UiLedger Ui { get; init; } = UiLedger.AllPresent;

    /// <summary>
    /// Runtime dialogue progression (the active beat, fired-once guard, unique-inspect counter).
    /// Defaults to <see cref="DialogueState.Empty"/> so non-demo profiles never show dialogue. The
    /// beat definitions themselves live on <see cref="GameSession"/> (<see cref="DialogueBook"/>).
    /// Progression is monotonic — see <see cref="DialogueState"/> for the undo decision.
    /// </summary>
    public DialogueState Dialogue { get; init; } = DialogueState.Empty;

    /// <summary>
    /// Fixed-inventory routing rule (Slice 3). When true — the demo profile — a bare pickup in the
    /// inventory (Primary on a resting item, empty hand, not a bag/output-slot, not nested) routes
    /// the item to the first available <b>toolbar</b> slot instead of the hand: the toolbar is where
    /// pickups land, the hand stays reserved as the in-flight cut buffer (grab-for-move, still reached
    /// by Primary on the toolbar/facility panels). Defaults false so every non-demo profile keeps the
    /// classic grab-into-hand behavior unchanged. Set only by <see cref="GameInitializer.CreateDemoProfile"/>.
    /// </summary>
    public bool ToolbarPickup { get; init; } = false;

    /// <summary>
    /// The known-recipe registry (Slice 6, journey 15:30 — the explorer path). Recipe ids the player
    /// has learned by picking up recipe-as-item finds (an <see cref="ItemStack"/> carrying a
    /// <see cref="RecipeItemProperty"/> naming its recipe). Grows monotonically as such items enter the
    /// hand/toolbar (see <see cref="GameSession"/>); serialized in the VM (<c>knownRecipes</c>). The
    /// "another Quiet 1" recipe proves the explorer path; wiring it to an actually-craftable
    /// <see cref="Recipe"/> is a Slice-7 table-context concern — this slice only holds the state + item.
    /// Defaults empty; every non-demo profile is unaffected.
    /// </summary>
    public ImmutableHashSet<string> KnownRecipes { get; init; } = ImmutableHashSet<string>.Empty;

    /// <summary>
    /// Zones-reached state (Slice 7, journey 26:00 — the minimap). The distinct EnterOnly-wilderness
    /// bag ids the player has <b>entered</b> (crossed the threshold into), in first-entry order. Each
    /// entry lights one wedge of the cosmology's 12-wedge ring around the Core dot. Crafting a new
    /// wilderness does NOT light a wedge — only entering it does — so the wedge count is a record of
    /// where the player has physically been. The VM projects only the count + wedge indices (never the
    /// bag GUIDs), so it is deterministic and cross-driver diff-clean. Grows monotonically; defaults
    /// empty, every non-demo profile unaffected. See <see cref="GameSession"/> for the entry detector.
    /// </summary>
    public ImmutableList<Guid> ZonesReached { get; init; } = ImmutableList<Guid>.Empty;

    /// <summary>The EnvironmentType marking the demo Crafting Table facility (Slice 7, journey 24:00).</summary>
    public const string CraftingTableEnvironment = "Crafting Table";

    /// <summary>The demo's Quiet Compass item name — its appearance fires CompassCrafted → Minimap.</summary>
    public const string QuietCompassItem = "Quiet Compass";

    /// <summary>
    /// The per-item property key a recipe-as-item carries to name the recipe it teaches on pickup
    /// (Slice 6). Data-driven like <see cref="FeatureSlotFrame.GlyphProperty"/>: the demo attaches it to
    /// the ruin's "another Quiet 1" card.
    /// </summary>
    public const string RecipeItemProperty = "Recipe";

    /// <summary>
    /// Fires a UI trigger, materializing any chrome it reveals. Returns the same instance when
    /// nothing changes (idempotent), so it never manufactures a spurious state change.
    /// </summary>
    public GameState FireUiTrigger(UiTrigger trigger)
    {
        var updated = Ui.Fire(trigger);
        return ReferenceEquals(updated, Ui) ? this : this with { Ui = updated };
    }

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
    /// The active bag at an arbitrary panel location, following that location's own breadcrumb trail
    /// (B, but also a C/W look-in panel showing a facility). Falls back to <see cref="ActiveBag"/> when
    /// the location is absent or its trail is broken. Used by the recipe menu, which can be opened on a
    /// facility whether the player has entered it (B) or peeked it (C).
    /// </summary>
    public Bag ActiveBagAt(LocationId loc)
    {
        var info = Locations.TryGet(loc);
        if (info is null) return ActiveBag;

        var bagId = info.BagId;
        foreach (var entry in info.Breadcrumbs.Reverse())
        {
            var bag = Store.GetById(bagId);
            if (bag is null) break;
            var cell = bag.Grid.GetCell(entry.CellIndex);
            if (cell.Stack?.ContainedBagId is not { } childId) break;
            bagId = childId;
        }
        return Store.GetById(bagId) ?? ActiveBag;
    }

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

    // ==================== Toolbar (fixed inventory) ====================

    /// <summary>
    /// The toolbar bag's Id, or null if no toolbar location is present. The toolbar is a real,
    /// depth-invariant fixed inventory (its own <see cref="LocationId.T"/> bag), unaffected by how
    /// deep the B cursor has navigated — the same slots are present at every depth.
    /// </summary>
    public Guid? ToolbarBagId => Locations.TryGet(LocationId.T)?.BagId;

    /// <summary>
    /// Store-aware acquisition of a single stack into the bag identified by <paramref name="bagId"/>,
    /// implementing the <b>bag-as-partial-empty</b> rule (Slice 3, beat 5): a cell holding a non-full
    /// plain bag counts as available space, and the item is then acquired <i>into</i> that bag
    /// (recursive placement — the carrying-capacity mechanic).
    ///
    /// Ordering (documented + tested): within each bag, true-empty cells and mergeable same-type
    /// stacks fill first (top-left, the classic <see cref="Grid.AcquireItems"/> pass); only then does
    /// any remainder descend into non-full <i>plain</i> sub-bags (top-left). "Plain" excludes facility
    /// and wilderness bags — you never dump loose items into a crafting station or an enter-only world.
    /// The recursion applies the same empties-first-then-partial-bags rule at every level.
    ///
    /// This lives on <see cref="GameState"/> (not <see cref="Grid"/>) because descent needs the
    /// <see cref="BagStore"/> to resolve a cell's <see cref="ItemStack.ContainedBagId"/> to its bag.
    /// The store-blind <see cref="Grid.AcquireItems"/> is left untouched, so every existing
    /// acquire/drop/sort path is byte-identical and non-demo behavior is unchanged.
    /// Returns the updated state and any portion that could not be placed anywhere.
    /// </summary>
    public (GameState State, ItemStack? Unplaced) AcquireIntoBagRecursive(Guid bagId, ItemStack stack)
    {
        var bag = Store.GetById(bagId);
        if (bag is null)
            return (this, stack);

        // Phase A — fill this bag's true-empty cells + mergeable same-type stacks (top-left).
        var (grid, unplaced) = bag.Grid.AcquireItems(new[] { stack });
        var state = this with { Store = Store.Set(bagId, bag with { Grid = grid }) };
        var remaining = unplaced.Count > 0 ? unplaced[0] : null;
        if (remaining is null)
            return (state, null);

        // Phase B — descend into non-full plain sub-bags (top-left), acquiring the remainder into them.
        for (int i = 0; i < grid.Cells.Length && remaining is not null; i++)
        {
            if (grid.Cells[i].Stack?.ContainedBagId is not { } childId)
                continue;
            if (state.Store.GetById(childId) is not { } child || !IsDescendableBag(child))
                continue;

            (state, remaining) = state.AcquireIntoBagRecursive(childId, remaining);
        }

        return (state, remaining);
    }

    /// <summary>
    /// A bag counts as available carrying space for the recursive acquire rule when it is a plain
    /// carrying bag — not a facility (crafting station) and not a wilderness (enter-only world). The
    /// <see cref="Bag.EnterOnly"/> guard (Slice 6) covers the demo's Quiet 1 wilderness, which uses a
    /// non-<see cref="IsWildernessType"/> env name but is still a world you never dump loose items into.
    /// </summary>
    public static bool IsDescendableBag(Bag bag) =>
        bag.FacilityState is null && !IsWildernessType(bag.EnvironmentType) && !bag.EnterOnly;

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
        var toolbarBag = new Bag(Grid.Create(10, 1), "Toolbar");

        var store = BagStore.Empty.Add(filledBag).Add(handBag).Add(toolbarBag);
        if (extraBags is not null)
            store = store.AddRange(extraBags);

        var locations = LocationMap.Create(handBag.Id, filledBag.Id)
            .Set(LocationId.T, Location.AtOrigin(toolbarBag.Id));

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

    // ==================== Panel management ====================

    /// <summary>
    /// Opens a bag as a Container panel (LocationId.C). The bag remains in its
    /// current grid cell — we're viewing it, not entering it via breadcrumbs.
    /// If C is already open with a different bag, it is replaced.
    /// </summary>
    public ToolResult OpenAsContainer(Guid bagId)
    {
        if (Store.GetById(bagId) is null)
            return ToolResult.Fail(this, "Bag not found");

        var newLocations = Locations.Set(LocationId.C, Location.AtOrigin(bagId));
        return ToolResult.Ok(this with { Locations = newLocations });
    }

    /// <summary>
    /// Opens a bag as a World panel (LocationId.W). The bag remains in its
    /// current grid cell — we're viewing it, not entering it via breadcrumbs.
    /// If W is already open with a different bag, it is replaced.
    /// </summary>
    public ToolResult OpenAsWorld(Guid bagId)
    {
        if (Store.GetById(bagId) is null)
            return ToolResult.Fail(this, "Bag not found");

        var newLocations = Locations.Set(LocationId.W, Location.AtOrigin(bagId));
        return ToolResult.Ok(this with { Locations = newLocations });
    }

    /// <summary>
    /// Closes a panel by removing its location. The bag data stays in the store.
    /// </summary>
    public ToolResult ClosePanel(LocationId panelId)
    {
        if (panelId is LocationId.H or LocationId.B)
            return ToolResult.Fail(this, $"Cannot close {panelId} panel");
        if (!Locations.Has(panelId))
            return ToolResult.Fail(this, $"Panel {panelId} is not open");

        return ToolResult.Ok(this with { Locations = Locations.Remove(panelId) });
    }

    /// <summary>The EnvironmentType marking the demo Shrine bag (Slice 5, journey 12:00).</summary>
    public const string ShrineEnvironment = "Shrine";

    /// <summary>
    /// Returns true if the given location is the EnvironmentType of a wilderness bag.
    /// Convention: wilderness bags have EnvironmentType containing nature-themed words.
    /// For now, checks if the bag's EnvironmentType is in a known set.
    /// </summary>
    public static bool IsWildernessType(string environmentType) =>
        environmentType is "Forest" or "Cave" or "Mountain" or "Ocean" or "Desert" or "Swamp";

    /// <summary>
    /// Returns true if the bag is a facility (has FacilityState).
    /// </summary>
    public static bool IsFacilityBag(Bag bag) =>
        bag.FacilityState is not null;

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
        // A slotted core is irreversible (Slice 5): a locked feature slot never releases its item.
        if (CurrentCell.IsLockedFeatureSlot)
            return ToolResult.Fail(this, "A slotted core cannot be removed");

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

        // Feature slot (Slice 5): a drop onto a Shrine slot is a slotting attempt, glyph-checked and
        // irreversible — routed away from the generic drop path (which never fills feature slots).
        if (cursorCell.Frame is FeatureSlotFrame slot)
            return ToolSlotCore(slot, cursorCell, handItems, activeBag, grid);

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
    /// Slots a core into a Shrine feature slot (Slice 5, journey 28:00). The slot accepts exactly one
    /// matching-glyph core (see <see cref="FeatureSlotFrame.Accepts(ItemStack)"/>); on success the core
    /// is placed and the frame LOCKS irreversibly (the click that can't be undone). Rejects with a
    /// failure — the world untouched — when the slot is already locked/filled, the hand holds more than
    /// a single unit, or the item's glyph doesn't fit (the wrong-item rejection the journey asserts).
    /// </summary>
    private ToolResult ToolSlotCore(FeatureSlotFrame slot, Cell cell, IReadOnlyList<ItemStack> handItems, Bag activeBag, Grid grid)
    {
        if (slot.IsLocked || !cell.IsEmpty)
            return ToolResult.Fail(this, "Feature slot is already filled");
        if (handItems.Count != 1 || handItems[0].Count != 1)
            return ToolResult.Fail(this, "Only a single core can be slotted");

        var core = handItems[0];
        if (!slot.Accepts(core))
            return ToolResult.Fail(this, "This item does not fit the slot's glyph");

        var lockedCell = cell with { Stack = core, Frame = slot with { IsLocked = true } };
        var newGrid = grid.SetCell(Cursor.Position, lockedCell);
        var emptyHand = CreateHandBag(HandBag.Grid.Columns);
        var newStore = Store
            .Set(ActiveBagId, activeBag with { Grid = newGrid })
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
        // A slotted core is irreversible (Slice 5): harvesting a locked feature slot is refused.
        if (cell.IsLockedFeatureSlot)
            return ToolResult.Fail(this, "A slotted core cannot be removed");
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
