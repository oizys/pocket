# Game Mechanics Reference

## GameKey Enum (Logical Input)
```
Up, Down, Left, Right    — cursor movement (not undoable, no tick)
Primary                  — context-sensitive: grab/drop/swap/merge/enter bag/harvest
Secondary                — half-grab or place-one
QuickSplit               — split stack in half, right to hand
Sort                     — sort & merge entire bag
CycleRecipe              — cycle facility to next recipe
LeaveBag                 — pop breadcrumb, return to parent
Undo                     — restore previous state
AcquireRandom            — debug: add random item
```

## Grid Coordinate System
- Row-major: on an 8×4 grid, cell index 12 = row 1, col 4
- `Position(Row, Col)` — zero-indexed
- `Position.FromIndex(index, columns)` → Position
- `position.ToIndex(columns)` → int
- Root bag is always 8×4. Facility grids are typically 3×1.

## ExecutePrimary Context Rules
Inside `ToolPrimary()`:
1. **Output slot + occupied + empty hand → Grab** (always, even for bag-type items)
2. Cell has bag → **EnterBag** (always, even with hand full)
3. Nested + empty hand + occupied cell → **Harvest** (move to parent bag)
4. Empty hand + occupied cell → **Grab** (full stack to hand)
5. Hand full + empty cell → **Drop** (all from hand)
6. Hand full + same type → **Merge/Drop** (stack, overflow stays in hand)
7. Hand full + different type → **Swap**

## Facility Grid Layout
Standard facility grid is 3×1:
```
[Input 0] [Input 1] [Output 0]
```
- Input slots have `InputSlotFrame` with `ItemTypeFilter` (only accepts specific item type)
- Output slots have `OutputSlotFrame`
- Grid dimensions come from the recipe's `GridColumns`/`GridRows`

## FacilityState Lifecycle
```
FacilityState(RecipeId?, Progress, IsActive, ActiveRecipeId?)
```
1. **Idle:** `ActiveRecipeId` set (which recipe is selected), `RecipeId` null
2. **Crafting starts:** When inputs match recipe → `RecipeId` set, `Progress = 1`
3. **Each tick:** `Progress++`
4. **Completion:** `Progress >= Duration` → inputs consumed, outputs placed, `RecipeId` reset to null
5. **Recipe cycling:** `ExecuteCycleRecipe()` dumps items, rebuilds grid, advances `ActiveRecipeId`

## Tick Modes
- **Rogue (`TickMode.Rogue`):** Every undoable action (grab, drop, sort, etc.) fires one facility tick. Cursor movement does NOT tick.
- **Realtime (`TickMode.Realtime`):** Ticks only via explicit `session.Tick()` calls.

## Bag Navigation
- `ExecuteEnterBag()` / `ExecutePrimary()` on bag cell → push breadcrumb, cursor to (0,0)
- `ExecuteLeaveBag()` → pop breadcrumb, restore parent cursor
- `session.Current.IsNested` → true when inside any nested bag
- `session.Current.ActiveBag` → the bag currently being viewed (follows breadcrumbs)
- `session.Current.RootBag` → always the top-level bag

## Hand Model
- `GameState.HandBag` — a real Bag with 1 slot (default)
- `HasItemsInHand` / `HandItems` — check/read hand contents
- Grab = cut from grid to hand; Drop = paste from hand to grid
