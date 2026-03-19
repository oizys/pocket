# Farming: Seed → Plant → Produce Lifecycle

## Concept

Farming is a simplified variant of facility crafting applied at the individual cell level. Instead of a facility bag with input/output slots and recipe selection, each plant occupies a single cell and progresses through a lifecycle: **Seed → Plant (growing) → Plant (grown) → Harvest → reset**.

### Core Items Per Farmable Species

Each farmable plant requires:
- **Seed** — A stackable item the player stores and places to begin growing. Acts as the "input."
- **Plant** — A unique item representing the growing/grown organism. Occupies one cell.
- **Produce** — One or more item types harvested from a fully grown plant. Could be a fixed output or a loot table roll.

### Planting

A seed has placement requirements similar to a Cell Filter — the cell must meet certain conditions for the seed to take root. The simplest case is a **Planter Cell Frame** that provides the necessary soil/environment. Frames could be more specific (e.g. "Large Planter", "Hydroponic Tray") to gate certain plant types.

When a seed is placed in a valid cell, it converts into the corresponding Plant item and begins receiving ticks.

### Growth

Once planted, the plant receives ticks just like a facility. Each plant species has a known duration until fully grown. Progress tracks linearly from 0 to duration.

### Harvesting

When a plant is fully grown, a normal **Grab** action harvests it — producing the Produce type(s) (or rolling a loot table) and resetting the plant's progress to 0. The plant remains in the cell and begins growing again. This makes farming a renewable cycle without replanting.

## Cohesion: Very High

Reuses existing systems with minimal new mechanics:
- **Cell Frames** gate where seeds can be planted (already implemented)
- **Tick system** drives growth (already implemented for facilities)
- **Grab tool** doubles as harvest action (existing)
- **Loot tables** can define produce variety (already implemented in data architecture)
- **ItemType unique/stackable** distinction maps naturally to seeds (stackable) vs plants (unique)

The main new concept is a cell-level tick consumer rather than a bag-level one (facilities).

## Intuition: High

Plant seed, wait, harvest. Universal game mechanic understood by anyone who's played Stardew Valley, Minecraft, or Animal Crossing. The Planter frame requirement is intuitive — you need soil to grow things.

## Architecture

### Planting Flow

```
1. Player has Seed item (stackable) in hand
2. Player Drops seed onto cell with compatible Planter frame
3. Cell filter check passes → Seed consumed, Plant item created in cell
4. Plant item is unique, has progress = 0, duration from species data
5. Ticks advance progress
6. When progress >= duration → plant is "grown"
7. Grab on grown plant → Produce items acquired, progress resets to 0
8. Plant stays in cell, cycle repeats
```

### Data Definition

```markdown
# Item: Tomato Seed
Category: Material
Stackable: Yes
A small seed that grows into a tomato plant.

# Item: Tomato Plant
Category: Material
Stackable: No
A tomato plant in various stages of growth.

# Item: Tomato
Category: Consumable
Stackable: Yes
A ripe tomato, freshly harvested.

# Plant: tomato
Seed: Tomato Seed
Plant: Tomato Plant
Produce: 2 Tomato
Duration: 6
Frame: Planter
```

The `# Plant:` block is a new content type linking the three items together with growth parameters. `Frame` specifies the required CellFrame type for planting.

### Cell-Level Tick

Currently ticks operate at the bag level (FacilityState on Bag). Farming needs per-cell tick tracking. Two approaches:

**Option A — Plant item carries progress (unique item instance data):**
Plant items store progress as instance data on the unique ItemStack. Tick logic scans cells for plant items and advances their progress. This requires per-instance data on unique items (not yet implemented).

**Option B — PlantFrame on the cell:**
A `PlantFrame` CellFrame tracks growth state (species, progress, duration). The plant item is just a marker; the frame does the work. This keeps instance data out of items and uses the existing CellFrame extension point.

**Option C — Staged item types (no instance data):**
Define separate ItemTypes per growth stage: "Tomato Sprout", "Tomato Plant (Growing)", "Tomato Plant (Grown)". Tick logic swaps the item type at each threshold. Simple to render (each stage can have distinct visuals) but more verbose data definitions.

### Grab Override for Harvest

When grabbing from a cell containing a fully grown plant:
- Instead of removing the plant, produce the Produce items into the hand
- Reset the plant's progress to 0
- If the hand is full, no-op (same as normal grab failure)

This is a special case in the Grab tool — check if the cell contains a grown plant before doing the normal cut behavior.

### Transplanting

A dedicated tool or special action to uproot a plant:
- Converts the Plant back to a Seed (losing progress)
- Or moves the Plant item directly (preserving progress if using instance data)
- Prevents accidental uprooting via normal grab (which harvests instead)

## Open Questions

- **Planting tool**: Is Drop sufficient to plant, or does a dedicated "Plant" tool make the seed→plant conversion more explicit? Drop might be cleaner since it already handles cell filter checks.
- **Planter crafting**: How are Planter frames created? A recipe in Workshop/Workbench that produces a planter item with a PlanterFrame? Or a tool that converts empty cells?
- **Transplant tool**: Dedicated tool key, or a modifier on Grab (e.g. Shift+Grab to uproot)?
- **Growth stages vs instance data**: Staged items (Option C) are simplest for rendering but verbose. Instance data (Option A) is most flexible but requires new infrastructure. PlantFrame (Option B) reuses CellFrame but conflates cell state with item state. Need to decide before implementation.
- **Watering/fertilizing**: Future complication — could modify tick rate or produce quality. Not needed for v1.
- **Cross-pollination (Moore neighborhood)**: Future complication — adjacent plants could modify each other's loot tables (Animal Crossing flower genetics). Not needed for v1.
- **Visual stages**: Even if using instance data internally, how many visual stages? Minimum 2 (growing/grown), maximum 4 (seed/sprout/growing/grown) like Pokopia.

## Methodology Fit

- **Testable**: Plant lifecycle is pure state transitions — seed placement, tick advancement, harvest, reset. All unit-testable.
- **Incremental**: Start with one plant species, fixed produce, simple Planter frame. Add loot tables, more species, and complications later.
- **Data-driven**: `# Plant:` content blocks fit the existing typed-header markdown system. New content type in ContentParsers + ContentRegistry.
- **Reuses existing systems**: Cell Frames, ticks, grab tool, loot tables, content loader.

## Status

Proposed. Depends on deciding the growth state tracking approach (Options A/B/C) before implementation can begin. V1 should be minimal: one plant species, fixed produce, simple Planter frame, no watering/fertilizing/cross-pollination.
