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
- **`CompactGridView`** — renders cells at **3×2 terminal cells per logical cell** (visually square at ~2:1 cell aspect; matches Cogmind). Logical capacity ~33×25 at a 100×50 terminal, comfortably above the 16-cell max bag/wilderness dimension the design caps at (larger wildernesses stitch discrete rooms via portals). Glyph uses **both color and character** — category drives background/foreground color, item-type drives the glyph character (matches the Cogmind "color = class, glyph = identity" pattern).
- **`CommandStripView`** — **one global strip**, anchored to the bottom row of `GameView` (full width). Replaces the hardcoded `GridPanel._toolbar` Label at `GridPanel.cs:78`. Receives `(focused panel, cursor cell, session.SplitMode)` and renders the verbs available right now: `[1/E Open] [2 Half] [3 Split] [4 Sort] [Q Back]` on a bag cell; `[1 Drop] [4 Sort]` on an empty cell; the inline split editor when `SplitMode` is active. Always one row, single source of truth for "what can I do right now."
- **`FocusedDescriptionView`** — standalone fixed pane, always visible, owned by `GameView`. Subscribes to `FocusChanged` and `CursorMoved` events from every panel and re-renders for the focused panel's cursor cell. Position: **bottom of the left column, directly above the global command strip**, full left-column width, fixed **8 rows** tall. Content layered top-to-bottom: item name (colored to match its grid glyph), category + count, 1–2 line description, recipe list / crafting progress for facility cells. Same content set as today's `ItemDescriptionView`, but always-on and focus-tracking instead of B-bound.

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

## Decisions (2026-05-11)

- **Cell aspect:** 3×2 (visually square, ~33×25 capacity). Bags and wildernesses cap at 16×16 by design; larger wildernesses stitch discrete rooms via portals, so dense 2×1 is not needed.
- **Glyph differentiation:** color **and** glyph — category drives color, item-type drives the glyph character.
- **Command strip:** one **global** strip at the bottom of `GameView`, full width. Single source of truth.

## Open Questions

- **Breadcrumbs:** stay in `GridPanel` title row, or move to the command strip? Title row is cleaner; recommend leaving them.
- **R (CycleRecipe) on non-B focus:** does it operate on focused panel (per `panel-ux.md` §Recipe Selector)? Recommend yes — folds into change 3.

## What stays untouched

- `GameController` / `GameSession` / `GameState` zipper pattern — clean separation, well-tested
- `TuiTestHarness` and existing render tests
- `GameKey` / `ClickType` enums in `Pockets.Core.Models`
- `Pockets.Godot` parallel renderer
- Tab/Shift-Tab focus model and per-panel cursor (already correct per `panel-ux.md`)
