# Agent Patterns Reference

## NavigateTo Algorithm
Move cursor step-by-step from current position to target, choosing shortest path (wrap-aware):

```csharp
private static GameSession NavigateTo(GameSession session, Position target)
{
    var current = session.Current.Cursor.Position;
    var grid = session.Current.ActiveBag.Grid;

    while (current.Row != target.Row)
    {
        var rowDist = target.Row - current.Row;
        if (rowDist < 0) rowDist += grid.Rows;
        session = rowDist <= grid.Rows / 2
            ? session.MoveCursor(Direction.Down)
            : session.MoveCursor(Direction.Up);
        current = session.Current.Cursor.Position;
    }

    while (current.Col != target.Col)
    {
        var colDist = target.Col - current.Col;
        if (colDist < 0) colDist += grid.Columns;
        session = colDist <= grid.Columns / 2
            ? session.MoveCursor(Direction.Right)
            : session.MoveCursor(Direction.Left);
        current = session.Current.Cursor.Position;
    }
    return session;
}
```

## Find Helpers

```csharp
// Find item by type in active bag
Position? FindItemInGrid(GameState state, ItemType itemType)
// Find item with minimum count
Position? FindItemInGrid(GameState state, ItemType itemType, int minCount)
// Find facility by environment type in root grid
Position? FindFacilityInGrid(GameState state, string envType)
// Find first empty cell
Position? FindEmptyCell(GameState state)
```

## Grab → Deliver → Craft Pipeline

### Step 1: Grab with exact count
```csharp
// If stack has more than needed, use ModalSplit to take exact amount
session = NavigateTo(session, itemPos);
if (stack.Count > needed)
    session = session.ExecuteModalSplit(stack.Count - needed); // keep remainder, take needed
else
    session = session.ExecutePrimary(); // grab all
```

### Step 2: Deliver to facility slot
```csharp
session = NavigateTo(session, facilityPos);
session = session.ExecutePrimary();       // enter facility
session = NavigateTo(session, slotPos);   // navigate to input slot
session = session.ExecutePrimary();       // drop
session = session.ExecuteLeaveBag();      // leave facility
```

### Step 3: Tick until complete
```csharp
// Each ExecuteSort generates a tick in Rogue mode
for (int i = 0; i < maxTicks; i++)
{
    var facility = session.Current.Registry.Facilities
        .FirstOrDefault(f => f.EnvironmentType == envType);
    if (facility?.FacilityState?.RecipeId is null)
        return session; // craft complete
    session = session.ExecuteSort();
}
```

### Step 4: Extract output
```csharp
session = NavigateTo(session, facilityPos);
session = session.ExecutePrimary();  // enter
// Find output slot with item
session = NavigateTo(session, outputPos);
session = session.ExecuteGrab();     // grab (not ExecutePrimary — avoids entering bag outputs)
session = session.ExecuteLeaveBag();
// Drop in empty root cell
session = NavigateTo(session, emptyPos);
session = session.ExecutePrimary();  // drop
```

## Recipe Cycling
```csharp
session = NavigateTo(session, facilityPos);
session = session.ExecutePrimary(); // enter
while (session.Current.ActiveBag.FacilityState?.ActiveRecipeId != targetRecipeId)
    session = session.ExecuteCycleRecipe();
session = session.ExecuteLeaveBag();
```

Note: CycleRecipe dumps all items from slots to root bag and rebuilds the facility grid.

## Wilderness Harvesting
```csharp
// Enter wilderness bag
session = NavigateTo(session, forestBagPos);
session = session.ExecutePrimary(); // enter

// Find and harvest items (ExecutePrimary on non-bag cells in nested bag = harvest)
var itemPos = FindItemInGrid(session.Current, targetType);
session = NavigateTo(session, itemPos.Value);
session = session.ExecutePrimary(); // harvests to parent bag

session = session.ExecuteLeaveBag();
```

## WebSocket JSON Commands (Live Mode)
```json
{"type": "key", "key": "Up"}
{"type": "key", "key": "Primary"}
{"type": "key", "key": "CycleRecipe"}
{"type": "key", "key": "LeaveBag"}
```

## Common Gotchas
- **Cursor movement doesn't tick** — only undoable actions (grab, drop, sort, etc.) generate ticks
- **Output slots always grab** — `ToolPrimary` on an `OutputSlotFrame` forces a grab, even for bag-type items (Forest Bag, Belt Pouch, etc.)
- **CycleRecipe dumps items** — any items in facility slots get dumped to root bag when cycling
- **ModalSplit(leftCount)** — leftCount is what STAYS in the cell; the remainder goes to hand
- **InputSlotFrame filters** — facility input slots only accept their specific ItemType; drop will fail on wrong type
