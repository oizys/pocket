# Panel UX Design

## Status: In Progress

## Layout Rules

### Panel Visibility
- B and T are always visible
- C and W can be open independently
- If B and W are both open, C is not (or vice versa — at most two "content" panels)
- In the final game, W is the rendered world itself — it moves from abstract (inventory grid) to real (world view) depending on context. The TUI approximates this with a grid panel.

### Spatial Layout (TUI)
Current: C above B, W below B, T at bottom, H left of all.

Future consideration: C or W beside B (left/right split) if we adopt compact cell rendering (1-char contents, square cells with unicode symbols instead of text). This improves readability when multiple panels are open simultaneously.

### Panel Sizing
Each cell is a fixed size. Larger bags = larger panels. No scrolling. Compact rendering mode is a future option for fitting more panels.

## Focus

- Tab / Shift-Tab cycles through open panels
- Mouse click on a panel auto-switches focus to it
- Arrow keys move the cursor in the focused panel
- Zero-arg DSL verbs resolve against the focused panel's location
- Focus indicator: `►` in title, cyan border (gray when unfocused)

## Cross-Panel Interaction

### Hand (H) as Clipboard
- Grab always goes to H regardless of which panel has focus
- Drop always comes from H regardless of which panel has focus
- This means grab in C → Tab to B → drop in B "just works"
- H renders at a fixed position left of all panels

### Toolbar (T)
- T's cursor position = the active/selected tool
- The highlighted toolbar slot determines what "primary" does in world contexts (future)
- Grab from T → item goes to H (like any other panel)
- Primary/open on a bag item in T → replaces B's root bag (see "Bag Swapping" below)

## Bag Swapping (B Root)

When primary/open is used on a bag item in T:
- B's current root bag is saved (need a "home bag" or "root stack" concept)
- B switches to viewing the opened bag
- This is distinct from breadcrumb navigation — it's swapping the root, not entering a child
- Exiting should restore the previous B root

Implementation: LocationMap.B tracks the active root. A separate field or stack tracks the "home" root for restoration.

## Recipe Selector and C Panel

### Current Issue
Recipe selector and facility descriptions are coupled to the B panel (GridPanel). When a facility is opened as C, the recipe UI should move to the C panel context.

### Fix
- Recipe selector (R key, CycleRecipe) should operate on the focused panel if it's C
- Item description view should show info for the cursor cell in the *focused* panel, not always B
- Consider: description as its own standalone panel that follows focus

## Item Description

### Current Issue
Description is embedded in GridPanel (B). When focus is on C or W, it still shows B's cursor item.

### Fix
Description should be a standalone view that reads from the focused panel's cursor cell. It updates whenever:
- Focus changes
- Cursor moves in the focused panel
- Cell contents change (after grab/drop/craft)

Position: below the focused panel, or in a fixed area at the bottom of the left column.

## World (W) — Long-Term Vision

W is a core concept that evolves through the game:
1. **Abstract**: W is an inventory grid (wilderness bag with harvestable items) — current implementation
2. **Spatial**: W is a world grid where cursor = avatar position, movement = walking
3. **Immersive**: "Diving into" a bag transforms it into the world. The bag IS the world.
4. **Fluid**: The boundary between inventory and world blurs — organizing your bags IS exploring the world

The TUI prototype handles stage 1 (abstract). The Godot build will explore stages 2-4.

## Open Questions

- Should there be a visual indicator of which bag in B is currently open as C/W? (dimmed border, highlight, icon)
- How to handle the case where a bag open as C is grabbed/moved in B? Close C automatically?
- Should T slots have category filters (weapon slot, tool slot, etc.)?
- Keyboard shortcuts for direct panel focus? (F1=T, F2=C, F3=B, F4=W)
