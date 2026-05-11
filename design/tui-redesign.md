# TUI Cleanup & Redesign

## Status: Proposed

## Concept

Bring the Pockets TUI in line with the design-doc aspirations in `panel-ux.md` and the two hard constraints the user re-stated on 2026-05-11: **no modal pop-ups** and **no text on the icon/map grid**. Three structural changes plus a cleanup pass:

1. **Glyph cells** — replace 10×3 text-in-cell rendering with compact glyph cells. The grid becomes purely visual; all text moves to the surrounding panels.
2. **Inline split mode** — remove `ShowModalSplitDialog` (the only modal). Replace with a `SplitMode` state rendered into a bottom command strip.
3. **Focus-following description pane** — promote `ItemDescriptionView` from a child of `GridPanel` to a standalone view at `GameView` level that reads the focused panel's cursor cell (the fix already proposed in `panel-ux.md` §Item Description).

## Cohesion

- `panel-ux.md` §Spatial Layout already flags compact-glyph cells as a "future consideration." This proposal promotes it to now.
- `panel-ux.md` §Item Description and §Recipe Selector both call out the same coupling problem (description hard-wired to B). One fix solves both.
- Removes the only modal that contradicts the user's stated UX direction.
- Core domain model (Grid/Cell/ItemStack/Bag/Cursor) is unchanged — every change lives in `Pockets.App`. Godot parallel renderer is unaffected.

## Intuition

The grid is the playing field. Cells render as one colored glyph (category-driven) on a category-background. Selection/cursor is shown via attribute change on the glyph cell, not via text overlay. Everything textual — item name, description, recipe list, crafting progress, action queue, hotkey hints — lives in panels around the grid. Verbs are discoverable through a context-aware command strip at the bottom of the active panel that updates per-cell-state, replacing both the static hotkey label and the Spacebar-style "all commands" modal that Cogmind uses.

## Architecture

### New views

- **`GlyphRenderer`** — replaces `CellRenderer`'s text-content helper. Maps `(Category, ItemType)` → `(Glyph char, FgColor, BgColor)`. Reads from a glyph map alongside `CategoryColors`.
- **`CompactGridView`** — renders cells at 3×2 terminal cells per logical cell (visually square at ~2:1 cell aspect). Selectable density (2×1 for late-game large wildernesses).
- **`CommandStripView`** — replaces `GridPanel._toolbar` (the hardcoded Label at `GridPanel.cs:78`). Receives `(focused panel, cursor cell)` and renders the available verbs with hotkeys: `[1/E Open] [2 Half] [3 Split] [4 Sort] [Q Back]` on a bag cell; `[1 Drop] [4 Sort]` on an empty cell; etc.
- **Standalone `FocusedDescriptionView`** — owned by `GameView`, not `GridPanel`. Subscribes to `FocusChanged` and `CursorMoved` events from all panels.

### State changes

- **`GameSession.SplitMode`** — optional state record `(LocationId, Position, int amount)`. When non-null, the command strip renders the inline amount editor. `←`/`→` adjust by 1, `Shift+←`/`Shift+→` by 10, `Enter` confirms, `Esc` cancels. No popup.
- **Tick timer behaviour** during `SplitMode`: pause (the modal currently halts ticks; preserve parity).

### Removed

- `GameView.ShowModalSplitDialog` (lines 285–323) and its `Application.Run(dialog)` reentrant loop.
- `CellRenderer.ContentWidth` and text-content helpers (after `GlyphRenderer` lands).
- Dead session API: `GameSession.ExecuteGrab/Drop/EnterBag/Interact/Harvest` (lines 181–278) — only callers are tests; migrate tests to the controller path.

## Cleanup pass (separable from the redesign, all low-risk)

| # | Fix | Location |
|---|-----|----------|
| 1 | `RootBag` → `ActiveBag` for B-panel height | `GameView.UpdatePanelLayout()` line 263 |
| 2 | Refactor `ExecuteOnFocusedPanel` swap hack — thread focused `LocationId` through tools explicitly | `GameController` lines 225–249 |
| 3 | `RightPanel._logContent` single `Label` → scrollable list | `RightPanel.cs:24` |
| 4 | Gate `_inputStatus` behind debug flag (or delete) | `GridPanel.cs:94` |
| 5 | Rename one of the two "toolbar" concepts | `GridPanel._toolbar` vs `GameView._toolbarPanel` |
| 6 | Move `RecipeRegistry`/`Recipe` lookups out of `ItemDescriptionView` into a Core display-text helper | `ItemDescriptionView.cs:55–67` |
| 7 | Document the magic `+6`/`+3` constants in `UpdatePanelLayout` (or compute from child sizes) | `GameView.cs:263, 281` |

## Methodology Fit

- **TDD-friendly:** `GlyphRendererTests`, `CommandStripViewRenderTests`, `SplitModeTests` are all unit-testable via the existing `TuiTestHarness`. Existing `GridViewRenderTests` and `GamePlaythroughTests` define the parity bar.
- **Staged delivery:** four commits, each shippable:
  1. **Cleanup pass** (items 1–7 above). No behavior change, all tests stay green.
  2. **Inline split mode.** Kills the modal. Update `GamePlaythroughTests` for the new flow.
  3. **Standalone focus-following description.** Resolves `panel-ux.md` §Item Description.
  4. **Glyph cells.** Biggest visual change — keep last so rollback is one commit.
- **Godot impact:** zero. All changes in `Pockets.App`. Core API stable.

## Open Questions

- **Glyph cell aspect:** 3×2 terminal cells (visually square, matches Cogmind, ~33×25 logical capacity at 100×50 terminal) vs 2×1 (denser, ~50×25, better for large wilderness grids). Pick one default per stage?
- **Glyph differentiation:** color-only, glyph-only, or both? Cogmind uses both — category gives color, item-type gives glyph. Recommend matching that.
- **Command strip ownership:** one global strip at `GameView` bottom, or one per panel? Per-panel is more accurate to focus model, global is simpler. Recommend per-panel, mirroring the focused-description pattern.
- **Breadcrumbs:** stay in `GridPanel` title row, or move to the command strip? Title row is cleaner; recommend leaving them.
- **R (CycleRecipe) on non-B focus:** does it operate on focused panel (per `panel-ux.md` §Recipe Selector)? Recommend yes — folds into change 3.

## What stays untouched

- `GameController` / `GameSession` / `GameState` zipper pattern — clean separation, well-tested
- `TuiTestHarness` and existing render tests
- `GameKey` / `ClickType` enums in `Pockets.Core.Models`
- `Pockets.Godot` parallel renderer
- Tab/Shift-Tab focus model and per-panel cursor (already correct per `panel-ux.md`)
