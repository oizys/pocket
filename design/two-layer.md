# Two-Layer Architecture: Simulation (L1) + Realization (L2)

**ID:** #23 · **Phase:** Early-Mid (foundation for realtime, belts, entropy, Godot W-view)
**Status:** Ratified 2026-10-27 (D1–D11 accepted with amendments A1–A6). Phase 0 done:
this doc plus spikes A/B/C in [`spikes/`](../spikes/README.md). The vault proposal
(`~/obsid/paths/projects/pockets/two-layer-architecture.md`) is now a mirror of this file.
**The repo copy is the source of truth.** **Phase 1 ✓ (2026-10-28):** the Step contract and
event stream are built, golden-neutral (slices 1.0–1.9,
[two-layer-phase1-plan.md](two-layer-phase1-plan.md); drift note:
[two-layer-phase1-drift.md](two-layer-phase1-drift.md)).

---

## Concept

**L1 is the game. L2 is a performance of it.**

- **L1 Simulation** is discrete, integer-based, deterministic and testable with ASCII
  grids. It is the only thing that decides outcomes.
- **L2a Fine state** is the small amount of sub-cell state that must be exact and saved,
  such as a vase's offset on a table or a free-moving body's sub-cell position. It is
  stored as fixed-point and **L1 never reads it**.
- **L2b Presentation** covers animation, easing, camera, particles and the 2D/3D choice.
  It uses floats, has no determinism requirement, and is never saved.

The two layers have exactly one connection in each direction:

```
            intents (validated)                 events (what happened + why)
  ┌────────┐ ─────────────────▶ ┌────────────┐ ───────────────────────────▶ ┌──────────────┐
  │ Input  │                    │ L1  Step() │                              │ L2 timeline  │──▶ Godot / TUI
  │ (L2)   │ ◀── dry-run ────── │ pure, int  │ ◀── never reads ── L2a fine  │ pose(t), pure│
  └────────┘   (prediction)     └────────────┘     state (fixed-point)      └──────────────┘

                 Step(L1State, Intent[]) → (L1State', SimEvent[])
```

For a game-dev reader: this is **server-authoritative netcode, but single-player**. L1
plays the server and L2 plays a predicting client. Every problem below (prediction,
reconciliation, lag compensation, interpolation, catch-up) already has a name and a
proven answer, and the same structure keeps multiplayer possible later (§Multiplayer
guardrails).

## Cohesion

- It builds on what exists. `GameSession.ApplyResult` is already the single choke point
  for every state change. `TickMode.Rogue`/`Realtime` and the injected `IGameClock`
  already separate "when time moves" from "what time does". Snapshot parity already
  proves cross-driver determinism. Phase 1 (`Step`) mostly renames and widens this.
- It formalizes `design/tick-system.md` (integer ticks, per-bag `TimeTransform`) and
  `design/entropy-design-context.md` §3.2/§4 (closed bags fast-forward, lints exist only
  while observed). Those ideas become the **time-behaviour classes** (§Time).
- Belts and arms (`design/conveyors.md`) and `CellFrame` subtypes (`design/cell-frames.md`)
  become **transfer rules**. Plaque primitives (corner accumulator, edge creeper, decay)
  become **field rules**.
- The ASCII-grid testing culture extends to L2. A realization is testable as a "zoomed"
  ASCII grid (one cell → 4×4 sub-cells), and the TUI stays a valid n=1 realization.

## Intuition

For the player, nothing changes in kind. Things just start to *move* between the states
they already understand. A belt visibly carries its item right, a lint visibly lunges and
returns, and a vase sits slightly off-centre. When the player presses a key mid-animation,
the world snaps forward and obeys immediately (Brogue/DCSS feel). A bag that has been
closed for an hour "catches up" when opened, so the plaque is there and the belts have
delivered, as if it had kept running all along, because (for rules that allow it) it
effectively did.

For a developer: **if a fact matters to gameplay, it is L1, it is an integer, and it has
an ASCII test.** Everything else is a performance.

---

## Ratified decisions (D1–D11)

| # | Decision | Ratified answer |
|---|---|---|
| D1 | Cursor or body | A body only in world (W) bags. Inventory bags stay cursor-driven. |
| D2 | Movement model | **A) grid-locked + interpolation** is the default; rogue mode requires it. **B) free movement + grid occupancy** is an **early** per-bag realtime experiment behind the same intents (amendment A1, Phase 2). |
| D3 | Update semantics | Field rules = gather CA (double-buffered). Transfer rules = propose → resolve → commit. |
| D4 | Subdivision | One fixed n = 16 sub-units per cell (fixed-point) for L2a. Floats only in L2b. |
| D5 | Can L1 read L2a? | **Never.** Promote the fact to L1 instead. |
| D6 | Realtime step rate | 8 Hz (125 ms) outer step + dry-run prediction. Spike B: the step itself is cheap at 8 Hz: ~26 µs p50 for a fully churning 32×32 bag in today's immutable style (§Spike B). Bags may run slower (A5). |
| D7 | Input during animation | Snap all in-flight animation to its end state, then commit the intent. Never block. |
| D8 | L1→L2 contract | Events. Snapshots are kept for reconciliation, undo and parity. |
| D9 | Undo under realtime | Rogue: full undo as now. Realtime: checkpoint rewind only; **L2 history is lossy** (A3). |
| D10 | Cross-bag neighbourhoods | None, except explicit ports (seepage, portals, belts into bag-items). |
| D11 | Where realization math lives | Pure `Pockets.Realization` library, engine-neutral (A2). |

## Amendments (Aaron, 2026-10-27), and where each is answered

| # | Amendment | Answered in |
|---|---|---|
| A1 | Free movement is an **early** experiment; grid-locked stays the default | D2 above; §Movement; Phase 2 |
| A2 | Lean 3D for W-view; poses engine-neutral (cell + sub-cell + optional height/facing) | §L2 Realization: Pose |
| A3 | Lossy L2 history is acceptable (undo resets in-flight motion to cell centre) | §History & undo |
| A4 | Immutable state vs 8 Hz is first-class: hot working set inside Step + event log + snapshots | §State representation (+ Spike B numbers) |
| A5 | Variable / per-bag / offline / on-demand time is a core principle; rules declare a time class | §Time (+ Spike C) |
| A6 | Keep multiplayer possible (lockstep or async); guardrails only, no MP work | §Multiplayer guardrails |

---

## Architecture

### Layer table

| | L1 Simulation | L2a Fine state | L2b Presentation |
|---|---|---|---|
| Lives in | `Pockets.Core` | `Pockets.Core` (separate record, keyed by entity) | `Pockets.Realization` (pure) + frontend |
| Numbers | int, cells, ticks | **fixed-point**, 1 cell = 16 sub-units | float, anything |
| Deterministic | yes, across machines | yes, across machines | no requirement |
| Saved / replayed | yes (snapshot + event log) | yes (snapshot + event log) | **never**: rebuilt from events |
| Read by L1 rules | – | **never** | never |
| History | exact | exact | **lossy** (A3) |
| Tested by | ASCII grids, journeys, goldens | zoomed ASCII (1 cell → 4×4), "fuzz L2a ⇒ same L1" invariant | timeline unit tests, stills |

### The Step contract

```
Step(L1State s, IReadOnlyList<Intent> intents) → (L1State s', IReadOnlyList<SimEvent> events)
```

- **Pure from the outside.** The same `(s, intents)` always gives the same `(s', events)`,
  whatever machine, culture or thread runs it. How it computes that internally is free
  (§State representation).
- **Intents are the only input.** Key presses, the mouse, the WebSocket debug API, journey
  scripts and (one day) a remote peer all become `Intent`s. The clock is not an input.
  An outer step happens because the scheduler (below) asked for one, and the step
  number is part of `s`.
- **Rogue mode:** the scheduler calls `Step` once per committed player intent. Rogue mode
  is "realtime with a clock that only advances on input".
- **Realtime mode:** a fixed-rate accumulator calls `Step` at 8 Hz. Frame time is
  floored into whole steps and the fraction carries over (tick-system.md's accumulator).
- One outer step may run **0, 1 or k sub-steps per bag** (§Time).

**As built in Phase 1.** The function is
`Simulation.Step(SimState, IReadOnlyList<Intent>) → StepResult<SimState>` in
`src/Pockets.Core/Sim/`. It is named `Simulation`, not `Sim`, because a type `Sim` would be shadowed by
the `Pockets.Core.Sim` namespace. `SimState` = `GameState` + `TickCount` + `Elapsed` + `SplitMode` +
`RecipeMenu` + `Rng`, with `SimContent` (recipes, facility map, dialogue beats, `TickMode`) as read-only
L1 content. `Intent` has **32 data-only cases**: one per controller path, each naming its target
`LocationId`, with no delegates and no GUIDs (the transitional `Intent.Tool` closure was deleted in 1.3).
`GameSession.Apply(Intent)` is the only public mutation. The session is the shell around Step: undo
stack, action log, intent log, event log. Two Phase 1 limits still apply. The step rate is still 1 Hz
(`TickLengthMs = 1000`, P1-D4), not 8 Hz. Realtime player intents apply immediately with no time
advance (P1-D2), so there is no intent queue or prediction yet.

```
  frame loop (L2b, ~60 Hz)            scheduler (L1 boundary)               Step (L1, pure)
  ───────────────────────            ───────────────────────               ───────────────
  accumulate dt ──────────▶ whole steps? ──yes──▶ drain intent queue ──▶ for bag in ScheduleOrder:
  sample poses(t) ◀─ events ◀──────────────────────────────────────────     substeps = rate(bag, step)
                                                                            run rules × substeps
                                                                          emit events, step++
```

### Rule kinds

There are two kinds, and **every rule is exactly one of them.** This corrects the original
"Moore last-frame for everything" sketch.

**Field rules (gather).** Plaque spread, blooming, growth and decay. Each processed cell
computes **its own** next value from its Moore neighbourhood in the **previous** buffer.
These rules are double-buffered and order-independent, so they are deterministic with no
extra work. A field rule writes only its own cell.

```
 prev buffer          next buffer
 . 3 .                . . .
 2 [5] 1     ──▶      . [5+r+#(nbrs≥T)] .      (reads 9 cells of prev, writes 1 cell of next)
 . . 4                . . .
```

**Transfer rules (propose → resolve → commit).** Conveyors, arms, lint and body movement,
acquisition. Any change that **writes to another cell** can conflict, so a transfer rule
runs in three phases:

1. **Propose:** each actor emits `(from, to, priority-key)`.
2. **Resolve:** walk each transfer chain **head → tail** (downstream first, memoized
   `Vacates(cell)` with cycle detection). A move succeeds if its target is empty **or is
   vacated in this same step**, and the mover wins the target's deterministic tie-break.
3. **Commit:** apply all winning moves at once, and emit `Moved` / `Blocked` events.

**Belt rulings (Aaron, ratified 2026-10-27):**

- **R1. A completely full belt loop stops.** If every cell of a ring is occupied, nothing
  can vacate first, so the ring stalls and each cell emits `Blocked(full)`. A ring with at
  least one gap rotates. This needs no SCC detection: an on-stack hit in `Vacates` simply
  counts as "not vacating".
- **R2. Merges use fixed side priority.** For a target belt flowing in direction D, the
  order is **straight-through** (the input from behind) **> left side > right side**
  (> head-on, which is degenerate). Targets with no direction (sinks) use a fixed N, E, S, W.
  The main line always wins, and a side input enters **only when the main line leaves room**.
  - **Consequence, accepted:** a saturated main line **starves** its side inputs
    indefinitely (spike A: over 100 steps the main source delivers 100 and the side 0). The
    player's eventual tool for this is a buildable **priority splitter / merger** frame (or
    similar), not a change to the rule.
  - **Rationale:** it is deterministic **and stateless**. No per-merge cursor (`LastWinner`)
    lives in L1 state, so there is nothing to save, snapshot, diff or replay. Each step's
    outcome depends only on occupancy. That is also what makes merge networks **cheap to
    catch up exactly**. A network's whole state is occupancy plus the phase of its feed
    periods, so with periodic feeds it falls into a repeating cycle that catch-up can skip
    (*cycle skip*, §Time; measured in spike C finding 7).
  - **Correction, measured:** the main line is **not** independent of its side inputs.
    Once the sink backlogs, side items already queued downstream of the merge delay main
    items by up to 24 steps (8 fewer main deliveries). So "the main line keeps its linear
    recurrence" holds only while the sink keeps up.

Spike A showed why the resolve phase is required: the naive "move if the target was empty
last frame" rule *is* Rule 184. A full belt starts with a ripple and settles at half
throughput. Chain resolution moves a full belt as a single unit. (Details in §Spike A.)

**Active set.** A cell is processed if it is flagged (it has a frame or ticking content)
or if any Moore neighbour changed in the previous step. Everything else costs nothing.
Spike B measured the worst case (all 1024 cells every step), so the active set is a
multiplier on top of those numbers, not a prerequisite.

**Rule declaration.** Every rule registers with its kind, its **time class** (§Time) and
its event kinds:

```csharp
// sketch, Phase 3. As built in Phase 1: IRule { Kind; Time; MaxDelta; EventKinds },
// ITickRule.Apply(GameState, RuleContext) as one coarse pass, registered in RuleRegistry.TickPhase
interface IRule { RuleKind Kind { get; } TimeClass Time { get; } }
interface IFieldRule    : IRule { int Next(in Neighbourhood prev, CellCoord c); }
interface ITransferRule : IRule { void Propose(in BagView s, ProposalSink sink); }
```

### Event contract

L2 animates from **events, not diffs.** A diff loses the **cause** (walk vs. teleport vs.
belt shove) and the **direction**. Direction is exactly the conveyor readability problem:
`Pushed(belt, dir)` lets even a single intermediate frame read the right way.

```
SimEvent = (Step, BagId, Seq, Kind, payload…)          // Seq: order within (Step, Bag)

Moved(entity, from, to, cause)        cause ∈ {walk, belt, arm, push, teleport, acquire}
Blocked(entity, from, to, reason)     reason ∈ {occupied, lost-merge, full, filter}   → L2 "bump"
Spawned(entity, at, cause) / Despawned(entity, at, cause)
Emitted(source, entity) / Consumed(sink, entity)
FieldCrossed(cell, field, band, dir)  plaque/bloom threshold crossings, NOT every increment
Attacked(a, b, dmg) / Died(e)
BagStepped(bag, substeps, mode)       mode ∈ {stepped, caught-up}   → L2 knows to snap, not animate
IntentRejected(intent, reason)        → L2 reconciles its prediction
```

Contract rules:

1. **Completeness:** replay(intents) ≡ state; events cover every change. Replaying the
   intent log from snapshot `s0` must give `s_n` exactly, and every cell, slot or cursor
   that differs between `s` and `s'` must be named by at least one event of that step
   (both checked in CI, Phase 1). *(Amended by P1-D1, ratified 2026-10-28. The stronger
   form, replaying `events[0..n]` onto `s0` gives `s_n` with no intents, moves to Phase 7,
   when the event log becomes the save format (A4).)*
2. **Order is part of the contract:** `(Step, ScheduleOrder(bag), Seq)`. It never depends
   on dictionary or hash order.
3. **Granularity is semantic.** Field rules emit **threshold crossings**, not per-tick
   increments. The log stays proportional to *meaningful* change (spike B: ≈ 203
   events/step for a churning 32×32 bag with 8 belts).
4. **Caught-up steps are flagged.** A catch-up yields `BagStepped(mode=caught-up)` plus
   the *net* events, which L2 plays as a snap or fade, never a 10-minute replay.
5. Snapshots stay the reconciliation baseline. Parity gains an **event-stream golden**
   next to the view-model golden (Phase 1).

**As built in Phase 1** (`Sim/SimEvent.cs`). The sketch above is the Phase 3+ target. Phase 1 has
**29 cases**: 28 that Step emits, plus `Rewound(toStep)`, which only the shell emits on undo.
- Of the 28, `BeltAdvanced(chain, dir, count, head)` is reserved (P1-D3) and nothing emits it yet.
- The other 27 name today's mechanics:
  - envelope: `IntentApplied`, `IntentRejected`, `TimeAdvanced`;
  - cursor and navigation: `CursorMoved`, `Bumped`, `BagEntered`/`Left`, `PanelOpened`/`Closed`;
  - stacks: `StackMoved`/`Split`/`Merged`/`Removed`, `Acquired`, `BagRearranged`;
  - crafting: `CraftStarted`/`Progressed`/`Aborted`/`Completed`, `RecipeSelected`;
  - plants: `PlantGrew`, `Harvested`;
  - chrome and dialogue: `UiTriggerFired`, `DialogueQueued`/`Advanced`, `RecipeLearned`, `ZoneReached`.
- Tool events are **derived** (`EventDeriver`) from the pre- and post-tool state plus the intent.
  Rule events come through `RuleContext.Emit`.
- `Seq` is step-global (one canonical pass), and `(Step, Seq)` is unique only per Step call, because
  untimed intents share the next timed step's number.
- The canonical text is `EventText`: `step seq Kind field=value…`, invariant culture, with bags renamed
  `bag#N` by first appearance (P1-D8).

### State representation: immutable model vs. hot working set (A4)

This is the first-class concern from A4. There are three options:

```
 (A) Fully immutable (today)            (B) Hot working set + event log         (C) Hybrid per bag
 ─────────────────────────              ────────────────────────────────        ──────────────────
 GameState = records +                  Observed/active bags: mutable SoA        Inventory/rogue bags: (A)
   ImmutableArray<Cell>                   arrays owned by the scheduler,         Realtime/active W bags and
 Step returns a new tree                  mutated in place inside Step           belt-heavy bags: (B), promoted
 undo = keep old roots                  Step appends events to an              on observe/activate and
                                          append-only log                        demoted (snapshot) on close
                                        Every N steps: immutable snapshot
                                        GameState = latest snapshot + log tail
                                        undo/rewind = snapshot + replay
```

| | (A) Immutable | (B) Hot set + log | (C) Hybrid |
|---|---|---|---|
| Purity seen from outside | yes | yes (Step owns the mutation; callers see versions) | yes |
| Alloc / step (32×32, spike B) | 72 KB | 4–5 KB (0 without retention) | per bag |
| Time / step (32×32, spike B) | 26 µs p50 / 55 µs p99 | 3.1 µs p50 / 4–15 µs p99 | per bag |
| Undo | trivial (keep roots) | snapshot + replay ≤ N steps | trivial for inventory |
| Fits today's code | 100% | Core rewrite of the hot paths | only hot bags change |
| Event log | optional | **is** the history | required for hot bags |
| Multiplayer (A6) | lockstep OK | lockstep + async replay native | OK |

**Recommendation: (C) hybrid. (B) is not mandatory for Phase 1**, but it is the target
for hot bags, and the rule engine (Phase 3) must be written against a `BagView` /
`BagWriter` abstraction so that the representation can be swapped per bag without
rewriting rules. Spike B's evidence:

- The immutable **builder** style (`ToBuilder` + `with` + `MoveToImmutable`) costs
  ~26 µs p50 per fully churning 32×32 step, which is about 0.02% of the 125 ms period. Even
  at a 10% sim share that is ~475 bags per period. **CPU is not the problem.**
- **GC tail is the problem.** 72 KB/step of short-lived records gives occasional
  8–12 ms steps. In a paced 60 s run of 50 bags at true 8 Hz, a1 had 0 missed deadlines but
  34/480 ticks with a GC pause > 1 ms, the worst at 17.7 ms. That is more than one 60 Hz frame
  if the sim shares the render thread.
- **Per-change `SetItem`** (today's `Grid.SetCell` idiom) is the real trap: 370 µs p50,
  10 MB/step and ~1000 gen0 GCs per 10 min. **It is banned in any Step hot path from
  Phase 1** (use builders or the hot set).
- The SoA hot set is ~8× faster (3.1 µs p50), allocates **0 B/step** without retention
  and 4–5 KB/step with a struct event log + snapshots, and does 0 GCs in the 1440/4800-step runs.
  The **checksums are bit-identical across all variants**, so mutation inside Step costs nothing in
  determinism.
- **Retention dominates memory, not stepping.** Ten minutes of one fully churning bag
  retained 45 MB of record-class events (a1) vs 23 MB of 20-byte struct events (b, snap/8).
  Event granularity (§Event contract rule 3) matters more than the state layout.

### Time: per-bag rates, offline and on-demand (A5)

**Principle.** Time is local to a bag (tick-system.md). The scheduler gives every bag a
**rate** and a **mode**. Each rule declares a **time class**, which says what the scheduler
is allowed to do with that rule when the bag is not stepped at full rate. Aaron has
accepted that this costs some automaton flexibility: a rule that cannot state a class
cannot run in unobserved bags.

```
 bag mode        when                                   what runs
 ─────────       ────                                   ─────────
 Observed        on screen / player inside              every rule, full rate (or bag's own TimeTransform)
 Background      explored, not visible, "warm"          rate 1/k, SUB-STEPPED (k steps every k-th outer step)
 Dormant         closed / far                           nothing; on observe → CatchUp(Δsteps)
 Frozen          stasis bag (TimeTransform = 0)         nothing, ever (Δ = 0)
```

**Time classes** (each rule picks one):

| Class | Contract | Catch-up cost | Examples (from spike C) |
|---|---|---|---|
| `ClosedForm` | `CatchUp(s, Δ) ≡ Step^Δ(s)` **exactly**, in O(cells) | O(1) per cell | plaque **accumulator** (P1), facility progress, decay |
| `EventSkippable` | `CatchUp(s, Δ) ≡ Step^Δ(s)` **exactly**, by jumping between internal events (threshold crossings, arrivals) or over repeating cycles | O(#events) or O(transient + cycle), not O(Δ) | plaque **spread** (P2) via threshold fast-forward; a **linear belt** (queue recurrence); **belt networks with side-priority merges and periodic feeds** (cycle skip) |
| `BoundedCatchUp(maxΔ)` | exact by sub-stepping up to `maxΔ`; beyond that, an **approximation with a declared tolerance** (or capped at a saturation state) | O(min(Δ, maxΔ) · active) | belt networks fed by **aperiodic** inputs (player, arm or RNG driven), arms with filters |
| `MustSimulate` (observed-only) | does not exist while unobserved; spawned fresh or from a deterministic seed on observe | – | lints (entropy §4 Schrödinger model), free-moving bodies |

**Cycle skip** is the catch-up algorithm for transfer networks (belts, merges, and periodic
sources and sinks). It is exact, and it is general for any bag whose rules are deterministic,
whose state is finite, and whose inputs are periodic.

```
 P = lcm(all source/sink periods)            key = (occupancy layer, step mod P)
 step at full rate; at every step ≡ 0 mod P:  seen[key]? ── no ──▶ record (step, counters, event index)
                                                  │ yes (first repeat t1 → t2)
                                                  ▼
 C = t2 − t1;  m = ⌊(Δ − t2) / C⌋ whole cycles:  counters += m · (per-cycle deltas)
                                                  events  += recorded cycle, re-timed by j·C (j = 1..m)
                                                  item ids += m · (emitted per cycle)   (identity is a counter)
 then step the remaining Δ − t2 − m·C (< C) steps at full rate.
 Cost = t2 + remainder ≤ transient + 2C steps, independent of Δ.
```

It is only correct because R2 merges are stateless. A round-robin cursor would have to be
part of the key, which still works but makes cycles longer. If the inputs are not periodic
(player-fed, arm-fed or RNG-driven), there is no cycle to find, and the bag falls back to
`BoundedCatchUp` (sub-step up to maxΔ).

**What "catch-up ≡ stepped" means** (the contract, by class):

- `ClosedForm` and `EventSkippable`: **bit-exact** L1 equality with the full-rate stepped
  result after Δ steps, for every Δ, including Δ not divisible by k. The **event multiset**
  may differ in granularity (net events instead of per-step), but the *net* events must
  match: the same items consumed, the same thresholds crossed, and the same order of
  crossings per cell. CI property test: random seeded states × Δ ∈ {1, k−1, k, 10k+3, big}.
- `BoundedCatchUp`: bit-exact for Δ ≤ maxΔ. Beyond that, the rule declares an invariant
  that must hold (conservation: items in = items out + items on belt; plaque monotone and
  ≤ Cap) and a **tolerance** on the observable totals (e.g. ±1 item delivered per merge).
- **Background at 1/k must be sub-stepped** (k ordinary steps every k-th outer step), never
  a "big step" with k× rates. Spike C: for the neighbour-dependent plaque spread (P2), a 1/8-rate
  big step ends up to 352 plaque units off on a 16×16 bag after 240 steps (87 of 256 cells wrong),
  and a "jump k cells" belt delivers 29 items instead of 76. Even the pure accumulator (P1),
  whose *end state* survives big-stepping exactly, has its threshold-crossing *times*
  quantized to the batch edge (up to k−1 steps late). Sub-stepping matched **exactly** in
  every case, provided the n mod k remainder is flushed on observe (without the flush the bag
  lags by exactly n mod k steps).
  A background bag therefore saves **latency, not total work**. The work saving comes
  from `Dormant` + `ClosedForm`/`EventSkippable` catch-up.

```
 outer steps:   1   2   3   4   5   6   7   8   9  ...
 observed bag:  ●   ●   ●   ●   ●   ●   ●   ●   ●         1 step each
 background/4:  ·   ·   ·  ●●●● ·   ·   ·  ●●●● ·         4 sub-steps every 4th: exact at multiples of 4
 dormant:       ·   ·   ·   ·   ·   ·   ·   ·   ·   ── observe ──▶ CatchUp(Δ) (closed form / event jumps)
```

**Seepage and ports** (D10) are the only cross-bag couplings. They are themselves
`EventSkippable` (discrete "burp" events, entropy §3.3), so a dormant child still
contributes the right burps to its parent on catch-up, in step order.

**Classification from spike C** (16×16 plaque bag, 12-cell belt, N ∈ {240, 241, 2400},
k ∈ {2, 4, 8}; details in §Spike C):

| Rule | Big step 1/k | Sub-step 1/k | Catch-up | Class |
|---|---|---|---|---|
| P1 plaque accumulator | end state exact, crossing times ±(k−1) | exact | closed form, exact, 1 op | `ClosedForm` |
| P2 plaque spread (Moore) | **diverges** (maxAbs 16→352 as k 2→8) | exact | threshold jumps, exact, 56–60 jumps for 240–2400 steps | `EventSkippable` |
| Linear belt (source→sink) | **diverges** (items lost/late) | exact | item recurrence, exact, cost = #items | `EventSkippable` |
| Belt network with R2 merges, periodic feeds | **diverges** (up to 2100 items, 2093 steps late at k=8) | exact | **cycle skip**, exact in 60/60 rows + a 600-run sweep; ≤ 299 steps for any Δ | `EventSkippable` |
| … same, aperiodic feeds | – | exact | no cycle to find | `BoundedCatchUp` |
| Lint random walk | stateful RNG diverges; counter RNG exact | exact only with counter RNG | none | `MustSimulate` |

### L2 Realization (D11, A2)

`Pockets.Realization` is a pure C# library with no Godot reference. It maps
`(events, policy, t) → Pose[]`.

```csharp
// engine-neutral pose (A2): no screen space, no 2D assumption
readonly record struct Pose(
    EntityId Entity, BagId Bag,
    int CellCol, int CellRow,          // L1 cell
    int SubX, int SubY,                // L2a/L2b offset, 1/16-cell units (fixed-point, D4)
    int? Height,                       // optional, 1/16-cell units: stacking, hops, a 3D W-view
    Facing? Facing);                   // optional 8-way (or yaw in 1/16 turns later)
```

- 2D Godot, 3D Godot and the TUI all **sample** poses. A 3D W-view maps
  `(col, row, sub, height)` to world space. Nothing in L1 or L2a knows which projection
  is used. **The 2D/3D fork stays open; nothing here bakes 2D in.**
- The TUI is the **n=1 realization** (events apply instantly). It can later show sub-cell
  motion with half-blocks or braille. Timelines are unit-tested with zoomed ASCII:

```
 cell (1,0)→(2,0), n=4, eased, t=0.5:     . . . . | . . . .
                                          . . . . | . . . .
                                          . . . . @ . . . .    ← at the boundary
```

**Rogue choreography:** one intent → L1 resolves the whole turn → L2 plays the events in
phases (the player's motion, then overlapping reactions, then ambient). **Snap & commit**
(D7): new input snaps every in-flight animation to its end state and commits immediately.

**Realtime prediction (D6):** on input, L2 dry-runs `Step` on a copy (cheap, deterministic;
in model (B) this means a snapshot-backed scratch copy). Motion starts this frame. A
misprediction is possible only if something else changes in the same step. L2 reconciles
by snapping or bending the animation into a bump (`IntentRejected` / `Blocked`).

**L2 → L1 adjudication:** L2 may run any hit detection or physics, but it only *proposes*
(`Intent.Attack(target, evidence)`). L1 checks reach, line of sight and liveness against
the **step that was on screen** (lag compensation within a bounded window, using the
snapshots and log that A4 keeps anyway). Replays re-run intents, not physics.

### Movement (D1, D2, A1)

```
 D2-A grid-locked (default, rogue + realtime)       D2-B free movement (realtime experiment, per bag)
 ─────────────────────────────────────────          ────────────────────────────────────────────────
 L1: body at cell c                                  L1: body occupies cell c (grid occupancy)
 Intent.Move(dir) → transfer rule → Moved            L2a: sub-cell position p ∈ 16ths, velocity v
 L2: interpolate c→c' over the step                  Intent.Steer(v) each step; L2a integrates p
                                                     crossing a cell edge ⇒ Intent.Move(dir) into L1
                                                     L1 rejects? ⇒ L2a clamps p to edge, Blocked → bump
```

Both are the **same `Move` intent into L1**. B only adds an L2a integrator in front of it,
so B can be switched on per bag (a W-bag flag) without touching rogue mode, and can be
deleted without touching L1. The invariant **"fuzz L2a ⇒ identical L1 checkpoints"** holds
by construction because L1 only ever sees `Move`.

### History & undo (D9, A3)

- **L1 + L2a: exact history**, as snapshots every N steps plus the event log.
- **L2b: lossy.** After undo or rewind, L2 rebuilds poses from the restored state with
  **no in-flight motion**. Objects in motion reset to the centre of their cell, and
  particles and animations are dropped. This is the explicit acceptance from A3.
- Rogue: full snapshot undo as today (1000 deep).
- **Undo and the logs (ratified 2026-10-28, Aaron: "Keep it as built").** Undo truncates the
  **event** log back to the restored snapshot and emits one `Rewound(toStep)`, so L2 snaps (A3). It
  does **not** truncate the **intent** log. The intent log is the full replay history, undos included:
  `Undo` is itself a logged intent, and undo carries `TickCount`, `Elapsed`, the RNG, dialogue and the
  action log forward. So **replay(intents) ≡ state** (contract rule 1) holds for every session. If a
  timeline-mirroring history is ever wanted, it becomes a separate *derived* view (intents since the
  restored snapshot), not a truncation of the replay input.
- Event-log retention (P1-D9, as built). Events are kept back to the oldest retained snapshot:
  - rogue: the snapshots are the undo stack;
  - realtime: a checkpoint every 8 steps, in a ring of 1000;
  - an 8 MB hard cap drops the oldest chunks and records a `LogTruncated` marker **on the log**
    (metadata, not a `SimEvent`);
  - an idle realtime session takes no checkpoints (a no-op tick is not a step) and relies on the cap.
  - The intent log has no retention yet (≈ 6k entries per 10 min at a 10 Hz clock sync). Phase 7
    bounds it (save = checkpoint + intents since).
- Realtime: checkpoint rewind = restore snapshot k, then replay the logged intents up to
  the target step. Whether rewinding past a lint's reply counts as a cheat is a design
  knob, not an architecture question.

### Multiplayer guardrails (A6, no MP work)

Lockstep and async-replay multiplayer both reduce to the same property: **two machines,
given the same initial snapshot and the same intent log, produce bit-identical L1/L2a
state and event logs.** These constraints keep that door open. Every Phase 1–7 slice
must keep them, and a PR that breaks one must say so.

| # | Guardrail | Status today | Door-closing risk |
|---|---|---|---|
| G1 | **No floats in L1/L2a.** Integers and fixed-point only. Floats live in L2b. | Core sim is int. `Cosmology/Glyphs` uses `double`, but that is render geometry (L2b), so it is OK. **Open (P1-D7):** `WildernessTemplate.FillRatio`, its loot weights and `LootTableTemplate` weights are still `double`, and through the seedling craft that math runs **in step** on `SimRandom.NextDouble()`. Deterministic on IEEE-754 hardware, but a violation. | Phase 3 converts them to per-mille ints when the rule engine re-homes generation. Any new `float` in a rule (e.g. physics-y belts). |
| G2 | **Seeded RNG held in state.** Use a counter-based RNG keyed `(worldSeed, step, bag, entity, purpose)`, never a stateful `System.Random` threaded through calls. | ✓ Phase 1 (1.1). `SimRng` = `Hash(WorldSeed, Step, Stream, Draw)` in `SimState`, with golden vectors pinned. Every in-step draw (`Acquire`, `Dsl`, `Craft` streams) comes from it. `new Random()`, `Random x = new()` and `Random.Shared` are banned in `src/` (`Src_HasNoUnseededRandom`). Remaining debt (P1-D7): the seeded **init-time** `System.Random` (demo seed, wilderness seed, the Godot fallback) is outside Step. | Low. The init-time `Random` moves to `SimRng` in Phase 3. |
| G3 | **Stable iteration order.** Never iterate a `Dictionary`/`HashSet`/GUID order in L1. | Partially: `OrderedFacilities` fixes the rule order, and `EventDeriver.CanonicalOrder` sorts changed bags by world position (root rank, then the cell path), not GUID-hash order. There is no per-bag `ScheduleOrder` yet, because Phase 1 steps every bag in one pass. | Medium. Phase 4 (per-bag scheduling) introduces `ScheduleOrder(bag)`. |
| G4 | **Culture invariance.** | ✓ already (Windows-portability slice, `InvariantCulture` audit). | Low |
| G5 | **Intents are the only input.** No wall clock, no frontend state and no L2a read inside `Step`. | ✓ Phase 1 (1.3). Every controller and debug path builds `Intent`s, and `GameSession.Apply(Intent)` is the only public mutation (`GameSession_PublicSurface_HasApplyAndNoDirectMutators`, `Frontends_DoNotCallSessionMutatorsDirectly`). The clock enters as `SetElapsed` + `Tick` intents, focus stays out of replay because every intent names its location, and the time flag is L1 content (`TickMode`). | Low. Keep new input paths intent-only. |
| G6 | **Replayable event log.** Snapshot + log ⇒ state, checked in CI. | ✓ Phase 1 (1.7, in its P1-D1 form). Replaying the intent log from the profile reproduces the VM **and** the event stream at every journey step (`--replay-check` in `make parity`/`parity-full`, `IntentReplayTests`, `ReplayCheckTests`). The coverage check names every changed cell, slot and cursor (`EventJourneyTests`). The events golden is `journeys/goldens/target-demo.events`. | Low. The stronger form (events alone ⇒ state) is Phase 7. |
| G7 | **Stable identity.** Entity and bag ids are deterministic (counter in state), not `Guid.NewGuid()`. | ✗ Bag ids are still process-fresh GUIDs. Phase 1 works around this (P1-D8): `EventText` renames bags `bag#N` at serialization, and no intent carries a `Guid` (`Intent_Cases_CarryNoClosuresOrBagIds`). | Medium. Phase 3: a deterministic counter in state. |
| G8 | **Per-bag time is a pure function of (bag, step).** Background/dormant scheduling must not depend on *which* player is observing. | n/a yet | Async MP: each peer may observe different bags. Catch-up exactness (`ClosedForm`/`EventSkippable`) makes "observed by whom" irrelevant. `BoundedCatchUp` beyond maxΔ and `MustSimulate` make it **observer-dependent**; that is acceptable for single-player, and such rules must be flagged before any MP work. |

Decisions that would **close** the door, and must not be made casually: a float in a rule;
a physics engine result fed back into L1 other than as an adjudicated intent; tolerance-based
catch-up for a rule whose output feeds other bags; RNG drawn from shared stateful
`Random` in unspecified order.

---

## Phase 0 spike findings

All spikes live in [`spikes/`](../spikes/README.md). This is a separate solution
(`spikes/Spikes.sln`), not referenced by `Pockets.sln`, the game, the journeys or the
goldens.

### Spike A — belt flow (transfer rule resolution)

Code: `spikes/Pockets.Spikes/Belts/` (`NaiveStep`, `ChainStep`), tests in
`spikes/Pockets.Spikes.Tests/BeltTests.cs`. The traces below are the **literal asserted test
strings**. ASCII legend: `▶▼◀▲` empty belt, a letter = an item on a belt, `K` sink (1/step),
`⇒⇓⇑` source, `|` row separator.

**Naive rule "move iff the target was empty last frame" is Rule 184.** It shows a start-up
ripple: the head moves first and the gaps propagate back one cell per step.

```
 naive, ▶▶▶▶▶K full             chain-resolved, same input
 t00 AAAAAK out=0               t00 AAAAAK out=0
 t01 AAAA▶K out=1               t01 ▶AAAAK out=1      ← whole belt moves as a unit
 t02 AAA▶AK out=1               t02 ▶▶AAAK out=2
 t03 AA▶A▶K out=2               t03 ▶▶▶AAK out=3
 t04 A▶A▶AK out=2               t04 ▶▶▶▶AK out=4
 t05 ▶A▶A▶K out=3               t05 ▶▶▶▶▶K out=5      ← drained in 5 steps, not 9
 ...
 t09 ▶▶▶▶▶K out=5
```

With a saturated source feeding a 1/step sink over 100 steps, naive delivers **exactly 1/2
item/step** in steady state (25 in the last 50 steps). Chain resolve delivers **exactly 1
item/step** (50 in the last 50, 96 total = 100 − 4 cells of latency), and a compressed belt
never develops gaps:

```
 chain, saturated source ⇒▶▶▶▶K
 t00 ⇒▶▶▶▶K out=0   t02 ⇒AA▶▶K out=0   t04 ⇒AAAAK out=0   t05 ⇒AAAAK out=1   t06 ⇒AAAAK out=2
```

**Conflict: two sources into one cell.** This uses fixed side priority (ruling R2) and keeps
no per-merge state. In `.⇓.|⇒▶K` the merge cell flows east, so A (from the west) is
straight-through and B (from the north) is a side input. A saturated main line starves B
completely: over 2N steps A delivers 2N and B delivers 0. If the main line leaves gaps (A
emits every 2nd step), B fills exactly those gaps. With three inputs (W main every 3rd
step, N = left, S = right), left beats right in every gap and the right input starves. The
loser and **its whole chain** stay put (no phantom compression). The result is identical
across 10 seeded shuffles of the input listing order.

```
 saturated main (B starves)    main every 2nd step          3 inputs, main every 3rd (C starves)
 t00 .⇓.|⇒▶K out=0             t00 .⇓.|⇒▶K out=0            t00 .⇓.|⇒▶K|.⇑. out=0
 t01 .⇓.|⇒AK out=0             t01 .⇓.|⇒AK out=0            t01 .⇓.|⇒AK|.⇑. out=0   main
 t02 .⇓.|⇒AK out=1             t02 .⇓.|⇒BK out=1  ← gap     t02 .⇓.|⇒BK|.⇑. out=1   left
 t03 .⇓.|⇒AK out=2             t03 .⇓.|⇒AK out=2            t03 .⇓.|⇒BK|.⇑. out=2   left
 t04 .⇓.|⇒AK out=3             t04 .⇓.|⇒BK out=3  ← gap     t04 .⇓.|⇒AK|.⇑. out=3   main

 loser's chain stays put (A = main from W, column of Bs = side from N)
 t00 .B.|.B.|.B.|A▶K out=0
 t01 .B.|.B.|.B.|▶AK out=0   ← A wins, the Bs wait (no compression)
 t02 .▼.|.B.|.B.|▶BK out=1   ← B enters the room A left
 t03 .▼.|.▼.|.B.|▶BK out=2
 t04 .▼.|.▼.|.▼.|▶BK out=3
```

The resolver is a memoized `Vacates(cell)` with cycle detection. A ring with one gap rotates.
A **fully saturated ring stalls** (an on-stack hit counts as "not vacating") and emits one
`Blocked` per cell, as ratified in R1. Conservation
(`items before + emitted = items after + consumed`) holds for 200 steps of a messy 5×3 belt
soup under both step functions.

**Verdict:** chain resolve gives up strict radius-1 locality, but only along explicit
belt graphs (out-degree 1). It is cheap (memoized, O(cells)), deterministic, and gives the
behaviour players expect. D3 stands as ratified.

### Spike B — Step cost at 8 Hz (immutable vs. hot working set)

Code: `spikes/Pockets.Spikes.Bench/` (a **Stopwatch harness**: no BenchmarkDotNet,
tiered JIT off, 200 warm-up steps, 5 trials, median reported). Full output:
[`spikes/results/spike-b-bench.md`](../spikes/results/spike-b-bench.md). Rerun with
`dotnet run -c Release --project spikes/Pockets.Spikes.Bench` (`--quick` for a fast pass).
Environment: .NET 8.0.32, workstation concurrent GC, Ryzen 7 9800X3D, WSL2.

Workload: a 32×32 bag with **every cell processed every step** (no active set). It runs a plaque
field rule that wraps mod 256 so it churns forever, plus 8 belts × 28 cells with sources (1 per 2
steps) and 1/step sinks, chain-resolved. Events: band crossings plus belt
Moved/Emitted/Consumed, ≈ 203 per step. **All variants produce bit-identical state and event
hashes.**

| 10 min game time (4800 steps) | a1 immutable builder | a2 immutable `SetItem` | b SoA + log + snap/8 | b SoA + snap/64 | b0 SoA floor |
|---|---|---|---|---|---|
| time/step p50 | 26.1 µs | 374 µs | 3.1 µs | 3.1 µs | 3.1 µs |
| time/step p99 | 53.1 µs | 1816 µs | 5.3 µs | 13.6 µs | 3.3 µs |
| alloc/step | 72 KB | 10.4 MB | 5.1 KB | 4.2 KB | 0 B |
| GC gen0/1/2 | 7/6/1 | 995/14/1 | 0/0/0 | 0/0/0 | 0/0/0 |
| GC pause total | 66 ms | 827 ms | 0 | 0 | 0 |
| retained (log ± snapshots) | 45 MB | 82 MB | 23 MB | 19 MB | 0 |
| **bags / 125 ms @ 10% share (p50)** | **479** | 33 | **3,986** | 4,012 | 4,024 |
| bags / 125 ms @ 10% share (p99) | 235 | 7 | 2,381 | 917 | 3,781 |

The 3-minute (1440-step) run gives the same per-step figures (a1 26.3 µs / 72 KB; b 3.1 µs / 5.1 KB).

**Paced run** (50 bags, 60 s wall, true 8 Hz):

| | a1 immutable builder | b SoA + log + snap/8 |
|---|---|---|
| missed deadlines | 0 / 480 | 0 / 480 |
| work per tick p50 / p99 / max | 2.0 / 20.7 / 25.4 ms | 0.2 / 1.0 / 18.3 ms |
| ticks with a GC pause > 1 ms | 34 | 2 |
| GC total / worst pause | 531 / 17.7 ms | 24 / 17.9 ms |

Reading: 8 Hz is comfortable for either model. a1's cost is GC, not CPU. b still saw one
large gen2 pause (17.9 ms) in the paced run, caused by the growing retained event log, so
**log retention needs a policy** (compact or flush to disk past the last snapshot)
regardless of model.

### Spike C — per-bag rates and offline catch-up

Code: `spikes/Pockets.Spikes/Plaque/` (`PlaqueSim`, `LineModels`), tests in
`PlaqueTests.cs` / `BeltRateTests.cs`. Full table (~90 rows):
[`spikes/results/spike-c-table.txt`](../spikes/results/spike-c-table.txt). Regenerate with
`dotnet test spikes/Spikes.sln --filter ReportTests --logger "console;verbosity=detailed"`.

Setup: a 16×16 bag starting at zero, Cap = 600, threshold T = 50, corner-accumulator rate
`max(0, 6 − (x+y)/2)`.
- **P1** `p' = min(Cap, p + r)`.
- **P2** `p' = min(Cap, p + r + #(Moore nbrs ≥ T))`.
- The belt is L = 12, with sources every *s* and sinks every *c* steps. It is tested underloaded
  (s3c1) and backlogged (s1c3).

Equivalence tests also run seeded random initial grids (seeds 1–5, N ∈ {1, 7, 30, 240, 241, 2400}).

```
 rule mode             N   k | exact diffCells maxAbs  sumAbs xDiffCells maxXDiff |  cost
 P1   bigstep          240 4 | True          0      0       0         78        3 |    60
 P1   p1-catchup       240 - | True          0      0       0          0        0 |     1
 P2   bigstep          240 2 | False        64     16     656        199        4 |   120
 P2   bigstep          240 8 | False        87    352   17258        246       49 |    30
 P2   substep          240 8 | True          0      0       0          0        0 |   240
 P2   substep-noflush  241 8 | False        58      8     312          0        0 |   240
 P2   p2-catchup       240 - | True          0      0       0          0        0 |    56
 P2   bigstep         2400 8 | True          0      0       0        256       70 |   300   ← saturated at Cap; history still wrong
 P2   p2-catchup      2400 - | True          0      0       0          0        0 |    60

 BELT L=12                         N   k | exact occDiff  cons f/c  emit f/c | cost
 s3c1 under   naive-jump         240   8 | False       9     76/29     80/40 |  30
 s3c1 under   substep            240   8 | True        0     76/76     80/80 | 240
 s3c1 under   belt-catchup      2400   - | True        0   796/796   800/800 | 800
 s1c3 backlog naive-jump        2400   8 | False       1    796/299   808/310 | 300
 s1c3 backlog belt-catchup      2400   - | True        0    796/796   808/808 | 808
```
(xDiff = cells whose first threshold-crossing step differs from full rate; cost = steps,
jumps, or items visited.)

Findings:
1. **Sub-stepping at 1/k is always exact**, provided the n mod k remainder is flushed when
   the bag is observed. Background rate saves latency/scheduling, **not total work**.
2. **Big-stepping (k× rates) is wrong for anything neighbour-dependent.** P2 under-counts
   because frozen flags lag, by up to 352 units. Even P1 keeps the right end state but
   gets crossing *times* wrong by up to k−1 steps. Matching end state ≠ matching history,
   and history is what events (and multiplayer, G8) see.
3. **P2 threshold fast-forward is exact.** Between crossings each cell's increment is
   constant, so the bag jumps crossing to crossing: 56–60 jumps instead of 240–2400 steps. This is
   the entropy §3.2 "time-to-next-threshold" fast-forward, proven exact.
4. **Linear belt catch-up is exact.** The item recurrence is `T_i(p) = max(E_i + 1 + p,
   T_{i−1}(p+1))`, with a sink-period correction at the head. Its cost is O(items), not
   O(steps), so it only pays off when items are sparse (s1c3: 808 items vs 2400 steps). It
   assumes an empty-start belt; a mid-flight start needs the occupied items seeded as
   `E_i < 0` (Phase 4).
5. **The naive "jump k cells" belt diverges.** Its magnitude depends on the spike's
   one-consume-per-jump design, so treat the size as illustrative and the divergence as the finding.
   It coincidentally matches at one configuration (s1c3, k=2, N=240), which is why the
   class contract requires property tests over many Δ, not a single example.
6. **Lints:** a stateful `System.Random` walker diverges as soon as batching changes draw order.
   A counter RNG `Hash(seed, step, id)` is exact under sub-stepping. Neither can skip, so lints are
   `MustSimulate`, and **G2 (counter RNG in state) is confirmed as required, not
   optional.**
7. **Merge networks (added after R2): cycle skip is exact, and a fluid estimate is not.**
   Three networks, all under R2 side priority:
   - N1 is a main line plus one side input.
   - N2 is a main line plus left and right inputs.
   - N3 is a cascade of two merges.

   Each runs under five feed configurations (source periods, then sink period):
   - `under` 3/4/5, sink 1
   - `mainsat` 1/2/3, sink 1
   - `backlog` 2/2/2, sink 3
   - `backlog2` 3/2/2, sink 2
   - `coprime` 3/2/5, sink 1

   N ∈ {240, 241, 2400, 24000}, and k ∈ {2, 4, 8} for the rate modes. All 540 rows are in
   `spike-c-table.txt`, and the excerpt below is copied from it.

   | Method | Exact rows | Worst divergence | Cost |
   |---|---|---|---|
   | sub-step 1/k | 180/180 | 0 | Δ steps |
   | big step 1/k | 0/180 | k=2: 1200 items, 1589 steps late. k=8: 2100 items, 2093 steps late | Δ/k |
   | **cycle skip** | **60/60** (including consume timings) | 0 | 16–120 steps, **flat in Δ** (the same at 2400 and 24000) |
   | main line only ("main ignores sides") | 36/60: holds in under, mainsat, coprime | backlog: 8 fewer main items, 24 steps late (N3) | Δ |
   | fluid O(1) estimate | 0/60 | 1 item (under, coprime), 2–4 (mainsat), 2–7 when backlogged (it credits starved sides with 0 although they deliver a few start-up items, and over-credits main) | 1 |

   - **Sweep:** 200 random period combinations (1..6) × 3 networks, with Δ random in
     1..5000. That is 600 runs with **0 mismatches**.
   - **Measured bound:** transient ≤ 210 steps, cycle ≤ 60 (always P or a divisor of P),
     cost ≤ 299 steps. A re-run at Δ = 1,000,000 gives the same bound. This is measured on
     these network sizes, not proven: the transient grows with belt length (it is roughly
     the fill time), and the cycle grows with lcm(periods).

```
 net cfg       mode           N  k | exact occDiff dDiff  late |  cost   t1    C | deliv
 N1  under     bigstep     2400  8 | False      13   594  1523 |   300           | A 796/286 B 598/4
 N1  under     cycleskip  24000  - | True        0     0     0 |    24   12   12 | A 7996/7996 B 5998/5998
 N1  backlog   cycleskip  24000  - | True        0     0     0 |    42   36    6 | A 7991/7991 B 5/5
 N1  backlog   mainonly   24000  - | False       0     4    12 | 24000           | A 7991/7995
 N1  backlog   fluid      24000  - | False       -     5     - |     1           | A 7991/7995 B 5/0
 N2  coprime   cycleskip  24000  - | True        0     0     0 |    90   60   30 | A 7996/7996 B 11995/11995 C 3998/3998
 N3  backlog   cycleskip  24000  - | True        0     0     0 |    60   54    6 | A 7985/7985 B 5/5 D 6/6
 N3  backlog   mainonly   24000  - | False       0     8    24 | 24000           | A 7985/7993
 N3  coprime   cycleskip  24000  - | True        0     0     0 |   120   90   30 | A 7993/7993 B 11992/11992 D 3999/3999
```
   **Verdict:** merges do **not** need `MustSimulate`. With periodic feeds they are
   `EventSkippable` via cycle skip. With aperiodic feeds they are `BoundedCatchUp`, sub-stepped
   up to maxΔ. If a tolerance is ever wanted instead, the fluid estimate's measured error
   (≤ 7 items per source at these sizes) is the starting figure.

### Decisions the spikes suggest revisiting

| Question | Spike says | Action |
|---|---|---|
| Is the hot cache mandatory? | **No**, not for Phase 1. The immutable builder style runs ~475 fully churning bags per period at a 10% share. It is mandatory to **ban per-change `SetItem`** in Step (~14× slower, ~140× alloc). | Phase 1 rule: builders only. Phase 3 rules go through `BagView`/`BagWriter` so SoA can be swapped in per bag (model C). |
| Is 8 Hz too slow or too fast? | Cost is not a constraint. Even a1 uses ~1/4800 of the period per bag. | Choose the rate on **feel**. 8 Hz stands; 16 Hz is affordable if belts read too steppy. |
| GC tail | a1: 34/480 paced ticks with GC > 1 ms, worst 17.7 ms. The b log also triggered one 17.9 ms gen2. | Step runs **off the render thread** in Godot (Phase 1/3), plus a log-retention/compaction policy (Phase 7). |
| Must belts be MustSimulate? | **No.** Linear chains are `EventSkippable` (exact recurrence). Merge networks with periodic feeds are `EventSkippable` through **cycle skip**: exact in every row and in a 600-run sweep, ≤ 299 steps for any Δ. Only aperiodic feeds fall back to `BoundedCatchUp`. | Phase 4: implement cycle skip as the generic dormant catch-up for transfer networks; fall back to sub-step up to maxΔ when no cycle appears within transient + 2P. |
| "Lower-rate background" (A5) | Only `ClosedForm` rules survive a k× big step, and even then crossing times shift. Everything else must sub-step, so a background bag saves no work. | Work savings come from **Dormant + catch-up**. Background = sub-stepped, mainly for scheduling/latency spreading. A5 is still honoured, but via a different mechanism than "run it coarser". |
| Event granularity | Per-item belt `Moved` dominates the ≈203 events/step and the retained log (23–45 MB / 10 min per churning bag). | Add a chain-level `BeltAdvanced(chain, dir, n)` event (one per chain per step). L2 derives per-item motion. Decide in Phase 1. |
| RNG | Stateful `Random` breaks under batching. | G2 is a hard requirement in Phase 1. |

---

## Methodology Fit

- **TDD + ASCII:** L1 rules keep before/after ASCII tests (spike A is the template). L2
  timelines get zoomed-ASCII tests. Catch-up contracts become property tests ("catch-up ≡
  stepped" over seeded states).
- **Parity gate:** Phase 1 adds an event-stream golden next to the VM golden. `make
  parity` stays the gate and the demo journey must stay green in every phase.
- **Stage-based:** every phase below is shippable on its own and parity-gated.
- **Sub-agent friendly:** rules are small, declared units (kind + time class + events),
  which fits worker-sized slices.

## Phased build (re-sequenced per A1, A4, A5)

```
 0 ✓ design + spikes
 1 ✓ Step contract + events + scheduler skeleton ──┬─▶ 2 Realization v0 + D2-B free-move experiment
                                                   └─▶ 3 Rule engine + time classes ─▶ 4 Off-screen time
                                                                                       │
                          5 Realtime prediction ◀──────────────────────────────────────┘
                          6 Experiment: sub-cell hits
                          7 Hot set + event-log persistence, rewind, determinism audit
```

0. **Design doc and spikes.** ✓ (this doc, `spikes/`).
1. **Step contract, events, scheduler skeleton (no behaviour change).** ✓ 2026-10-28, golden-neutral
   (the as-built notes are in the slice plan's addenda). Slice plan:
   [two-layer-phase1-plan.md](two-layer-phase1-plan.md). Refactor
   `ApplyResult`/`TickFacilities`/`Tick` into `Step(state, intents) → (state, events)`. Add
   an `Intent` type for every controller path, a seeded counter RNG in state (G2), a stable
   `ScheduleOrder` (G3), and a per-bag `rate/mode` field that is always `Observed/1` for now.
   The TUI consumes events at n=1. Parity gains an event golden plus a "snapshot + log ⇒
   state" check. The demo journey stays green.
   *As built:* the per-bag field is `Bag.Clock` (`BagClock`, always Observed/1, read by nothing yet).
   A stable `ScheduleOrder(bag)` was **not** needed, because one canonical pass plus `OrderedFacilities`
   and `EventDeriver.CanonicalOrder` already fixes the order, so it moves to Phase 4. All three frontends (TUI, Godot, mock-godot)
   consume events at n = 1.
2. **Realization v0 + free-movement experiment (A1, moved up from late).**
   `Pockets.Realization` with engine-neutral `Pose` (A2), timelines (n=16), rogue
   choreography and the snap policy. A W-bag body with D2-A (default) and **D2-B behind a
   per-bag flag** (L2a fixed-point integrator → `Move` intents), plus the "fuzz L2a ⇒
   identical L1" invariant in CI. L2a fine state (the vase offset) lands here too, because
   the integrator needs it.
3. **Rule engine + time classes.** Field rules (double-buffered, active set) and transfer
   rules (propose/resolve/commit, chain resolve from spike A). **Every rule declares
   `TimeClass`** from day one. First rules: plaque accumulator, plaque spread, a belt, each
   with ASCII tests and a "catch-up ≡ stepped" property test (spike C is the template).
4. **Off-screen time (A5, moved up from Phase 7).** Bag modes Observed / Background /
   Dormant / Frozen. Sub-stepped background at 1/k, catch-up on observe, plaque threshold
   fast-forward, seepage burps, and lints as `MustSimulate` spawned on observe.
   `TimeTransform` from tick-system.md wired to the scheduler.
5. **Realtime prediction.** Dry-run on input, reconciliation into a bump, lints acting in
   realtime bags.
6. **Experiment: sub-cell hits.** L2 hitboxes → `Intent.Attack` with lag compensation
   against the displayed step.
7. **Hot working set + event-log persistence (A4) and determinism audit (A6).** Promote
   hot bags to SoA (model C), save files as snapshot + log, realtime checkpoint rewind
   (D9), a cross-process replay test (same log on two processes ⇒ identical hash), and
   the G1–G8 audit. Spike B says the hot set is an
   optimization, not a prerequisite. Pull it forward only if the GC-tail measurements
   above show up in real play (realtime W-bags with many belts).

## Status

**Ratified; Phase 0 and Phase 1 complete.** Phase 1's decisions P1-D1…D9 were ratified
2026-10-28 (all as recommended), and so was the 1.8 intent-log ruling ("Keep it as built", §History &
undo). Slices 1.0–1.8 landed golden-neutral, and 1.9 closed out the docs.

**Open guardrail debt:**
- G1: the `double` weights in `WildernessTemplate`;
- G2: the init-time `Random` (both P1-D7, Phase 3);
- G3: `ScheduleOrder` (Phase 4);
- G7: deterministic bag ids (Phase 3).

The Phase 1 follow-ups are listed in [two-layer-phase1-drift.md](two-layer-phase1-drift.md).

Open questions carried forward:

- Lint spawn determinism on observe (entropy Q3) now has an architectural default:
  counter-RNG keyed `(seed, bag, closedDuration)`, which is MP-safe (G8). The tension
  vs. reroll question is still a design call.

Resolved since Phase 0 (2026-10-27): a full belt loop **stalls** (R1), and merges use **fixed
side priority** with accepted side-input starvation (R2). See §Rule kinds.
