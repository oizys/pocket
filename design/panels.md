# Panels: Multi-Inventory Layout System

## Status: Idea

## Concept

A tmux-inspired panel system for viewing and interacting with multiple inventories simultaneously. Each panel hosts a bag with its own cursor, and panels have varying levels of persistence and modality. Quick-move operations route items between panels based on configurable rules or explicit selection.

## Cohesion

Panels are the natural extension of the nested-bag model. Currently, opening a bag replaces the active view entirely (push/pop via breadcrumbs). Panels let multiple bags coexist on screen, enabling side-by-side organization, facility interaction without losing context, and progressive tutorial layering.

## Intuition

Three modality tiers (inspired by Pokopia/Minecraft patterns):

1. **Persistent** (Toolbar) — Always visible and active. Cursor here represents "main hand" (equipped/selected item). The first toolbar slot is the default acquisition target ("pachinko drop" — depth-first into this bag).
2. **Available** (Inventory) — Always accessible, toggled visible. Cursor here is an "eyeball" — examining/hovering.
3. **Contextual** (Chest/Facility/World) — Only available when near or inside the source. Cursor metaphor varies: "avatar location" in wilderness, "eyeball" in facilities.

These tiers can scale to 4-6+ panels as features layer on. Each tier has rules for:
- When it appears/disappears
- Whether it can be dismissed
- What quick-move targets it routes to

## Quick-Move Routing

Two models, possibly coexisting:

**Rule-based** (Pokopia-style):
- From Contextual → Available (overflow to Persistent)
- From Available → Contextual (error if full)
- From Persistent → Contextual (error if full)
- Rules are per-tier, not per-panel

**Explicit selection** (power-user):
- Two panels marked as "source" (red border) and "target" (blue border)
- Quick-move always goes source→target and vice versa
- Useful when 4+ panels are open and rule-based routing is ambiguous

## Cursor Metaphors

| Context | Metaphor | Behavior |
|---------|----------|----------|
| World/Wilderness | Avatar location | Movement = walking, interact = harvest/pickup |
| Inventory/Chest | Eyeball | Movement = browsing, interact = grab/drop |
| Toolbar | Main hand | Movement = equip swap, selected slot = active tool/item |
| Facility slots | Eyeball | Movement = slot selection, interact = fill/empty |

The "off hand" (current HandBag/clipboard) remains separate from all cursors — it's the transient carry state during grab/drop.

## Tutorial Progression

1. Start with 1 panel: a 1x4 toolbar (the only bag)
2. Acquire a bag item in slot 1 → opening it creates panel 2 (2x4 inventory), toolbar closes temporarily
3. Acquire a tool → tutorial: "slot 1 of toolbar = default drop target" → toolbar becomes persistent (2 panels)
4. Enter a wilderness bag → 3 panels: toolbar + inventory + world
5. Open a facility → 3 panels: toolbar + inventory + facility (world hidden but available)

## Architecture

### Panel Record
```
Panel(Bag bag, Cursor cursor, PanelTier tier, bool isVisible, int index)
```

### PanelTier Enum
```
Persistent, Available, Contextual
```

### PanelLayout
Manages the set of active panels, their arrangement, visibility, and focus. Owns the quick-move routing logic. Replaces or wraps the current breadcrumb navigation for multi-panel cases.

### Key Questions (TBD)
- Does PanelLayout replace Breadcrumbs entirely, or do breadcrumbs still exist within a single panel?
- How does the bag tree relate to panels? Each panel points into the tree, but panels are a UI concept, not a domain one.
- Should panel arrangement be fixed (toolbar=bottom, context=top, inventory=middle) or user-configurable?
- How do panels interact with undo? Is undo per-panel or global?
- Quick-move across panels: does it go through the hand (grab then drop), or is it a direct transfer?

## Methodology Fit

- Panels are a UI/session concern, not a domain model change — bags and grids stay pure
- Progressive disclosure: start simple (1 panel), add complexity as player progresses
- Each panel tier can be developed and tested independently
- Quick-move routing is a pure function of panel state — testable without UI
