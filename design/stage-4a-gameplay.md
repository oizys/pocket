# Stage 4a: Gameplay Expansion & Data Architecture

## Concept

Deepen the gameplay loop with harvest tools, multi-recipe facilities, facility crafting, farming, and real-time mode — while cleaning up the data/init layer so game content is defined in data files rather than hardcoded C#.

## Features

### 1. Harvest Tool & Wilderness Interaction

Currently wilderness bags are just pre-populated grids. The Harvest tool gives them purpose:

```
Wilderness cell: [Oak Log ×3]
Player uses Harvest tool → timer ticks down → items removed from wilderness cell
→ AcquireItems into parent bag (via breadcrumb)
```

- New tool key (6 or H): Harvest
- Per-item action with speed varying by resource type (some items harvest faster)
- Harvested items flow to **parent bag** (breadcrumb peek), not into the wilderness grid
- Wilderness bag tracks depletion: when all harvestable cells are empty, bag is "Depleted"
- Ties into tick system: each harvest action consumes N ticks

### 2. Multiple Recipes Per Facility

Currently each facility type has exactly one recipe. Expand to multiple:

```
Workbench recipes:
  - Stone Axe:  5 Plain Rock + 3 Rough Wood → 1 Stone Axe (3 ticks)
  - Stone Bench: 8 Plain Rock + 2 Rough Wood → 1 Stone Bench (5 ticks)
  - Wooden Shield: 6 Rough Wood + 2 Woven Fiber → 1 Wooden Shield (4 ticks)
```

- **Recipe selector toggle (decided)**: Each facility has one "active" recipe. Player cycles through available recipes with a key (e.g. R). Switching the active recipe:
  1. Dumps all input/output slot contents back to parent bag (via AcquireItems on parent)
  2. Resets FacilityState (progress = 0, recipeId = new recipe)
  3. Updates input slot ItemTypeFilters to match the new recipe's inputs
- This keeps per-slot ItemTypeFilter (each slot accepts exactly one item type for the active recipe)
- Explicit setup is important for future automation — an automator can target "set active recipe" as an action
- FacilityState gains `ActiveRecipeId` (which recipe is selected, even before inputs are loaded) vs `CraftingRecipeId` (which recipe is actively crafting, set when inputs are sufficient)

### 3. Facility Crafting (The Meta-Facility)

A "Workshop" or "Foundry" is the starting facility that can craft other facilities:

```
┌─────────────────────────────────────┐
│  Workshop (starter facility)        │
│  Recipes:                           │
│    8 Plain Rock + 4 Rough Wood      │
│      → Workbench (5 ticks)          │
│    6 Tanned Leather + 3 Woven Fiber │
│      → Tanner (5 ticks)             │
│    4 Rich Soil + 2 Rough Wood       │
│      → Seedling Pot (4 ticks)       │
│    10 Iron Ore + 6 Rough Wood       │
│      → Forge (8 ticks)              │
└─────────────────────────────────────┘
```

- Player starts with a Workshop instead of pre-built facilities
- Workshop crafts facility items (unique items with ContainedBag = the facility bag)
- Facility recipes produce fresh facility bags via output factory functions
- This bootstraps the progression: Workshop → Workbench → tools; Workshop → Tanner → bags; etc.

### 4. Farming & Real-Time Mode

Farming is wilderness-in-reverse: the player plants items and they grow over time.

- **Farm Bag**: A facility-like bag where input slots accept seeds/soil, and output appears after a long tick duration
- **Real-time toggle**: GameSession gains a mode flag. In real-time mode, ticks advance on a timer (configurable seconds-per-tick). In action mode (current), ticks advance on player actions.
- UI shows current mode and tick rate
- Key binding: Tab or similar to toggle modes
- Real-time mode makes farming/long crafts viable without spamming actions

### 5. Data-Driven Content

Move hardcoded content definitions out of C# code and into data files.

#### Design Decisions

**File format:** Markdown with typed headers. `# Type: Id` marks the start of each definition block. Multiple definitions can coexist in one file (package-style) or live in separate files. The loader recursively scans all `.md` files under `data/`, splits on typed headers, and routes each block to the correct parser. Directory structure is arbitrary — purely human convenience.

**Definition types and their headers:**
- `# Item: Plain Rock` — item type definition
- `# Recipe: stone-axe` — recipe with grid/slot layout, inputs, outputs, duration
- `# Facility: Workbench` — bag + timer + recipe selector (lists recipe ids it can run)
- `# GridTemplate: belt-pouch-2x3` — reusable bag layout (dimensions, environment, color, cell frames)
- `# LootTableTemplate: forest-materials` — weighted item list for generators

**Recipes own the slot layout.** A recipe defines its grid dimensions, input slot positions/filters, and output slot positions. Facilities are generic containers — just a bag, timer, and recipe selector. When the active recipe changes, the facility grid is rebuilt from the recipe's layout.

**Facilities own their recipe list.** A facility definition lists the recipe ids it supports. This allows the same recipe to appear in multiple facility types.

#### Output Pipeline Syntax

Recipe outputs use a composable pipeline. Operations chain left-to-right with `->`:

| Syntax | Type | Behavior |
|--------|------|----------|
| `3 Plain Rock` | Static | Produces ItemStack(s) directly |
| `@forest` | Template ref | Looks up a parsed template by id, produces a PipelineValue |
| `!wilderness` | Generator ref | Code function, receives previous pipeline value, produces next |

Examples:
```
Output: 1 Stone Axe                              ← pure static
Output: @forest -> !wilderness                   ← template → generator
Output: @belt-pouch-2x3 -> !bag                  ← grid template → bag generator
Output: @forest -> !wilderness -> !shuffle        ← template → generate → transform
Output: 1 Belt Pouch -> !attach-bag(@belt-pouch)  ← static → attach generated bag
```

**PipelineValue** is a discriminated union:
```
PipelineValue
├── TemplateValue(string Id, object Template)     ← GridTemplate, LootTableTemplate, etc.
├── StacksValue(IReadOnlyList<ItemStack> Stacks)  ← produced items
├── BagValue(Bag Bag)                             ← intermediate bag
```

Each generator declares accepted input type and output type. Pipeline type-checks at load time (resolve phase), not runtime. Generators are the only C# code — everything else is data.

#### Example Package File

```markdown
# Item: Workbench
**Category**: Structure
**Stackable**: No
A crafting station for basic tools and structures.

# Facility: Workbench
**Environment**: Workbench
**ColorScheme**: Brown
**Recipes**: stone-axe, stone-bench

# Recipe: stone-axe
**Name**: Stone Axe
**Duration**: 3
**Grid**: 3x1
**Input 1**: Plain Rock ×5
**Input 2**: Rough Wood ×3
**Output**: 1 Stone Axe

# Recipe: stone-bench
**Name**: Stone Bench
**Duration**: 5
**Grid**: 3x1
**Input 1**: Plain Rock ×8
**Input 2**: Rough Wood ×2
**Output**: 1 Stone Bench
```

#### Example Wilderness Package

```markdown
# Item: Forest Bag
**Category**: Bag
**Stackable**: No
A bag containing a small forest wilderness.

# LootTableTemplate: forest-materials
**Items**: Plain Rock ×2.0, Rough Wood ×3.0, Forest Seed ×0.5
**FillRatio**: 0.6

# GridTemplate: forest-6x4
**Columns**: 6
**Rows**: 4
**Environment**: Forest
**ColorScheme**: Green

# Recipe: seedling-forest
**Name**: Forest Bag
**Duration**: 8
**Grid**: 3x1
**Input 1**: Forest Seed ×5
**Input 2**: Rich Soil ×3
**Output**: 1 Forest Bag -> !wilderness(@forest-6x4, @forest-materials)
```

### 6. More Wilderness Content

- Multiple biome templates: Forest, Cave, Shore, Mountain
- Biome-specific loot tables with weighted drops
- Rarer resources only in specific biomes
- Wilderness bags obtainable via: Seedling Pot variants, random drops, crafting
- Wilderness refresh: optional slow respawn of resources over real-time ticks

## Implementation Order

1. **Data architecture** — PipelineValue DU, header parser, content registry, loaders, migrate existing data
2. **Multiple recipes per facility** — Recipe selector, grid rebuild on switch, slot dump to parent
3. **Facility crafting** — Workshop meta-facility, facility-as-recipe-output
4. **Harvest tool** — Wilderness interaction, parent-bag acquisition
5. **Farming & real-time mode** — Farm bags, tick timer, mode toggle
6. **More wilderness** — Additional biomes, loot tables, refresh mechanics

## Architecture Notes

### Loading Pipeline

```
Phase 1 — Parse (directory-agnostic):
  Scan all .md files under data/ recursively
  Split each file on "# Type: Id" headers
  Route each block to type-specific parser
  → Raw definitions (no cross-references resolved yet)

Phase 2 — Resolve (cross-reference):
  Build ItemType dictionary (name → ItemType)
  Resolve recipe inputs (item names → ItemTypes)
  Resolve recipe outputs (parse pipeline, validate types)
  Resolve facility recipe lists (recipe ids → Recipe refs)
  Resolve template refs in pipelines (template ids → parsed templates)
  → ContentRegistry (fully resolved, ready for game init)

Phase 3 — Initialize:
  Register generators in code (wilderness, bag, shuffle, attach-bag, etc.)
  GameInitializer.Create(contentRegistry) → GameState + GameSession
```

### ContentRegistry

```csharp
ContentRegistry
├── Items: ImmutableDictionary<string, ItemType>
├── Recipes: ImmutableDictionary<string, Recipe>
├── Facilities: ImmutableDictionary<string, FacilityTemplate>
├── GridTemplates: ImmutableDictionary<string, GridTemplate>
├── LootTableTemplates: ImmutableDictionary<string, LootTableTemplate>
├── Generators: ImmutableDictionary<string, Generator>
```

### Generator Interface

```csharp
Generator
├── Id: string
├── InputType: Type (typeof(PipelineValue) subtype it accepts, or null for no input)
├── OutputType: Type (typeof(PipelineValue) subtype it produces)
├── Execute(PipelineValue? input) → PipelineValue
```

### Built-in Generators

| Id | Input | Output | Behavior |
|----|-------|--------|----------|
| `wilderness` | GridTemplate + LootTableTemplate | BagValue | Creates wilderness bag with random loot |
| `bag` | GridTemplate | BagValue | Creates empty bag from template |
| `shuffle` | StacksValue or BagValue | same | Randomizes item positions |
| `attach-bag` | StacksValue + GridTemplate arg | StacksValue | Sets ContainedBag on first stack's item |

## Status

Proposed. Ready for planning breakdown and sub-task creation.
