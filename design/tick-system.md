# Tick System: Hierarchical Fixed-Point Time Propagation

## Concept

Time in Pockets is measured in integer ticks, propagated recursively down the bag tree. The top-level time source (real-time clock, turn counter, music beat, etc.) generates ticks, which flow downward: bag → cells → contained bags → their cells, and so on. Each level can transform the tick count before passing it to children, enabling time dilation, pausing, or rhythm-driven mechanics per bag.

Ticks are the universal unit for all time-dependent mechanics: crafting duration, conveyor speed, spawner intervals, wilderness refresh, portal cooldowns, etc. The tick system doesn't define what generates ticks — only how they propagate and are consumed.

## Core Design Decisions

### Fixed-Point Integer Ticks (decided)

Ticks are `int` (milliseconds or abstract units), not `float`. Reasons:
- No floating-point error accumulation across long sessions
- Deterministic behavior — same inputs always produce same outputs
- Simpler equality comparisons and state snapshots (important for undo)

If the top-level time source is real-time, it floors the floating-point delta and holds the fractional remainder as an accumulator for the next frame:

```csharp
// At the game loop level
double accumulatorMs = 0.0;

void OnFrame(double deltaSeconds)
{
    accumulatorMs += deltaSeconds * 1000.0;
    int wholeTicks = (int)accumulatorMs;
    accumulatorMs -= wholeTicks;
    if (wholeTicks > 0)
        rootBag.Tick(wholeTicks);
}
```

### Recursive Propagation

Ticks flow down the bag hierarchy. Each bag/cell/frame that cares about time implements a tick interface:

```
ITickable
└── Tick(int deltaTicks) → returns updated self (immutable)
```

Propagation order within a bag:
1. Bag receives `Tick(n)`
2. Bag applies its own time transform (e.g. `n * 2` for double-speed, `0` for paused)
3. Bag iterates cells, ticking each CellFrame that implements ITickable
4. Bag iterates cells with ContainedBags, ticking each contained bag
5. Returns updated Bag with all mutations applied

### Time Transforms Per Bag

Each bag can optionally define a time transform, enabling:
- **Paused bag**: transform returns 0 (contents frozen)
- **Fast bag**: transform multiplies ticks (accelerated crafting)
- **Slow bag**: transform divides ticks with remainder tracking (decelerated)
- **Rhythmic bag**: transform gates ticks to beat intervals (music-driven)
- **Turn-based bag**: ignores real-time ticks, only ticks on player action

```
TimeTransform
├── Multiplier: int (numerator)
├── Divisor: int (denominator, default 1)
└── Accumulator: int (remainder from integer division)
```

Example: a bag with Multiplier=1, Divisor=2 runs at half speed. It accumulates ticks and only passes them through when the accumulator reaches the divisor.

## Architecture

```csharp
public interface ITickable
{
    /// <summary>
    /// Advance by deltaTicks. Returns updated instance.
    /// </summary>
    ITickable Tick(int deltaTicks);
}
```

Bag gains an optional `TimeTransform` and tick propagation:

```
Bag
├── TimeTransform? Transform       (null = passthrough, ticks flow unmodified)
├── TickAccumulator: int            (remainder from divisor-based transforms)
└── Tick(int deltaTicks) → Bag      (propagates to cells/frames/children)
```

CellFrame subtypes opt in:

```
SpawnerCellFrame : CellFrame, ITickable
├── TicksRemaining: int
└── Tick(int deltaTicks) → spawns item when TicksRemaining reaches 0

ConveyorCellFrame : CellFrame, ITickable
├── TicksPerMove: int
├── Accumulator: int
└── Tick(int deltaTicks) → moves item to next cell on threshold

FacilityState (on crafting bag)
├── CraftingProgress: int           (ticks remaining)
└── Tick(int deltaTicks) → decrements progress, produces output at 0
```

### Interaction with Undo

Ticking mutates state. The current undo system snapshots entire GameState. As the game grows, undo needs to become **localized** — scoped to the subtree affected by a player action, while the rest of the world continues ticking forward.

**Progression of undo models:**

1. **Current (Stage 2):** Full state snapshot. Simple. Works because there are no ticks yet.
2. **Near-term:** Snapshot post-tick state at each player action. Undo restores the snapshot. Ticks that occurred between actions are baked into the snapshot — undoing also undoes those ticks, which is acceptable for slow tick rates.
3. **Mature:** Localized undo. The undo system records which subtree a player action affected. On undo, only that subtree is rewound to its pre-action state. The rest of the tree either continues forward or is replayed from the snapshot with ticks applied. Delta-based tick propagation makes this natural — you replay the subtree from a snapshot with different deltas (the original action removed, subsequent ticks reapplied).
4. **Advanced:** Rewind-and-replay. Undo rewinds the affected subtree, removes the player action, then fast-forwards back to the current tick count. This requires storing the action + tick deltas as a log rather than just state snapshots. Most complex, but enables undoing an action from N ticks ago while preserving all subsequent time progression.

Delta-based time feeds are key to making models 3 and 4 work — since each bag receives ticks as a delta from its parent, localizing the replay to a subtree is just re-ticking that subtree from the snapshot with the corrected inputs. No global clock to rewind.

## Methodology Fit

- **Builds on:** bag hierarchy, Cell Frames, immutable state model
- **Enables:** crafting timers, conveyor movement, spawner intervals, wilderness refresh, any time-dependent mechanic
- **Key property:** time is local to each bag. A paused bag's contents don't tick. A fast bag accelerates everything inside it. This creates gameplay around time manipulation without special-casing.
- **Emergent potential:** High. Time-dilated bags are items the player can craft/find. A "time crystal" bag that runs 10x speed makes crafting faster. A "stasis" bag freezes contents (preservation). Combined with portals, items flowing between bags experience different time rates.

## Open Questions

- What is the base tick unit? Milliseconds (ties to real-time), abstract game ticks (ties to game loop), or configurable?
- Should the game loop tick at a fixed rate (e.g. 20 ticks/sec like Minecraft) or variable?
- How does the undo system handle tick state? Snapshot-after-tick seems simplest.
- Should tick propagation be breadth-first (all bags at depth N before depth N+1) or depth-first (current implementation suggestion)? Probably doesn't matter for correctness, but could matter for determinism if cross-bag effects exist.

## Status

Proposed. Foundational system needed before crafting, conveyors, spawners, or any time-dependent mechanic. The interface and propagation model can be implemented incrementally — start with Bag.Tick passthrough, add transforms later.
