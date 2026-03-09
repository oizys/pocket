# Cell Frames: Separating Cell Logic from Cell Contents

## Concept

Each Cell is conceptually two pieces: a **Frame** (the cell's behavior, constraints, and visual chrome) and an **ItemStack** (the cell's contents). A Frame is an optional field on Cell linking to a `CellFrame` object. Each cell can have at most one Frame, which keeps visualization simple and prevents unreadable stacks of modifiers.

CellFrame is a base type that can be subclassed for different behaviors (e.g. `ConveyorCellFrame`, `SpawnerCellFrame`). The base type supports:
- **Locked** flag: whether the player can remove or modify the frame
- **Rendering hints**: for TUI, this could be alternate box-drawing characters or ANSI colors; for graphical targets, a visual background/border treatment (hence "Frame")

This cleanly separates what a cell *does* from what a cell *holds*, and opens up player-driven grid logic (placing conveyor belts, filters, etc.) without bloating the Cell record with feature-specific properties.

## Cohesion: Very High

Frames are a natural extension of the existing Cell model. The current `CategoryFilter` on Cell is effectively a proto-frame — a constraint on what the cell accepts. Frames generalize this into a proper extensibility point. The one-frame-per-cell rule keeps the model simple and the rendering tractable.

## Intuition: High

"The cell has a border/background that tells you what it does." Players already understand UI affordances like colored borders, icons, and silhouettes. A tool-only cell with a hammer silhouette background is immediately legible. Conveyor arrows on a cell frame communicate direction without explanation.

## Architecture

```
Cell
├── Stack: ItemStack?       (contents — what's IN the cell)
├── Frame: CellFrame?       (behavior/chrome — what the cell DOES)
└── (CategoryFilter migrates into Frame eventually)

CellFrame (base)
├── IsLocked: bool
├── RenderHint: FrameRenderHint   (box chars, colors, or future graphics ref)
└── virtual OnItemEnter/OnItemLeave/OnTick hooks

ConveyorCellFrame : CellFrame
├── Direction: Direction
└── Speed: int

SpawnerCellFrame : CellFrame
├── SpawnType: ItemType
├── Interval: int
└── MaxCount: int

FilterCellFrame : CellFrame
├── AllowedCategory: Category?
├── AllowedType: ItemType?
└── (replaces current CategoryFilter)
```

### Rendering

- **TUI (current):** Frame determines box-drawing character set and/or foreground/background colors. A conveyor cell might use arrow-style borders (`→`, `↓`). A locked frame might render with double-line box characters (`╔═╗`). A filter frame might tint the background.
- **Graphical (future):** Frame provides a background texture/sprite and border style. The ItemStack renders on top.

### Migration Path

1. Add `CellFrame? Frame` to Cell record (nullable, backward-compatible)
2. Existing `CategoryFilter` continues to work alongside Frame initially
3. Later, migrate `CategoryFilter` into `FilterCellFrame` and remove the field
4. Player-placed frames come after core frame behaviors are proven

### Scaling Considerations

If one frame per cell proves too limiting, three escape hatches:
1. **List of frames** — flexible but complex to render and reason about
2. **Fixed slots** — e.g. one "constraint" frame + one "behavior" frame (orthogonal concerns)
3. **Composite frame** — a single frame that internally composes multiple behaviors

Option 2 (orthogonal slots) is probably the right middle ground if needed, since constraints and behaviors are genuinely independent concerns.

## Methodology Fit

- **Builds on:** Cell, Grid, rendering system, tool dispatch
- **Enables:** player-modifiable grid logic, conveyor belts, spawners, filtered slots with visual affordance, automation
- **Reduces friction:** separates concerns that would otherwise accumulate as Cell properties, keeps each behavior in its own class
- **Emergent potential:** High. Player-placed frames turn grids into programmable systems. Combined with nested bags, you get player-designed machines (a bag with conveyor frames = an auto-sorter)

## Implementation Decisions

- **Sealed abstract record pattern** (decided): Use `abstract record CellFrame(bool IsLocked = false)` with `sealed record` subtypes (ConveyorFrame, FilterFrame, etc.). This gives immutability, structural equality, `with` expressions, and switch-expression pattern matching — effectively C# discriminated unions. Each subtype is sealed to keep the set closed.
- **CategoryFilter stays separate initially**: Migrate into a `FilterFrame` later once core frame behaviors are proven. No reason to add risk for no immediate gain.
- **One frame per cell, with orthogonal slots as escape hatch**: If we later need both a constraint and a behavior on one cell, split into two typed slots (e.g. `ConstraintFrame?` + `BehaviorFrame?`) rather than a general list.

## Open Questions

- When do players unlock frame placement? Too early might overwhelm; too late and grids feel static.
- How do frame behaviors interact with the tool/action queue? Conveyors and spawners imply a tick/turn system.

## Status

Proposed. Natural fit once core inventory mechanics are stable. Could be introduced alongside or just before automation features.
