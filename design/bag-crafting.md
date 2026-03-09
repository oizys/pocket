# Bag Crafting: Building Better Bags as Progression

## Concept

Bag Crafting is a specialization of crafting that produces new Bags from templates or generator functions. Creating bigger and better bags is a primary engine of progression — the player's organizational capacity grows as they invest materials into bag construction.

Early bags are small and generic (1x4 belt pouch). Later bags are larger (2x3 sack, 4x4 chest), specialized (1x6 potion belt with filtered cells), or exotic (bags with Cell Frames, time transforms, or portals built in). Since bags nest arbitrarily, a player *could* hoard dozens of 1x4 pouches for infinite storage — but the navigation burden makes this miserable compared to a single well-organized 4x4 chest. This creates genuine demand for bag upgrades: not just more space, but better space.

## Cohesion: Very High

Bag Crafting is just Crafting (see `crafting.md`) where the output is an ItemStack with a ContainedBag. The bag template defines the grid dimensions, environment type, color scheme, and any pre-placed Cell Frames or filters. No new mechanics — just a new output type for the existing recipe system.

## Intuition: Very High

"Craft a bag to hold more stuff." Universal RPG/survival concept. The twist is that bags aren't just capacity — they're organizational tools with structure (dimensions, filters, frames). A 2x4 bag and a 4x2 bag hold the same number of items but feel different to navigate and organize.

## Architecture

### Bag Templates

A BagTemplate defines what bag a recipe produces:

```
BagTemplate
├── Columns: int
├── Rows: int
├── EnvironmentType: string
├── ColorScheme: string
├── CellFrames: ImmutableDictionary<int, CellFrame>?    (pre-placed frames by cell index)
├── CategoryFilters: ImmutableDictionary<int, Category>? (per-cell filters)
└── TimeTransform: TimeTransform?                        (if the bag has special time properties)
```

A bag recipe's output is a function, not just an ItemStack list — it calls `BagTemplate.Generate()` to produce a fresh Bag with a unique Id.

### Recipe Integration

Bag recipes work like normal recipes but with a generator output:

```markdown
# Belt Pouch
**Facility**: Workbench
**Duration**: 5
**Inputs**: 2 Woven Fiber
**Output**: Belt Pouch (1x4 bag)
```

The recipe system needs to support output factories (functions that produce ItemStacks) rather than only static ItemStack lists, since each crafted bag must have a unique Id and fresh grid.

### Progression Tiers

Example progression (numbers are illustrative):

| Tier | Bag | Size | Special | Materials |
|------|-----|------|---------|-----------|
| 1 | Belt Pouch | 1x4 | — | 2 Fiber |
| 1 | Herb Satchel | 1x4 | Medicine-only cells | 2 Fiber, 1 Herb |
| 2 | Leather Sack | 2x3 | — | 3 Leather, 1 Fiber |
| 2 | Tool Roll | 1x6 | Tool-only cells | 2 Leather, 1 Wood |
| 3 | Wooden Chest | 4x4 | — | 8 Wood, 2 Iron |
| 3 | Potion Belt | 1x6 | Consumable-only, color-coded | 2 Leather, 1 Crystal |
| 4 | Enchanted Trunk | 4x6 | Time transform (2x speed inside) | Rare materials |
| 5 | Dimensional Satchel | 6x6 | Portal frame on cell 0 | Exotic materials |

### Why Bag Quality Matters

Infinite nesting means raw capacity is trivially solvable — just nest more small bags. But quality of life degrades fast:
- **Navigation cost**: entering/leaving bags is friction. Fewer, larger bags = less traversal.
- **Sort effectiveness**: Sort operates within one bag. Larger bags sort better.
- **Visual clarity**: a 4x4 grid is scannable at a glance. Four nested 1x4 bags are not.
- **Specialization**: filtered cells prevent clutter (potions can't end up in your tool bag).
- **Automation**: larger bags have more room for conveyors, frames, facilities.

This makes bag crafting feel essential, not optional — even though the game never hard-gates progress on bag size.

## Methodology Fit

- **Builds on:** Crafting system, Bag/Grid/Cell model, Cell Frames, ContainedBag on ItemStack
- **New friction:** Material cost for bags. Player must choose between crafting tools/items vs. expanding storage.
- **Reduces friction:** Better bags reduce navigation burden, improve organization, enable automation setups.
- **Emergent potential:** High. Specialized bags become tradeable commodities. Bag-crafting facilities can be shared. The "best bag layout" becomes a player expression/optimization problem. Exotic bags (time-warped, portal-linked, frame-laden) blur the line between storage and machine.

## Open Questions

- Should bags be repairable/upgradable, or must the player craft a new one and migrate contents?
- Can bags be disassembled to recover materials?
- Should bag recipes require a specific facility (loom for fiber bags, forge for metal chests), or can any workbench make any bag?
- How does the player discover bag recipes? Fixed progression, random drops, or exploration-gated?

## Status

Proposed. Depends on Crafting system. Can start simple with hardcoded bag templates and add recipe-driven generation once crafting is implemented.
