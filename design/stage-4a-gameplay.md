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

Move hardcoded content definitions out of C# code and into data files:

**Current state (hardcoded):**
- RecipeRegistry: all recipes in C#
- FacilityBuilder: facility grid layouts in C#
- GameInitializer: starter items, facility types in C#
- WildernessGenerator templates: inline in GameInitializer

**Target state:**
- `/data/items/*.md` — item definitions (exists, keep as-is)
- `/data/recipes/*.md` — recipe definitions with facility type, inputs, outputs, duration
- `/data/facilities/*.md` — facility templates: grid size, slot layout, environment, recipes
- `/data/wilderness/*.md` — wilderness templates: biome, dimensions, density, loot table
- C# code only handles: loading/parsing data files, generator logic (random placement, unique bag creation), the factory functions that recipes reference by id

```
# data/recipes/stone-axe.md
# Stone Axe
Facility: Workbench
Duration: 3
Inputs: 5 Plain Rock, 3 Rough Wood
Output: 1 Stone Axe
```

```
# data/facilities/workbench.md
# Workbench
Category: Structure
Grid: 3x1
Slots: in1(Material), in2(Material), out1
Environment: Workbench
ColorScheme: Brown
```

- Generator-type outputs (bags, wilderness) use an output type reference: `Output: generate:belt-pouch` where the generator id maps to a C# factory registered at startup
- This keeps data files simple while allowing complex output logic

### 6. More Wilderness Content

- Multiple biome templates: Forest, Cave, Shore, Mountain
- Biome-specific loot tables with weighted drops
- Rarer resources only in specific biomes
- Wilderness bags obtainable via: Seedling Pot variants, random drops, crafting
- Wilderness refresh: optional slow respawn of resources over real-time ticks

## Implementation Order

1. **Data architecture** — Recipe/facility/wilderness data files + loaders (foundation for everything else)
2. **Multiple recipes per facility** — Adapt slot filtering, recipe selection UI
3. **Facility crafting** — Workshop meta-facility, facility-as-recipe-output
4. **Harvest tool** — Wilderness interaction, parent-bag acquisition
5. **Farming & real-time mode** — Farm bags, tick timer, mode toggle
6. **More wilderness** — Additional biomes, loot tables, refresh mechanics

## Architecture Notes

### Data Loading Pipeline

```
Startup:
  ItemTypeLoader.LoadFromDirectory("data/items/")
  → ImmutableArray<ItemType>

  RecipeLoader.LoadFromDirectory("data/recipes/", itemTypes, generatorRegistry)
  → ImmutableArray<Recipe>

  FacilityTemplateLoader.LoadFromDirectory("data/facilities/")
  → ImmutableArray<FacilityTemplate>

  WildernessTemplateLoader.LoadFromDirectory("data/wilderness/")
  → ImmutableArray<WildernessTemplate>

  GameInitializer.Create(itemTypes, recipes, facilityTemplates, wildernessTemplates)
  → GameState + GameSession
```

### Generator Registry

For recipe outputs that need code (bags with unique Ids, wilderness with RNG):

```csharp
GeneratorRegistry
├── Register(string id, Func<IReadOnlyList<ItemStack>> factory)
├── Get(string id) → Func<IReadOnlyList<ItemStack>>
```

Recipe data files reference generators by id. Static outputs (e.g. "1 Stone Axe") don't need a generator — the loader creates the output factory inline.

## Status

Proposed. Ready for planning breakdown and sub-task creation.
