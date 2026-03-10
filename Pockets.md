Claude: Only read #Design, #Content, and #Prototype headers
# Design
## Concept

A game where everything exists inside a grid based inventory, bags within bags within bags, and your own organization is the challenge.

## Design Ideas

Proposed feature ideas live in `/design/` as individual markdown files. See `/design/INDEX.md` for the full list with status. Ideas are moved into this document when approved and scheduled for a development stage.

## Mechanics

### Grids
- A grid is an X by Y collection of cells
- Cells are arranged row first then column 
	- Example: on a 10x4 grid
		- the 0th cell is (0,0)
		- the 12th cell is (1,2)
		- the last cell is (3,9)
### Cells
- Can hold one item stack
- Can have a filter that limits what categories of items are allowed
### Item Types
- Can be stackable or unique
- Unique items can only have 1 item in a cell but can have custom property values 
- Stackable items are all identical and cannot have custom properties
### Item Stacks
- An item type plus a count, indicating how many of that item
- The default max stack size for stackable items is 20
- When an item attempts to be added to a stack that is full, it creates a new stack instead
- An item stack that is placed on top of another adds the quantities together, yielding a remainder stack that is the number beyond the max stack size
### Bags
- Any item that can be opened and contains a grid
- Contains a reference to the grid and an array of item stacks, one index for each cell
- Each bag is also its own world inside, and has an environment type and color scheme
- Bags can be inside other bags
### Cursor
- On a grid, there is one Cursor that points to the bag and cell that indicates where the player currently is
- The bag that the Cursor points to is the active bag
- The cursor can be moved up, down, left, or right, and if there is no cell in that direction, the cursor wraps to the opposite side
### Selection
- In addition to the Cursor, any number of items/cells in the active bag can be selected.
- Selected cells have a brighter background color
- Actions take the list of the current Selection (cells and item stacks they contain) 
- If no cells are selected, the Cursor cell implicitly counts as selected

### Acquiring Items
- Placing item stacks in the current bag has a specific algorithm
	- The operation starts at the first cell (top left) and attempts to place the item stack
	- If the cell does not allow items of that category or the cell contains an item of a different item type, the cell is skipped
	- If the cell contains a stackable item of the type, a count is added to the existing cell up to the max stack count. The algorithm continues with the next cell with any remainder left unplaced
	- The algorithm continues until there are no items left to place
- Multiple stacks repeat this process for each acquired stack (restarting at the first cell)
### Breadcrumbs 
- Since bags can contain bags, the cursor is actually a stack of cursors, collectively called Breadcrumbs
- When the player leaves a bag, the Cursor stack is popped to set the active cursor
- The player cannot leave the top most bag
### Time
- Game time is based on wall clock time multiplied by simulation speed
- Simulation speed defaults to 1.0 
- A main game loop runs at 60 frames per second and calls any system-level update loops passing in the delta time since the last update in seconds
### Tools
- The player takes action through tools
- Each tool has attributes including speed,  which is how long it takes to use
- Tools have an associated action function that operates on the selection
- Tool Action functions either operate on the entire selection at once (e.g. Sort), or run on each individual item
- Some tools can only work on a single item and show an error in the action queue
#### Action Queue
- Each action takes an amount of game time based on the tool
- If the Action Function operates on individual items, then the tool speed is the speed per operation and each operation is added to a queue (example: 4 item stacks are selected and the player uses the "Harvest" tool which has a speed of 0.5 seconds, 4 actions are enqueued each of which take 0.5 seconds and they are run in sequence)
- The player can cancel the remaining queue at any time
#### Basic Tools
##### Grab
- Marks the selected items as being in the hand
- The hand can contain any number of marked item stacks
- Items marked as being in the hand are not selectable or interactable in any way, but they do still take up their slots
- Pressing grab again while items are in the hand cancels it (unmarking all items)
- This behavior is the standard "Cut" operation in most file browsers
##### Drop
- Takes items marked as from the hand and places them into the selection in order
- If there are not enough selected cells to drop all the items, the remainder are acquired as normal, outside the selection (see Acquiring Items above)
- If the selection contains items marked as in the hand, they are treated as empty slots for the purposes of this drop (and any acquired)
- This behavior is the standard "Paste" operation in most file browsers
##### Split
- Only works on a single item stack with more than 1 item count
- Splits the item stack into two stacks 
- Opens a modal dialog to adjust the amount in each stack and confirm or cancel
	- Defaults to splitting in half with any remainder on the left (example: 31 splits into 16/15)
##### Quick Split
- Like Split but no dialog is shown - uses the defaults (split in half)
##### Sort
- If the selection is the same as the cursor (one item stack), operates on the entire bag instead (as if selected)
- Sorts and merges items according to simple algorithm
	- Makes a list of all unique item types and sum of all counts of each item stack for that item definition
	- Empties all selected cells, adds them back from the list as per the Aquire algorithm limited to the selection cells in order of the (category, name) of the item definitions
	- Example: (10 Red Rock, 5 Red Rock, 11 Blue Rock, 10 Red Rock) becomes (11 Blue Rock, 20 Red Rock, 5 Red Rock)
##### Acquire Random
- A debugging utility tool
- Selected a random Item Type definition, and acquires 1 of that 
# Content
## Theme

 The game's overall theme is a whimsical fantasy world where all science and technical details can be described by magic
### Setting

- A fantasy Earth-like world
- When coming up with names and descriptions, use terms that might exist on any Earth-like world ("Rock", "Ocean", "Mist", "Grass", "Sun") but avoid using proper names of things specifically on Earth ("Lilac", "Limestone", "Europe", "Adriatic")
## Data

- Data files are markdown files stored in a /data subdirectory and are intended to be modifiable by humans and agents
- Each file represents the data for a specific definition
- All data files are loaded into the game's memory on startup
### Item Type Definitions

- Use a standard template for all item definition with optional sections being omittable
- (Required) Name
	- Should be 2-4 words long
	- The last word should be based on its primary function or noun
	- Words before that can add flavor or sub-categories to the function
	- Examples:
		- Plain Rock
		- Green Grass
		- Volcanic Hard Rock
		- Basic Conveyor
		- Advanced Transmogrifier
		- Dark Purple Large Bag
		- Gleaming Long Sword
- (Required) Category 
	- Categories are used by bag cell filters to determine whether items can be placed there
	- Examples: Material, Weapon, Structure, Medicine
- (Required) Whether the item is stackable or unique
	- If unique, list the properties it can have and their possible value ranges
- (Optional) Description should be 1-2 sentences describing the item

### Bag Templates

- Ignore for now

# Prototype

!!! Use this section when creating the initial CLAUDE.md file 

The prototype will be a TUI (Terminal UI) app. Eventually the game will be built on top of Unity3D, Godot, or another C# game engine. However, the iteration time of a console TUI app is considerably faster. Code should be written to use C# 9 targeting .NET Standard 2.1 where possible, but if there is a much better path (library support) using up to C# 12, .NET 8 I'm willing to entertain It. 

The goal of the prototype is to explore a variety of game mechanics that could exist within this inventory metaphor. 
## Planning Rules

!!! IMPORTANT

- Plan out all features ahead of time, asking for any ambiguities
- Document feature plans with an ASCII diagram where possible
- Break implementation for features into sub-tasks and testing plans
## Development Rules

!!! IMPORTANT

- Use test-driven development for all basic functions
- For User Experience related features, test by running the game in another terminal
	- Automate by sending key and mouse signals to the game (create web-socket API to automate if the terminal functions cannot be easily emulated)
	- Take screen or buffer grabs to evaluate for correctness
- Use sub-agents (using Sonnet model) to implement tests and function code
- Add inline code documentation above each function describing how the function is to be used
- Use of functional programming style and LINQ is preferred but do not use LINQ Query syntax (from, select, where) - use Method Syntax instead (".Where()", ".Select()")
- Stop at the end of each Development Stage for me to test and keep Stage builds and task lists in separate sub-folders (so that I may always revisit an earlier stage)
- Use git to commit changes after each successful build->test->run cycle (known good builds)

## User Experiences

### User Interface

- User Interface is split into 4 panel sections:
	- Top: contains always-available game settings and options as a menu
	- Bottom: Contains the current toolbar and hotkeys as well as a status bar
	- Left: The current bag inventory as a grid and the breadcrumbs
	- Right: The current world visualization and action queue

Example Layout (entire screen):
```
--------------------------------------------------
| Top                                            |
--------------------------------------------------
| [ Breadcrumbs           ] | [ Action Queue   ] |
|                           |                    |
|                           |                    |
| Active Grid               | World              |
|                           |                    |
|                           |                    |
|                           |                    |
| Cursor Item Name          |                    |
|   and Description         |                    |
--------------------------------------------------
| Bottom  [Toolbar                      ]        |
| Status                                         |
--------------------------------------------------
```

#### Modal Dialogs
### Input

## Development Stages

Development should be done in stages, and should be fully functional at the end of each stage. 
### Stage 1
- A single bag with an 8x4 grid and a moveable cursor 
- Arrow keys move the cursor (including wrap-around)
- Only single item stack selection is supported 
- A small number (15) of item type definitions (/data) will be randomly created (as part of the build files)
- The game inventory on start up will have 4-10 stacks of randomly selected count and item type definitions from the data list
- Hitting Ctrl-Q quits the game

### Stage 2

#### Hand as Hidden Bag
- Hand becomes a real Bag (not visible in the root bag) instead of position markers
- Grab removes items from the source bag and places them in the Hand bag (true cut)
- Drop takes items from the Hand bag and places them at cursor / via Acquire
- A single GameState transition covers both the source bag mutation and hand bag mutation (atomicity via immutable swap)
- Hand bag renders as an overlay or indicator, not as a navigable bag

#### Modal Split
- Key: Shift-3
- Opens a popup dialog with a slider to adjust split amounts
- Defaults to half (remainder on left, e.g. 31 → 16/15)
- OK confirms and places the right portion in the Hand bag
- Cancel returns to previous state
- Only works on a single stack with count > 1

#### Undo and Action Log
- Undo: Ctrl-Z
- State snapshot undo — push entire GameState onto an ImmutableStack before each action
- Max depth: 1000 (effectively unlimited given low memory cost of shared immutable structure)
- Action Log: visible panel showing human-readable history of operations
  - e.g. "Grabbed 5 Plain Rock from (2,3)", "Sorted bag", "Undo: Sorted bag"
  - Primarily for debugging and visual feedback in Stage 2

#### Bag Navigation
- Open bag: E — if cursor cell contains a bag item, push current cursor onto breadcrumb stack and enter the bag's grid
- Go back: Q — pop breadcrumb stack, return to parent bag. Disabled at root bag
- Breadcrumb trail displayed above the grid showing bag path

#### Wilderness Bags (Minimal)
- See `/design/wilderness-bags.md` for full design
- Stage 2 scope: one wilderness template (e.g. "Forest"), manually placed in starting inventory
- Wilderness bag contains a randomly generated grid of harvestable resource items
- Harvest tool (new, key 6): per-item action, removes item from wilderness cell, acquires into parent bag
- No bag lifecycle/depletion tracking yet — just enter, harvest, leave

#### New/Changed Hotkeys

| Key | Action |
|-----|--------|
| Arrows | Move cursor |
| 1 | Grab (true cut to Hand bag) |
| 2 | Drop (from Hand bag to cursor/acquire) |
| 3 | Quick Split (right half goes to Hand bag) |
| Shift-3 | Modal Split (dialog, right portion to Hand bag) |
| 4 | Sort |
| 5 | Acquire Random (debug) |
| 6 | Harvest (wilderness only) |
| E | Open bag at cursor |
| Q | Go back up (pop breadcrumb) |
| Ctrl-Z | Undo |
| Ctrl-Q | Quit |

### Stage 3

#### UI/UX Improvements (done)
- WASD + arrow keys for cursor movement
- Unified Primary/Secondary input model (Factorio-style contextual grab/drop/swap/merge)
- Mouse support: left-click = Primary, right-click = Secondary (click-only, no drag)
- Back button cell (clickable, left of grid) — dims when at root
- Hand cell display (right of grid, category-colored border, cyan text when holding)
- Category-colored cell borders (Material=gray, Weapon=red, Structure=brown, Medicine=green, Tool=blue, Bag=magenta, Consumable=cyan), black background throughout
- Mouse state LED debug indicators on input status bar
- Border foreground reserved for CellFrame rendering

#### CellFrame (done)
- See `/design/cell-frames.md` for full design
- `CellFrame` sealed abstract record: `InputSlotFrame(SlotId, Filter?, IsLocked)` and `OutputSlotFrame(SlotId, IsLocked)`
- `Cell` gains `CellFrame? Frame` field; `IsInputSlot`, `IsOutputSlot`, `HasFrame` convenience properties
- `Cell.Accepts()` checks both `CategoryFilter` and `InputSlotFrame.Filter`
- Rendering: frame-specific border foreground colors (yellow=input slots, green=output slots)
- Empty cells with frames still render the frame border (visible slot affordance)

#### BagRegistry (done)
- `BagRegistry` class: BFS-built `ImmutableDictionary<Guid, Bag>` from root + hand bag
- Accessible via `GameState.Registry` computed property (rebuilt each access; no caching due to `with` copy semantics)
- `GetById(Guid)`, `Contains(Guid)`, `All`, `Facilities` (bags with non-null FacilityState), `Count`
- Foundation for tick dispatch and future subscription-based patterns (event listeners, dirty tracking)

#### FacilityState & Bag Updates (done)
- `FacilityState` record: `RecipeId?`, `Progress`, `IsActive` — optional field on `Bag`
- `GameState.ReplaceBagById(Guid, Bag)` — DFS find-and-replace anywhere in the bag tree, rebuilds parents on the way up

#### Tick System (done — minimal)
- See `/design/tick-system.md` for full design
- Action-based ticks only: 1 tick per undoable action (no wall-clock, no hierarchy)
- `int TickCount` on `GameSession`, incremented in `ApplyResult` after each successful state change
- `GameSession.TickFacilities()` iterates `Registry.Facilities`, calls `FacilityLogic.Tick()` per facility, propagates changes via `ReplaceBagById`
- No hierarchical propagation or TimeTransform yet

#### Crafting (done — minimal)
- See `/design/crafting.md` and `/design/bag-crafting.md` for full designs
- `Recipe` record: `Id`, `Name`, `Inputs` (RecipeInput[]), `OutputFactory` (Func<IReadOnlyList<ItemStack>>), `Duration` (int ticks)
- `OutputFactory` is a function so each invocation produces unique items (bags with fresh Ids, procedural content)
- `FacilityLogic` static class: `FindMatchingRecipe`, `RecipeMatches`, `Tick`, `GetInputStacks`, `OutputSlotsEmpty`
- Tick cycle: no inputs → idle; inputs match recipe → start (set RecipeId, Progress=1); each tick → Progress++; Progress >= Duration → consume inputs, call OutputFactory, place in output slot, reset
- If inputs removed mid-craft → reset to idle
- `RecipeRegistry`: hardcoded Stage 3 recipes, keyed by facility EnvironmentType prefix
- `FacilityBuilder`: creates facility bags with InputSlot/OutputSlot CellFrames
- `GameSession.New(state, recipes)` overload; recipes stored on session, dispatched per-tick

##### Facilities and Recipes
1. **Workbench** (1×3 grid: 2 input, 1 output, EnvironmentType="Workbench")
   - Recipe `workbench_axe`: Plain Rock ×5 + Rough Wood ×3 → Stone Axe (duration: 3 ticks)
2. **Tanner** (1×3 grid: 2 input, 1 output, EnvironmentType="Tanner")
   - Recipe `tanner_pouch`: Tanned Leather ×3 + Woven Fiber ×2 → Belt Pouch bag (2×3 grid, duration: 5 ticks)
3. **Seedling Pot** (1×3 grid: 2 input, 1 output, EnvironmentType="Seedling Pot")
   - Recipe `seedling_forest`: Forest Seed ×5 + Rich Soil ×3 → Forest Wilderness Bag (6×4, duration: 8 ticks)

##### New Item Types (in `/data/`)
- Tanned Leather (Material, stackable)
- Forest Seed (Material, stackable)
- Rich Soil (Material, stackable)
- Stone Axe (Tool, unique)

##### Stage 3 Game Initialization
- `GameInitializer.CreateRandomStage3Game`: 8×4 root bag with 3 facility items (Workbench, Tanner, Seedling Pot), 1 Forest Wilderness Bag, and starter crafting materials (8 Rock, 5 Wood, 4 Leather, 3 Fiber, 6 Seed, 4 Soil)
- `Program.cs` builds recipes via `RecipeRegistry.BuildRecipes` and passes to `GameView`

##### Test Coverage
- 309 total tests (up from 263 at Stage 2 end)
- `CellFrameTests` (15): frame types, Cell integration, pattern matching, structural equality
- `BagRegistryTests` (10): BFS traversal, nested/deep nesting, facilities filter, GameState integration
- `FacilityLogicTests` (18): recipe matching, tick start/progress/completion, excess inputs, mid-craft reset, output factory uniqueness
- `CraftingIntegrationTests` (3): ReplaceBagById, tick counting, full end-to-end Workbench→Stone Axe cycle through GameSession

#### Hotkeys

| Key | Action |
|-----|--------|
| Arrows / WASD | Move cursor |
| 1 / E / Left-click | Primary: contextual grab/drop/swap/merge/enter/harvest |
| 2 / Right-click | Secondary: grab half / place one |
| Shift-3 | Modal Split (dialog) |
| 4 | Sort |
| 5 | Acquire Random (debug) |
| Q / Back button | Go back up (pop breadcrumb) |
| Ctrl-Z | Undo |
| Ctrl-Q | Quit |

#### Known Limitations / Future Work
- OutputSlot grab-only behavior not yet enforced (Primary still drops into output slots)
- No crafting progress UI inside facility bags (player can't see "2/3 ticks done")
- Recipes are hardcoded in `RecipeRegistry`, not loaded from data files
- No hierarchical tick propagation or TimeTransform
- `BagRegistry` rebuilt on every access (fine for current bag counts, may need caching later)
- Mouse events don't work in WSL terminal — must run native Windows binary

### Stage 4

- TBD

### Future Stages

- See `/design/INDEX.md` for proposed feature ideas

# Notes

Claude: Do not read this section

Bag for controller mapping
Bags of random wilderness
Make Bags, starting with small
Find special Bags
Find recipes?

Tools
Have speed setting 
Send tools out on their own
Bags are portals 
Select all of type 
Select all of category
Select all
Select row
Select rect

Windows -> mapped zones
Sorter 
Conveyor 
Frames
##### Sort
##### Split



#### Harvesting Tools

#### Selection Tools


Worlds

Wilderness Worlds

Crafting

Recipes