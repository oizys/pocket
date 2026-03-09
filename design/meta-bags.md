# Meta-Bags: Game Systems as Inventory

## Concept

The root bag contains reserved slots holding special bags that *are* the game's UI systems. The toolbar is a bag (cells 0-8 = hotkeys 1-9, each holding a Tool item). Settings is a bag (cells = config toggles). The game's own interface is built from its own mechanics.

## Cohesion: Very High

Almost pure reuse of existing mechanics (bags, cells, items, cursor, grab/drop). The only new concept is "some bags are system-referenced" — a convention on top of existing bag-in-bag model. Cell category filters already restrict what goes where. The player already knows how to move items between cells — now that applies to rearranging their hotbar.

## Intuition: High

"Your toolbar is a bag. Drag tools into it to assign hotkeys." Immediately graspable to anyone who's played Minecraft/MMOs. The twist — settings screen is also a bag — is the kind of thing players find delightful once they notice it.

Potential confusion: players might accidentally grab/move a system bag out of its slot. Mitigate with pinned slots (cell filter) or undo.

## Architecture

- RootBag slots 0-N = player inventory, designated slots = system bags (convention, not type distinction)
- `GameState` gains named accessors: `ToolbarBag => RootBag.Grid.GetCell(toolbarIndex)` etc.
- Toolbar bag: 1x9 grid, category filter = `Category.Tool`
- Tool items = unique items with properties (speed, action type enum). Existing `ItemType.IsStackable=false` + custom properties fits
- Hotkey dispatch: key 1-9 -> read toolbar bag cell N -> resolve tool -> execute. Replaces current hardcoded switch
- Settings bag: cells hold toggle/value items (e.g. "Sim Speed" unique item with numeric property)
- No new model types needed. Data conventions + lookup indirection in input handling

## Methodology Fit

- **Builds on:** grab/drop, bags, cell filters, cursor navigation — all Stage 1 mechanics
- **New friction:** player must organize their toolbar before using tools efficiently. Tool acquisition becomes gameplay, not a given
- **Reduces friction:** once understood, players can customize their entire interface. Power users create tool-bag hierarchies (combat bag, crafting bag)
- **Emergent potential:** Very high. Tools are items, bags are items -> tool-bags-in-bags. A "loadout" is just a bag you swap into the toolbar slot. If automation comes later, could automate tool swapping. Settings-as-items means settings could be traded, copied, or scoped per-bag

## Open Questions

- Should system bags be movable (fully emergent, potentially chaotic) or pinned to root slots (safer, less emergent)? Start pinned, unlock movement as late-game mechanic.
- Risk: "I opened my toolbar bag and now hotkeys stopped working because I'm not in the root bag." Solution: toolbar always reads from the fixed reference regardless of active bag.

## Status

Proposed. Not yet scheduled for a development stage.
