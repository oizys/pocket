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

Currently ticks operate at the bag level (FacilityState on Bag). Farming needs per-cell tick tracking.

**Decided approach — Item Properties (Design #16):**
Plant progress is stored as per-instance ItemProperty data on the unique Plant item (`Progress: Int`, `Duration: Int`). Tick logic scans cells for plant items and advances their progress. This aligns with a broader goal of unifying facility progress under item properties too — FacilityState.Progress could eventually migrate to the owning ItemStack's properties, making facilities and plants use the same tick mechanism.

See [item-properties.md](item-properties.md) for the infrastructure design and [Bag-to-Owner Resolution](#bag-to-owner-resolution) below for how facility bags would access their owning item's properties.

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
- **Growth stages for visuals**: Even with instance data tracking progress internally, staged visual representations (sprout/growing/grown) could be driven by progress thresholds without needing separate ItemTypes.
- **Watering/fertilizing**: Future complication — could modify tick rate or produce quality. Not needed for v1.
- **Cross-pollination (Moore neighborhood)**: Future complication — adjacent plants could modify each other's loot tables (Animal Crossing flower genetics). Not needed for v1.
- **Visual stages**: Even if using instance data internally, how many visual stages? Minimum 2 (growing/grown), maximum 4 (seed/sprout/growing/grown) like Pokopia.

## Methodology Fit

- **Testable**: Plant lifecycle is pure state transitions — seed placement, tick advancement, harvest, reset. All unit-testable.
- **Incremental**: Start with one plant species, fixed produce, simple Planter frame. Add loot tables, more species, and complications later.
- **Data-driven**: `# Plant:` content blocks fit the existing typed-header markdown system. New content type in ContentParsers + ContentRegistry.
- **Reuses existing systems**: Cell Frames, ticks, grab tool, loot tables, content loader.

### Bag-to-Owner Resolution

If facility progress migrates from `FacilityState` on `Bag` to item properties on the owning `ItemStack`, facility bags need a way to look "up" at their owner's properties. This is relevant to farming because plants and facilities would share the same tick mechanism. See detailed analysis in [item-properties.md](item-properties.md#bag-to-owner-resolution).

## Status

Proposed. Growth state tracking decided: Item Properties (Design #16). V1 should be minimal: one plant species, fixed produce, simple Planter frame, no watering/fertilizing/cross-pollination. Depends on item properties infrastructure being implemented first.
