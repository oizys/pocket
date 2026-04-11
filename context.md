# Pockets - Context for Brainstorming

## Game Overview

Pockets is a grid-based inventory puzzle game where **everything exists inside nested bags**. Bags contain grids of items, and bags can be inside other bags. Your own organization is the challenge. The theme is whimsical fantasy — magic replaces science.

**Tech:** C# 12 / .NET 8. Currently a TUI prototype (Terminal.Gui); targeting Unity3D or Godot eventually.

### Core Model

- **Grid:** X x Y cells, row-major order
- **Cell:** Holds one ItemStack. Can have a category filter or CellFrame (input/output slot)
- **ItemType:** Stackable (identical, max 20) or unique (1 per cell, custom properties)
- **ItemStack:** ItemType + count. Merging stacks produces remainder beyond max
- **Bag:** An openable item containing a Grid + items. Has environment type and color scheme. Bags nest inside bags

### Item Acquisition

Items are placed starting at cell 0 (top-left), skipping filtered/mismatched cells, merging into matching stacks up to max, then continuing. Multiple stacks each restart from cell 0.

---

## Cursor and Navigation

- **Cursor** points to the active bag and cell — the player's current position
- Movement: arrow keys / WASD, wraps around grid edges
- **Selection:** Any number of cells can be selected (brighter background). If nothing is selected, the cursor cell counts as selected
- **Breadcrumbs:** A stack of cursors for navigating nested bags. Opening a bag pushes; leaving pops. Can't leave the topmost bag

---

## Actions and Tools

Players act through **Tools** with a speed attribute. Tool actions operate on the Selection — either the whole selection at once or per-item.

### Action Queue

Per-item actions queue individually. Each takes game time based on tool speed. Example: 4 stacks selected + Harvest tool (0.5s) = 4 queued actions at 0.5s each. The queue is cancellable.

### Current Hotkeys (Stage 3)

| Key | Action |
|-----|--------|
| Arrows / WASD | Move cursor |
| 1 / E / Left-click | **Primary:** contextual grab/drop/swap/merge/enter/harvest |
| 2 / Right-click | **Secondary:** grab half / place one |
| Shift-3 | Modal Split (dialog to adjust split amounts) |
| 4 | Sort (by category, name; merges stacks) |
| 5 | Acquire Random (debug) |
| Q / Back button | Go back up (pop breadcrumb) |
| Ctrl-Z | Undo (snapshot stack, max 1000) |
| Ctrl-Q | Quit |

### Hand Model

The Hand is a real hidden Bag (configurable slots, default 1). Grab = true cut (remove from grid, place in hand). Drop = paste from hand to cursor, remainder acquires from cell 0.

### Crafting

Facilities are special bags with input/output CellFrames. Recipes consume inputs over tick durations and produce outputs. Current facilities: Workbench, Tanner, Seedling Pot.

---

## UI Layout (4 Panels)

```
--------------------------------------------------
| Top: menu / game settings                      |
--------------------------------------------------
| [ Breadcrumbs           ] | [ Action Queue   ] |
|                           |                    |
| Active Grid               | World              |
|                           |                    |
| Cursor Item Name          |                    |
|   and Description         |                    |
--------------------------------------------------
| Bottom  [Toolbar                      ]        |
| Status                                         |
--------------------------------------------------
```

---

## Panels Design Idea (Future)

A **tmux-inspired panel system** for viewing multiple inventories simultaneously. Three modality tiers:

1. **Persistent (Toolbar)** — Always visible. Cursor = "main hand" (equipped item). First slot = default acquisition target
2. **Available (Inventory)** — Always accessible, toggled visible. Cursor = "eyeball" (browsing)
3. **Contextual (Chest/Facility/World)** — Only available when near the source. Cursor metaphor varies by context

### Cursor Metaphors per Panel

| Context | Metaphor | Behavior |
|---------|----------|----------|
| World/Wilderness | Avatar location | Movement = walking, interact = harvest/pickup |
| Inventory/Chest | Eyeball | Movement = browsing, interact = grab/drop |
| Toolbar | Main hand | Movement = equip swap, selected slot = active tool/item |
| Facility slots | Eyeball | Movement = slot selection, interact = fill/empty |

### Quick-Move Routing

- **Rule-based:** From Contextual -> Available (overflow to Persistent), etc.
- **Explicit selection (power-user):** Mark source (red) and target (blue) panels; quick-move always goes between them

### Tutorial Progression

1. Start with 1 panel: a 1x4 toolbar
2. Acquire a bag -> opens panel 2 (inventory), toolbar closes temporarily
3. Acquire a tool -> toolbar becomes persistent (2 panels)
4. Enter wilderness -> 3 panels: toolbar + inventory + world
5. Open facility -> 3 panels: toolbar + inventory + facility

The "off hand" (HandBag/clipboard) remains separate from all cursors — it's the transient carry state during grab/drop.

### Open Questions

- Does PanelLayout replace Breadcrumbs, or do breadcrumbs still exist within a panel?
- Should panel arrangement be fixed or user-configurable?
- Is undo per-panel or global?
- Does quick-move across panels go through the hand, or is it a direct transfer?

---

## Current State

Stage 3 is complete with ~309 tests. Features: contextual primary/secondary actions, mouse support, CellFrames, BagRegistry, FacilityState, tick system, and crafting. Stage 4 is TBD.
