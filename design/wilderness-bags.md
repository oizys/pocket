# Wilderness Bags: Harvestable Resource Worlds

## Concept

Special bags containing randomly generated "worlds" with harvestable resources. Opening a "Forest Wilderness" bag reveals a grid populated with rocks, trees, twigs, bushes, and empty spaces. The player navigates and uses tools to harvest resources, which flow through the normal Acquire algorithm into their parent bag. Wilderness bags are consumable — once depleted (or abandoned), they go to a history bag or are deleted. New wilderness bags may cost something to obtain.

This provides the core Minecraft/Stardew Valley harvest loop entirely within the bag metaphor: the world *is* inventory.

## Cohesion: Very High

A wilderness is just a bag with generated content. Harvesting is a tool action. Acquired items flow through the existing algorithm into the parent bag (via breadcrumb stack). No new core mechanics — just new content types (resource items, wilderness bag templates) and a bag generation system.

## Intuition: High

"Open a bag, it's a forest. Grab the wood." Immediately graspable. The only new concept is that some bags are consumable/finite — but that maps to familiar game concepts (consumable items, dungeon keys, expedition supplies).

## Architecture

- `WildernessTemplate`: biome type, loot table (item type + weight + density), grid dimensions. Stored as data files in `/data`
- `WildernessGenerator`: takes template, produces a Bag with randomly populated cells. Placement uses weighted random per-cell with density controlling fill ratio
- Harvest tool: per-item action with speed varying by resource type. On harvest, item removed from wilderness cell, `AcquireItems` called on **parent bag** (breadcrumb peek gives reference)
- Bag lifecycle enum: `Fresh → InProgress → Depleted`. Depleted or abandoned bags either move to a history/archive bag or are destroyed
- Wilderness bag acquisition: gated by cost (crafting recipe, currency item, found as loot in other wilderness bags)
- Optional: refresh timer that repopulates some cells over game time (renewable vs. non-renewable resources)

## Methodology Fit

- **Builds on:** bags, cursor navigation, tools, acquire algorithm, breadcrumbs — all existing Stage 1+ mechanics
- **New friction:** resource scarcity. Player must find/buy wilderness bags, choose when to harvest vs. move on, manage inventory space for incoming loot
- **Reduces friction:** gives purpose to inventory organization — now there's a reason to sort and make room, because loot is incoming
- **Emergent potential:** High. Wilderness bags inside other bags = expeditions. A "dungeon" is a bag of wilderness bags. Nested wilderness bags could contain rarer biomes. If automation comes later, auto-harvest tools could run inside wilderness bags. Players might stockpile wilderness bags as tradeable commodities

## Open Questions

- Should wilderness bags refresh over time (renewable) or be single-use (scarcity-driven)?
- What gates access to new wilderness bags? Crafting, currency, random drops, or progression unlocks?
- How does the "history bag" work? Is it automatic archival, or does the player manually manage it?
- Can resources in a wilderness bag be rearranged (grab/drop within the wilderness), or only harvested out? Allowing rearrangement is more consistent with the bag metaphor but less "wilderness-like"

## Status

Proposed. Natural fit for an early-mid development stage after basic bag-in-bag navigation (breadcrumbs) is working.
