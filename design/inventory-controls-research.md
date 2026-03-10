# Inventory Controls Research: Cross-Game Comparison

**Status:** Research reference document
**Date:** 2026-03-09

This document catalogs the inventory interaction controls used by six games: Minecraft, FFXIV, Satisfactory, Dyson Sphere Program, Factorio, and Arknights: Endfield. It is pure research -- no mapping to Pockets is attempted here.

---

## Per-Game Breakdown

### 1. Minecraft (Java Edition)

| Input | Action |
|-------|--------|
| Left-click (on stack) | Pick up entire stack onto cursor |
| Right-click (on stack) | Pick up half the stack (smaller half stays) |
| Left-click (cursor holding items, on empty slot) | Place entire held stack |
| Right-click (cursor holding items, on empty slot) | Place one item from held stack |
| Left-click (cursor holding items, on occupied slot) | Swap cursor stack with slot stack |
| Left-click (cursor holding items, on same-type slot) | Merge stacks (overflow stays on cursor) |
| Left-click drag | Distribute held stack evenly across dragged-over slots |
| Right-click drag | Place one item in each dragged-over slot |
| Middle-click (Creative mode) | Clone full stack of item without consuming it |
| Middle-click drag (Creative, Java) | Place full stack in each dragged-over slot |
| Double-click | Gather all same-type items from container into one stack (up to max) |
| Shift + Left-click | Quick-move entire stack to other inventory (e.g. chest <-> player) |
| Shift + Double-click | Move ALL stacks of that item type between inventories (must already hold an item on cursor) |
| Number keys 1-9 | Swap hovered item with that hotbar slot |
| Q (while hovering) | Drop one item from hovered stack to ground |
| Ctrl + Q (while hovering) | Drop entire hovered stack to ground |

**Controller (Bedrock/Console):**
- D-pad / left stick to navigate slots
- A/X to pick up / place items
- Bumpers to scroll hotbar
- D-pad down to drop item
- No equivalent for drag-painting, double-click, or half-stack pickup -- console inventory is simpler

**Notable features:**
- Drag-painting (left-drag to distribute, right-drag to sprinkle one each) is unique and widely copied
- Double-click gather is a "vacuum" for scattered same-type items
- Shift+double-click for "move all of type" is powerful but requires holding an item first (obscure)

---

### 2. FFXIV (Final Fantasy XIV)

| Input | Action |
|-------|--------|
| Left-click (on item) | Pick up item; it floats on cursor |
| Left-click (on destination slot) | Place item in slot / swap if occupied |
| Drag and drop | Move item between slots or between inventory tabs |
| Right-click (on item) | Open subcommand context menu (Use, Equip, Discard, Sort, Split Stack, Dye, etc.) |
| Right-click > Split Stack | Opens dialog to choose split amount |
| Drag onto vendor window | Quick-sell item (faster than right-click > sell) |
| Sort button (inventory UI) | Auto-sort inventory by configured rules |
| /itemsort command | Sort inventory with custom conditions via text command |

**Controller (PlayStation/Xbox):**
- D-pad to navigate inventory grid
- X / A to confirm / pick up item
- Square / X to open subcommand menu (equivalent to right-click)
- Circle / B to cancel
- L1/R1 to switch inventory tabs
- Touchpad (PS) for cursor mode

**Notable features:**
- Heavily menu-driven: right-click opens a rich subcommand menu rather than having direct mouse gestures for split/merge
- No shift-click quick transfer; moving items between inventories requires drag-and-drop or subcommand
- No multi-select; items are handled one at a time
- Sort is highly configurable through character settings
- Controller experience is first-class; the game was designed for console from the ground up

---

### 3. Satisfactory

| Input | Action |
|-------|--------|
| Left-click | Pick up / place entire stack |
| Right-click | Split stack in half |
| Right-click (hold) | Open split slider for custom amount |
| Shift + Left-click | Transfer entire stack to/from other inventory |
| Ctrl + Left-click drag | Transfer ALL items of same type to other inventory |
| Sort button (UI) | Sort inventory contents |

**Controller (PS5/Xbox):**
- Official controller support as of v1.1
- DualSense adaptive triggers supported
- Navigation via left stick
- Face buttons for grab/place/cancel
- Specific button mappings for split and transfer not well documented

**Notable features:**
- Hold-right-click for custom split slider is a nice UX for precision splitting
- Ctrl+drag for "move all of type" is efficient for bulk transfers
- Stack sizes vary by item: 1, 50, 100, 200, or 500

---

### 4. Dyson Sphere Program

| Input | Action |
|-------|--------|
| Left-click | Pick up entire stack |
| Right-click drag | Split stack: defaults to half, drag slider to adjust amount |
| Shift + Right-click drag | Split by full stacks (slider increments by stack size, not by 1) |
| Ctrl + Left-click | Transfer all items of that type to other inventory |
| Ctrl + Right-click drag | Pick up a custom number of items of the same type |
| Ctrl + Left-click (on empty slot) | Sort inventory |
| Sort button (top-right of inventory window) | Sort inventory |
| E | Open inventory |

**Controller:** No controller support (PC-only game)

**Notable features:**
- Right-click drag with slider for splitting is very precise
- Shift modifier changes slider increment from single items to full stacks -- useful for very large quantities
- Ctrl+right-click drag to pick a custom amount of a specific type is unique

---

### 5. Factorio

| Input | Action |
|-------|--------|
| Left-click (on stack) | Pick up entire stack |
| Right-click (on stack) | Pick up half the stack |
| Left-click (cursor holding, on empty slot) | Place entire held stack |
| Right-click (cursor holding, on empty slot) | Place one item |
| Shift + Left-click | Transfer one stack to other inventory |
| Shift + Right-click | Transfer half of one stack to other inventory |
| Ctrl + Left-click | Transfer ALL items of same type (or all items if empty-handed) |
| Ctrl + Right-click | Transfer HALF of all items of same type |
| Ctrl + Left-click (on entity, without opening) | Fast-insert: fill entity with held item without opening its UI |
| Ctrl + Right-click (on entity, without opening) | Fast-insert half of held item into entity |
| Ctrl + Left-click drag (over entities in world) | Fill multiple entities by dragging across them |
| Middle-click | Set filter on inventory slot (for filtered inventories) |
| Shift + Right-click / Shift + Left-click (on filtered slot) | Copy / paste filter settings |

**Controller:**
- Official gamepad support added in v1.1.83
- Playable but slower for inventory management
- D-pad for navigation, face buttons for confirm/cancel
- No equivalent for drag-over-entities mechanic

**Notable features:**
- The "half" modifier is systematic: right-click always means "half" of whatever left-click does
- Ctrl+click on entities in the world (without opening inventory) for fast entity insertion is unique to Factorio
- Ctrl+drag over multiple entities to fill them in one sweep is extremely powerful for factory management
- Middle-click for slot filtering is a distinct third action on a third button
- The left/right = full/half pattern is deeply consistent and learnable

---

### 6. Arknights: Endfield

| Input | Action |
|-------|--------|
| Click item | Select item for details / context actions |
| Drag and drop | Move items between Backpack and Depot |
| B key | Open Valuables Stash |
| X key | Enter Stash Mode (transfer view between backpack and storage) |
| Quick Stash button (in Depot UI) | Transfer ALL backpack items to Depot in one action |
| Filter icon | Filter inventory by category (Plants, Minerals, Products, etc.) |
| Sort (UI button) | Sort inventory contents |

**Controller (PS5/Xbox/mobile):**
- Full controller support
- Left stick to navigate, face buttons for confirm/cancel/context
- Touch controls on mobile (tap to select, drag to move)

**Notable features:**
- Inventory is intentionally small (35 slots); the game pushes "backpack as staging area, depot as warehouse" philosophy
- Quick Stash is a one-button bulk transfer -- dump everything to storage
- No complex mouse gestures for splitting; the game keeps inventory interaction simple
- Category filtering is prominent in the UI
- The game is designed for mobile-first (touch), with PC/controller as secondary

---

## Comparative Summary Table

| Feature | Minecraft | FFXIV | Satisfactory | DSP | Factorio | AK:Endfield |
|---------|-----------|-------|-------------|-----|---------|-------------|
| **Pick up stack** | L-click | L-click | L-click | L-click | L-click | Click |
| **Pick up half** | R-click | Menu | R-click | R-drag | R-click | -- |
| **Place all** | L-click | L-click | L-click | L-click | L-click | Drag |
| **Place one** | R-click | -- | -- | -- | R-click | -- |
| **Custom split** | -- | Menu dialog | Hold R-click (slider) | R-drag (slider) | -- | -- |
| **Swap** | L-click occupied | L-click occupied | L-click occupied | L-click occupied | L-click occupied | -- |
| **Merge stacks** | L-click same type | Drag onto | L-click same type | L-click same type | L-click same type | -- |
| **Quick-move stack** | Shift+L-click | Drag only | Shift+L-click | -- | Shift+L-click | Drag |
| **Move all of type** | Shift+Dbl-click | -- | Ctrl+L-drag | Ctrl+L-click | Ctrl+L-click | Quick Stash (all) |
| **Move half of type** | -- | -- | -- | -- | Ctrl+R-click | -- |
| **Distribute evenly** | L-drag (paint) | -- | -- | -- | -- (mod) | -- |
| **Sprinkle one each** | R-drag (paint) | -- | -- | -- | -- | -- |
| **Gather all same type** | Double-click | -- | -- | -- | -- | -- |
| **Sort** | -- (no vanilla) | Button + cmd | Button | Button + Ctrl+L-click empty | -- (no vanilla) | Button |
| **Filter slots** | -- | -- | -- | -- | Middle-click | Category filter UI |
| **Drop to ground** | Q / Ctrl+Q | Menu | -- | -- | -- | -- |
| **Hotbar swap** | Number 1-9 | -- | -- | -- | -- | -- |
| **Fast entity insert** | -- | -- | -- | -- | Ctrl+L-click entity | -- |
| **Context menu** | -- | R-click | -- | -- | -- | Click |
| **Controller support** | Full (Bedrock) | Full (native) | Full (v1.1+) | None | Basic (v1.1.83+) | Full |

---

## Cross-Cutting Patterns and Observations

### The Left/Right Symmetry Pattern
Factorio and Minecraft both use a consistent pattern: **left-click = full amount, right-click = half amount** (or single item). Factorio extends this systematically to every modifier combination (Shift+L = transfer stack, Shift+R = transfer half; Ctrl+L = transfer all of type, Ctrl+R = transfer half of type). This makes the system highly learnable once you understand the rule.

### The Modifier Key Ladder
Several games use a consistent escalation of modifier keys:
- **No modifier**: basic single-item operations
- **Shift**: operate on one stack as a unit
- **Ctrl**: operate on ALL items of a type
- **Ctrl+Shift**: (unused by most, potential for future expansion)

### Menu-Driven vs. Gesture-Driven
- **Gesture-driven** (Minecraft, Factorio, Satisfactory, DSP): mouse buttons + modifiers perform actions directly. Faster for experienced players, steeper learning curve.
- **Menu-driven** (FFXIV, AK:Endfield): right-click opens a context menu with options. More discoverable, but slower for repeated actions. Better suited for controller/touch.

### Splitting UX Spectrum
From simplest to most precise:
1. **Binary split** (Minecraft, Factorio): right-click = half, no slider
2. **Slider split** (Satisfactory, DSP): hold/drag opens a slider for exact amount
3. **Dialog split** (FFXIV): opens a separate dialog window with number input

### Drag-Painting (Minecraft Exclusive)
Minecraft's drag-painting (left-drag distributes evenly, right-drag places one per slot) has no equivalent in any other game studied. It is extremely useful for crafting grid setup and chest organization. Factorio has a mod (Even Distribution) that adds similar functionality for world entities, but not for inventory slots.

### "Move All of Type" Implementations
- **Minecraft**: Shift + double-click (requires holding an item -- obscure)
- **Factorio**: Ctrl + left-click (clean, discoverable)
- **Satisfactory**: Ctrl + left-click drag
- **DSP**: Ctrl + left-click
- **AK:Endfield**: Quick Stash button (moves everything, not per-type)

### Controller Inventory Challenges
Every game that added controller support simplified inventory interaction compared to mouse:
- No drag-painting equivalent
- No modifier-key combos (Shift/Ctrl)
- Rely on face buttons + bumpers for basic pick/place/transfer
- Some games (FFXIV) use a virtual cursor mode as fallback
- Context menus become more important on controller since fewer direct actions are available

### Unique Standout Features
| Game | Feature | Why It's Interesting |
|------|---------|---------------------|
| Minecraft | Drag-painting | Only game with "spread items across slots by dragging" |
| Minecraft | Double-click gather | Vacuum up scattered same-type items into one stack |
| Factorio | Fast entity insert (Ctrl+click in world) | Fill machines without opening their inventory |
| Factorio | Systematic left/right = full/half | Every operation has a "half" variant via right-click |
| Satisfactory | Hold-right-click split slider | Precise splitting without a separate dialog |
| DSP | Shift changes slider to stack increments | Efficient for very large quantities |
| DSP | Ctrl+right-drag custom pickup | Pick an exact quantity of a specific item type |
| FFXIV | /itemsort text command | Sort with arbitrary custom conditions |
| AK:Endfield | Quick Stash one-button | Dump entire backpack to storage instantly |
| AK:Endfield | Backpack-as-staging-area philosophy | Tiny inventory by design; forces depot usage |
