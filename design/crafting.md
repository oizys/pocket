# Crafting: Recipes, Facilities, and Timed Production

## Concept

Crafting converts input materials into output products via Recipes executed at Facilities. A Recipe defines input ItemStacks, output ItemStacks, and a time cost. A Facility is a special bag-like item with input slots, output slots, a recipe selector, and a production queue. Crafting provides material sinks (consuming base resources) and sources (producing higher-order items), creating compounding progression loops.

Facilities are "magic bags" — they have structured slot layouts (inputs, outputs, recipe) rather than free-form grids. The player loads inputs, selects a recipe, and the facility produces outputs over time. Multiple facilities running in parallel reward investment in infrastructure.

## Cohesion: Very High

Facilities are bags with constrained grids (input/output slots). Recipes are data (lists of ItemStacks). The crafting loop uses existing mechanics: AcquireItems for output placement, item removal for input consumption. Cell Frames could define input-only and output-only slots via FilterFrames. Time cost introduces a new dimension but maps cleanly to an action queue or tick system.

## Intuition: Very High

Universal game pattern. "Put rocks and wood in the workbench, wait, get a stone bench." Minecraft furnaces, Factorio assemblers, Stardew kegs — players know this. The bag metaphor adds a twist: a facility IS a bag you can open, see the slots, and interact with using existing tools.

## Architecture

### Recipe

```
Recipe
├── Id: string
├── Name: string
├── Inputs: ImmutableArray<ItemStack>        (required materials)
├── Outputs: ImmutableArray<ItemStack>       (produced items)
├── Duration: int                            (time units)
└── FacilityType: string?                    (which facility types can run this, null = any)
```

### Facility

A Facility is a unique item (Category.Bag or a new Category.Facility) with a ContainedBag whose grid is structured:

```
FacilityState
├── RecipeId: string?                        (selected recipe, null = none)
├── CraftingProgress: int                    (0 = idle, >0 = ticks remaining)
├── IsEnabled: bool                          (on/off — auto-start when possible)
├── InputSlots: Grid region                  (cells filtered to accept recipe inputs)
├── OutputSlots: Grid region                 (cells for produced items)
```

Alternatively, FacilityState could be a record on the Bag itself (extending Bag with optional facility data), or it could be modeled entirely through Cell Frames (input frames, output frames, recipe frame).

### Production Loop

```
Idle + Enabled + Inputs Sufficient + Output Has Room
    → Consume inputs, set CraftingProgress = Recipe.Duration
    → Tick down CraftingProgress each game tick
    → When CraftingProgress reaches 0:
        → Place outputs in output slots
        → Return to Idle
        → Check if another craft is possible

Idle + Enabled + Output Full
    → Stay idle (don't consume inputs)

Crafting + Cancel
    → Return consumed inputs to input slots
    → Set CraftingProgress = 0, return to Idle

Facility Destroyed/Removed
    → Cancel any active craft
    → Acquire all input + output contents into parent bag
```

### Facility as Bag

When a player enters a facility (key E), they see a structured grid:

```
┌──────────────────────────────┐
│ [Recipe: Stone Bench       ] │
├──────────────────────────────┤
│ IN:  [Rck3] [Wod2] [    ]   │
│ OUT: [    ] [    ]           │
│ Status: Crafting... 3s       │
└──────────────────────────────┘
```

Input/output cells use FilterFrames (from Cell Frames design) to constrain what can be placed. The player uses normal Grab/Drop to load inputs and collect outputs.

### Recipe Ownership

Two models:

1. **Facility-owned**: each facility has a fixed list of recipes. Simple, discoverable — "what can this workbench make?" Player progression comes from finding/building new facility types.

2. **Player-learned + facility intersection**: player learns recipes globally, facilities have a type filter. More RPG-like — "I know the recipe, I just need the right workbench." Adds a knowledge progression axis.

**Recommendation**: Start with facility-owned (simpler). Add player-learned recipes later if the game needs a knowledge progression system. The data model supports both — recipes have a `FacilityType` field either way.

### Data Model

Recipes stored as markdown data files in `/data/recipes/`:

```markdown
# Stone Bench
**Facility**: Workbench
**Duration**: 10
**Inputs**: 3 Plain Rock, 2 Rough Wood
**Outputs**: 1 Stone Bench
```

## Methodology Fit

- **Builds on:** bags (facilities are bags), Cell Frames (input/output slot constraints), items, AcquireItems algorithm
- **New friction:** time cost, material requirements, facility placement/management
- **Reduces friction:** transforms abundant base materials into useful items (gives purpose to hoarding)
- **Emergent potential:** Very high. Multiple facilities = parallelism. Facilities inside bags = portable workshops. Combined with portals, items could flow from wilderness → facility → storage automatically. Recipe chains (output of one is input of another) create factory design puzzles a la Factorio.

## Open Questions

- Where does FacilityState live? On Bag as an optional field? As a special CellFrame? As a separate record referenced by Bag?
- Should game ticks be real-time, turn-based (one tick per player action), or configurable?
- Can the player rearrange items within input/output slots, or are they auto-managed?
- How are facilities obtained? Crafted from a starter facility? Found? Always available?
- Should recipes be visible before the player has the facility, to guide goal-setting?

## Status

Proposed. Depends on a tick/time system. Natural fit for mid-stage development after Cell Frames and basic bag navigation are solid. Facility-owned recipes are the simplest starting point.
