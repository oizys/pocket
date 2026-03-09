# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

Pockets is a grid-based inventory puzzle game where everything exists inside nested bags. The prototype is a C# TUI (Terminal UI) app. The full design document is in `Pockets.md`.

**Tech stack:** C# 12 / .NET 8. Eventually targets Unity3D or Godot, but the prototype uses a console TUI (Terminal.Gui 1.19.0) for fast iteration.

## Development Rules

- **TDD for all basic functions.** Write tests before implementation.
- **Use sub-agents (Sonnet model)** to implement tests and function code.
- **UI/UX testing:** Run the game in a separate terminal. Automate via key/mouse signals or a WebSocket API. Use screen/buffer captures to verify correctness.
- **Git commits** after each successful build → test → run cycle (known good builds only).
- **Stage-based development:** Each stage must be fully functional. Keep stage builds and task lists in separate sub-folders so earlier stages can be revisited.
- **Plan features first:** Document plans with ASCII diagrams. Break implementation into sub-tasks with testing plans. Ask about ambiguities before coding.
- **Design ideas go in `/design/`:** Each new game design idea gets its own markdown file following the template (Concept, Cohesion, Intuition, Architecture, Methodology Fit, Status). Update `/design/INDEX.md` with a one-line entry. Commit immediately after creating the file — the user will review post-commit.
- **`Pockets.md` is the concrete spec.** Only add details to `Pockets.md` when an idea is approved and scheduled for implementation. Design ideas stay in `/design/` until then.

## Code Conventions

- Functional programming style preferred; use LINQ extensively
- **LINQ Method Syntax only** (`.Where()`, `.Select()`) — never Query Syntax (`from`, `select`, `where`)
- Inline documentation above each function describing its usage
- Item data files are human-editable markdown stored in `/data`

## Architecture

### Core Domain Model

- **Grid:** X×Y collection of Cells (row-major: on a 10×4 grid, cell 12 = row 1, col 2)
- **Cell:** Holds one ItemStack. Can have a category filter restricting allowed items.
- **ItemType:** Either *stackable* (identical, no custom properties, default max 20 per stack) or *unique* (1 per cell, custom properties)
- **ItemStack:** An ItemType + count. Stacking merges counts up to max, producing a remainder.
- **Bag:** An openable item containing a Grid and an array of ItemStacks. Bags nest inside other bags. Each bag has an environment type and color scheme.
- **Cursor:** Points to the active bag and cell. Wraps around grid edges.
- **Breadcrumbs:** A stack of Cursors for navigating nested bags. Popped when leaving a bag; can't leave the topmost bag.
- **Selection:** Any number of cells in the active bag. If nothing is selected, the Cursor cell is implicitly selected.

### Item Acquisition Algorithm

Items are placed starting at cell 0 (top-left), skipping filtered/mismatched cells, merging into matching stacks up to max, then continuing. Multiple stacks each restart from cell 0.

### Tools and Action Queue

Players act through Tools with a speed attribute. Tool actions operate on the Selection (whole selection or per-item). Per-item actions queue individually. The queue is cancellable. Basic tools: Grab (cut), Drop (paste), Split, Quick Split, Sort, Acquire Random (debug).

### UI Layout (4 panels)

```
--------------------------------------------------
| Top: menu / game settings                      |
--------------------------------------------------
| [ Breadcrumbs           ] | [ Action Queue   ] |
| Active Grid               | World              |
| Cursor Item Description   |                    |
--------------------------------------------------
| Bottom: toolbar, hotkeys, status bar           |
--------------------------------------------------
```

### Data Files

Markdown files in `/data`, one per definition, loaded at startup. Item definitions require: Name (2-4 words, last word = primary noun), Category (Material, Weapon, Structure, Medicine, etc.), stackable/unique flag. Optional: 1-2 sentence description.

## Build Commands

```bash
dotnet build Pockets.sln        # build all projects
dotnet test                     # run all tests
dotnet run --project src/Pockets.App  # run the TUI app
```

## Current Stage: Stage 1

- Single bag with 8×4 grid and moveable cursor
- Arrow keys move cursor with wrap-around
- Single item stack selection only
- 15 randomly generated item type definitions in `/data`
- Game starts with 4-10 random stacks from the data list
- Ctrl-Q quits

### Tools and Navigation

| Key | Tool | Behavior |
|-----|------|----------|
| 1 | Grab | Remove cursor item, place in Hand bag (merge if same type). No-op if hand full |
| 2 | Drop | Place hand items at cursor (merge if same type), remainder acquires from cell 0. No-op if type mismatch or bag full |
| 3 | Quick Split | Split cursor cell in half; left stays, right goes to Hand bag |
| Shift-3 | Modal Split | Dialog to adjust split amount; right portion goes to Hand bag |
| 4 | Sort | Sort & merge entire bag by (Category, Name) |
| 5 | Acquire Random | Debug: add 1 random item to grid |
| E | Enter Bag | Open bag at cursor, push breadcrumb, reset cursor to (0,0) |
| Q | Leave Bag | Pop breadcrumb, return to parent bag with saved cursor position |
| Ctrl-Z | Undo | Restore previous GameState from snapshot stack (max 1000 deep) |

### Hand Model

`GameState.HandBag` is a real `Bag` with a configurable number of slots (default 1, set via `GameConfig.HandSize`). Grab performs a true cut — items are removed from the grid and placed in the hand bag. Drop places hand items at cursor, with remainder acquired from cell 0. All tool methods return `ToolResult(State, Success, Error?)` instead of raw GameState.

### Bag Navigation

`ItemStack.ContainedBag` holds a nested `Bag` for bag-type items — the bag travels with the item through grab/drop/sort. `Cell.HasBag` is a convenience property that checks `Stack?.ContainedBag`. `GameState.Breadcrumbs` is an `ImmutableStack<BreadcrumbEntry>` tracking the path from root to active bag. `ActiveBag` computed property follows the breadcrumb trail. `WithActiveBag(Bag)` propagates changes back up to root (zipper pattern). `GameSession` wraps `GameState` with undo stack + action log.
