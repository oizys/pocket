> **Source of truth: this repo copy.** Vault reading copy:
> `~/obsid/paths/projects/pockets/two-layer-phase2-plan.md` (mirrored with a hash header).
> Parent design: [two-layer.md](two-layer.md) (#23), Phase 2. Model: [two-layer-phase1-plan.md](two-layer-phase1-plan.md).

---

# Pockets — Two-Layer Phase 2 Slice Plan: Realization v0 + D2-B Free-Movement Experiment

*(Plan written 2026-10-28, after Phase 1 closed and the post-Phase-1 bugfixes landed at `5eb382e`. It is
grounded in `main` as of that commit:
- `Simulation.Step` and the 32 data-only intents, with `GameSession.Apply(Intent)` as the only public mutation;
- 28 Step event kinds plus the shell's `Rewound`, with `EventText` and the `target-demo.events` golden;
- `EventLog`, undo marks and realtime checkpoints;
- `ControllerResult.Events` read at n = 1 by the TUI, Godot and mock-godot;
- the journey runner with `--events-out` and `--replay-check`.)*

**PLAN.** Decisions are numbered **P2-D1 … P2-D14** at the end. **Ratified 2026-10-28: Aaron accepted
all fourteen as recommended** ("Go with recs").

## Goal

The game starts to **move between the states it already has**. L1 does not change.

1. **`Pockets.Realization`** is a pure, engine-neutral library (D11, A2). It maps
   `(events, policy, t) → Pose[]`, where a `Pose` is a cell plus a sub-cell offset in 1/16ths (D4), with
   an optional height and facing. It plays a turn as **rogue choreography** (the player's motion, then
   overlapping reactions, then ambient). It applies the **D7 snap policy**: new input snaps every
   in-flight track to its end, then commits.
2. **Godot is the first animated consumer.** The TUI stays the n = 1 realization. mock-godot and the
   runner sample poses over the shared `DebugCommandHandler`, so L2 output is **goldened without Godot**.
3. **The world-bag body** walks with D2-A (grid-locked plus interpolation) by default. **D2-B (free
   movement plus grid occupancy)** sits behind a per-bag flag, realtime only. A fixed-point **L2a
   integrator** turns held steering into ordinary `Move` intents (A1).
4. **L2a fine state** exists. It holds the body's sub-cell position and velocity and the vase offset, is
   saved, undone and replayed exactly, and **L1 never reads it** (D5). CI enforces
   **"fuzz L2a ⇒ identical L1"**.

### Exit criteria

- [ ] `Pockets.Realization` is in `Pockets.sln`. It references `Pockets.Core` only, and architecture
      tests forbid any Godot or Terminal.Gui reference and any `float`/`double` in pose sampling.
- [ ] Zoomed-ASCII timeline tests (1 cell → 4×4) cover each animated mechanic, the D7 snap and the A3
      undo reset.
- [ ] A **pose golden**, `journeys/goldens/target-demo.poses`, is diffed cross-driver (tui against
      mock-godot) in `make parity` and checked in `make parity-full`.
- [ ] Godot animates body walks, bumps, same-bag stack moves and the sort permutation from Realization,
      and builds in WSL. Aaron has done the eyeball pass on Windows (§What Aaron runs).
- [ ] A D2-B bag works end to end:
      - a held direction moves the body smoothly;
      - a cell crossing becomes `Move`;
      - a tree refuses and the body clamps and bumps;
      - rogue mode ignores the flag.
- [ ] The fuzz invariant runs in `parity-full` on every journey (`--fuzz-fine`). The L1-projection test
      also passes: the Moves a D2-B run derives, replayed on a plain session, give an identical L1.
- [ ] A new `free-move` journey with its goldens drives D2-B on tui and mock-godot.
- [ ] **The existing VM checkpoint golden, the 11 TUI buffer goldens and `target-demo.events` are
      byte-identical at the end of the phase.**

## Principles (carried over, plus three new ones)

1. **Core-first, view-model out**, unchanged. Poses are a *third* projection next to the VM and the
   events. They replace neither.
2. **The parity gate is the ratchet.** A slice lands only if:
   - `dotnet test` is green;
   - `make parity-full` is green;
   - the goldens are untouched, except where the slice says it adds one. Phase 2 adds exactly two sets:
     `target-demo.poses` (2.4) and the `free-move.*` set (2.8).
3. **Golden-neutral for L1.** No Phase 2 slice may re-record the VM, buffer or events goldens. Realization
   sits downstream of L1. The D2-B integrator sits upstream of L1 and only ever produces `Move`. The flag
   is off in both demo journeys. If a slice finds it must re-record, the slice stops and the change
   becomes a decision for Aaron.
4. **No slice lands touching only one frontend.** Whatever a frontend starts consuming,
   `DebugCommandHandler` exposes in the same slice. For poses that means a `poses` query, so mock-godot
   and live Godot produce the same lines the TUI driver does.
5. **How L2 output gets tested without Godot (new).** Realization's sampling is **integer**: fixed-point
   easing tables and millisecond times (P2-D1). A pose stream is therefore deterministic and can be
   goldened like the event stream. The pose golden proves *what* Godot will draw. Only *how it looks*
   needs Aaron's Windows box, and that is an eyeball pass, not a gate.
6. **The L1 wall (new).** L1 code never references L2a or Realization. The direction is enforced by
   architecture tests: Realization may read Core, and the fine integrator may read L1 state and events.
   Nothing in `Sim/**` or `Models/**` may reference `Pockets.Core.Fine` or `Pockets.Realization`.
7. **The experiment is deletable (new).** Deleting D2-B means deleting `Pockets.Core/Fine/Integrator*`,
   the two shell-handled intents and the flag, with no L1 edit. A review flag is raised on any D2-B slice
   that touches `Simulation.Step`.

## What exists today, and what Phase 2 adds

```
 TODAY (5eb382e)                                        PHASE 2 END STATE
 ───────────────                                        ─────────────────
 input ─▶ GameController ─▶ GameSession.Apply(Intent)   input ─▶ GameController ─▶ GameSession.Apply(Intent)
                              │ Undo in shell                        │ Undo, Steer, FineTick in shell (P2-D7)
                              ▼                                       ├─▶ Fine.Integrate(fine, l1) ─▶ 0..2 × Move ─┐
                     Simulation.Step ─▶ events                        ▼                                           │
                              │                              Simulation.Step(SimState, intents) ◀───────────────┘
                              ▼                                       │ events (unchanged taxonomy, P2-D6)
 ControllerResult.Events ─▶ frontends at n = 1                        ├─▶ Fine.Reconcile(fine, events)  (offsets, re-anchor)
   TUI: VM re-render + Bumped flash                                   ▼
   Godot: VM redraw + Bumped flash                     ControllerResult.Events + session.Fine
   mock-godot / runner: event lines                                   │
                                                                      ▼
                                                        Pockets.Realization (pure, int)
                                                          Timeline.Commit(events, scene, policy)  ← snaps first (D7)
                                                          Timeline.Sample(ms) → Pose[]
                                                                      │
                                       ┌──────────────────────────────┼──────────────────────────────┐
                                       ▼                              ▼                              ▼
                                TUI: n = 1 (samples          Godot: _Process samples          DebugCommandHandler "poses"
                                the end; render              every frame, draws the           → runner --poses-out → golden
                                unchanged)                   2D projection of the pose        (tui and mock-godot diffed)
```

### The world-bag body (D1), as it is in the code

The demo never opens the W panel. Quiet 1 is **entered as B** (`BagEntered loc=B`), the body is the B
cursor, and tree refusals are `Bumped loc=B … reason=Unnavigable` (target-demo events lines 176–180).
The `W` location (`OpenAsWorld`) exists, but no journey uses it. So Phase 2 defines the body by **bag
type, not by location**:

> **The body** is the cursor of any location whose active bag is a world bag
> (`GameState.IsWildernessType(env)`). Inventory bags keep a cursor (D1). In L1 the body *is* that
> cursor. Its walk is `CursorMoved` and its refusal is `Bumped` (P2-D6). No new L1 entity and no new
> event case are needed.

## Slices

Each slice is one worker session. It lands Core, `Pockets.Realization` and the frontends where relevant,
plus tests, and is gated as in principle 2. The dependencies are in §Order.

### Slice 2.0 — `Pockets.Realization` scaffold and contract types *(pure additions)*

- Add a new project, `src/Pockets.Realization/Pockets.Realization.csproj` (net8.0, references
  `Pockets.Core` only), to `Pockets.sln`, plus `tests/Pockets.Realization.Tests`.
- Contract types:
  - `Pose(EntityKey Entity, Guid Bag, int CellCol, int CellRow, int SubX, int SubY, int? Height,
    Facing? Facing)`, as in two-layer.md §L2 Realization. Sub-units and height are in 1/16 cell (D4).
    SubX and SubY are centre-relative, in −8…+8.
  - `Facing`: 8-way (`N, NE, E, SE, S, SW, W, NW`; no zero member).
  - `EntityKey` (P2-D2): `Body(LocationId Loc)` or `Stack(CellRef At)`. A stack in flight keeps the key
    of the cell it *left* until its track ends. After that it is re-keyed to the cell it rests in.
  - `RealizationPolicy`: durations in **integer milliseconds** (P2-D5) and the easing choice.
  - `Ease`: a fixed-point table (`Ease.Smooth(num, den) → 0…65536`, integer smoothstep), plus linear.
  - `PoseText`: one canonical line per pose. It uses invariant culture and renames bags `bag#N` through
    the **same** naming as `EventText`, so the event and pose golden lines name a bag the same way.
- Architecture tests in `Pockets.Realization.Tests`:
  - the assembly references no `Godot*` and no `Terminal.Gui`;
  - a text scan of `src/Pockets.Realization/**` finds no `float`, `double` or `Math.` (P2-D1);
  - a scan of `src/Pockets.Core/Sim/**` and `Models/**` finds no `Pockets.Realization` (principle 6).
- **Acceptance:** compiles; unit tests for `Ease` (endpoints exact, monotone, the table pinned),
  `PoseText` and `EntityKey` equality. No call site changes; goldens trivially identical.
- *Depends on: nothing.* **Risk:** low. Keep the types minimal; 2.2 will want to add fields.

### Slice 2.1 — Scene and rest poses; zoomed-ASCII test harness

- `Scene.FromState(GameState, IReadOnlyList<LocationId> shown)` builds the **rest poses**:
  - one `Body(loc)` for each shown location whose active bag is a world bag;
  - one `Stack(cell)` for each non-empty cell of each shown bag;
  - everything at its cell centre (Sub 0, 0), `Height = null`;
  - body `Facing` = S at first, after that the last walk direction (held by the timeline).
- The rest poses read L2a offsets from 2.6 onward. Until then the offset source is an empty
  `IFineView` (P2-D9).
- Test-side `ZoomAscii.Render(poses, bag, n: 4)` draws each cell as 4×4 sub-cells, with `@` for the body
  and the item glyph for stacks. This is the two-layer.md §L2 Realization example made executable.
- **Acceptance:** rest poses for the demo profile at journey start and in Quiet 1 match the VM: the same
  cursor and the same occupied cells (a property test over both journeys' checkpoints, so **n = 1 ≡ VM**
  holds by test). Zoom-render tests for a body, a stack and an empty bag.
- *Depends on: 2.0.* **Risk:** low.

### Slice 2.2 — Timelines: tracks from events, sampling, snap and reset

- `Timeline` is an immutable record: a list of `Track(EntityKey, Pose From, Pose To, int StartMs,
  int DurMs, Curve, Channel)`, plus the scene it ends in.
  - `Timeline.Commit(events, scene', policy)` first **snaps** every in-flight track to its end (D7). It
    then appends the new step's tracks.
  - `Timeline.Sample(ms) → Pose[]` returns the rest pose for every entity with no live track.
- Event → track mapping for v0 (P2-D3):

  | Event | Track |
  |---|---|
  | `CursorMoved` at a world-bag location | Body walk `from → to`, eased, `walk` (125 ms). Facing = the move direction. A wrap (an edge jump) **snaps** and does not slide across the grid. |
  | `CursorMoved` in an inventory bag | Cursor glide (linear, 60 ms). Godot may draw it or ignore it. |
  | `Bumped(Unnavigable)` | Body lunge toward `dir` by 4/16 and back, 120 ms. Facing = `dir`. |
  | `Bumped(FailedPeek)` | A shake on that cell: ±2/16 on X, 3 cycles, 150 ms. |
  | `StackMoved` / `StackSplit` / `StackMerged` within **one bag** | A stack slide `from → to`, 125 ms. |
  | Any of those **across bags** (grid ↔ hand, toolbar, C) | **Snap** (`Channel.Fade`: the frontend may cross-fade; the pose is the end pose). |
  | `BagRearranged(permutation)` | Every moved stack slides to its new cell, in parallel, 200 ms. |
  | `Harvested` / `Acquired` | A "pop": `Height` goes 0 → 6 → 0 over 160 ms at the target cell. This is the one v0 use of the height channel, there to prove A2 end to end. |
  | `Rewound` | **Reset**: drop every track and rebuild the rest poses from the restored state, with no in-flight motion (A3). |
  | Everything else (crafting, dialogue, chrome, `TimeAdvanced`, panels) | No track: an n = 1 re-render from the VM. |

- **Acceptance:** one zoomed-ASCII test per row, in spike A trace style: scene + events → samples at
  t = 0, ¼, ½, ¾, 1 of the track. Further tests:
  - **snap:** commit step 2 at 40 ms into step 1's walk, and the sample at 0 ms of step 2 shows step 1's
    end pose;
  - **reset:** `Rewound` mid-walk puts the body at its cell centre;
  - **determinism:** the same inputs give an identical `PoseText` stream across two runs.
- *Depends on: 2.1.* **Risk:** medium. Choosing the entity key for a stack that splits *and* moves is the
  fiddly part. P2-D2's rule (keep the departure key until the track ends) decides it; pin it with a test.

### Slice 2.3 — Rogue choreography and the realtime path

- A turn's events are grouped into **phases** by `Choreography.Phase(event)`:
  - **0 player:** the events of the player's own intent (envelope through stack and location events);
  - **1 reactions:** the events after the envelope's own changes (`Bumped`, `Harvested` pop, rule events
    that name a cell);
  - **2 ambient:** `TimeAdvanced` and later (plants, craft progress).
- Start times: the player phase at 0; reactions **overlap** from 50% of the player phase; ambient starts
  when the reactions start. The offsets are policy values (P2-D5).
- **Rogue:** one committed intent is one turn. `Commit` snaps, then lays out the three phases.
- **Realtime (the demo profile):**
  - a player intent is a turn, exactly as in rogue (P1-D2: it applies immediately);
  - a clock `Tick`'s events are **ambient only** and do **not** snap in-flight player tracks (P2-D13);
  - a `FineTick` (2.7) neither snaps nor adds body tracks, because the body is sampled from L2a.
- **Acceptance:**
  - Traces for a rogue grab-then-tick turn, a walk into a tree (the walk is refused, so only the lunge
    plays, at phase 0) and a harvest (walk, then pop at 50%).
  - In a realtime test, a tick landing mid-walk leaves the walk unsnapped. A key landing mid-walk snaps
    it.
- *Depends on: 2.2.* **Risk:** low to medium. Phase classification must be by event kind and position, not
  by intent type, so a composite `Click` (SetCursor + Primary) choreographs as one turn.

### Slice 2.4 — Pose stream over the debug protocol, pose golden and parity *(adds the one L2 golden)*

- `DebugCommandHandler` holds one `Timeline` per controller, next to its `EventText`, in the same
  `ConditionalWeakTable`. It commits each response's events.
  - A new **`poses`** query returns the `PoseText` lines of the moving entities at sample points
    `u ∈ {0, ¼, ½, ¾, 1}` of the last commit's span (P2-D4).
  - Every success response also gains `"poses": [...]`.
- The TUI driver builds its own `Timeline` from `GameView.LastEvents`. The TUI **renders nothing new**:
  it is n = 1, and its frame is the `u = 1` sample.
- Runner: `--poses-out <file>`. Lines are prefixed `<journeyStep>:<k>`, as in the events golden.
- Makefile:
  - **`parity`:** also diffs the smoke pose streams across drivers;
  - **`parity-full`:** does the same for target-demo, plus `golden: pose stream` against
    **`journeys/goldens/target-demo.poses`**;
  - **`record-goldens`:** writes it.
  - `parity-godot` diffs the live-Godot poses against the tui poses and the golden.
  - Plain `dotnet run` lines only (the Windows rule).
- **Acceptance:**
  1. `make parity-full` is green with the new golden. `git status` shows only
     `?? journeys/goldens/target-demo.poses`.
  2. The VM, buffer and events goldens are unchanged.
  3. tui and mock-godot pose streams are byte-identical on both journeys.
  4. Replay: `--replay-check` also compares the replayed pose lines at every boundary.
  5. Mutation: a one-field policy change (walk 125 → 126 ms) fails the golden.
- *Depends on: 2.3.* **Risk:** low. Keep one `EventText` naming source for both stream kinds.

### Slice 2.5 — Godot: the first animated consumer

- `GameSceneController` holds a `Timeline` and commits every `ControllerResult.Events` (input and clock).
  - A new `_Process(delta)` accumulates **integer** milliseconds and calls `Sample`.
  - `GridDrawControl.SetPoses(poses)` draws the body and the stacks at
    `cell + Sub/16 · cellSize`, with `Height` as a −y offset (the 2D projection of A2; a 3D view would
    map the same pose).
  - The cursor highlight follows the body pose in world bags.
  - On input, the D7 snap happens inside `Commit`, so `_UnhandledInput` does nothing extra.
  - Undo arrives as `Rewound` and resets.
- `Pockets.Godot.csproj` gains a `ProjectReference` to `Pockets.Realization`. `DebugWebSocketServer`'s
  local screenshot response gains `"poses": []`.
- The TUI does not change. mock-godot already has `poses` from 2.4 (principle 4).
- **Acceptance:**
  - `dotnet build src/Pockets.Godot/Pockets.Godot.csproj` succeeds in WSL;
  - all goldens are unchanged;
  - `make parity-godot`'s pose diff passes **on Aaron's box**. This is not a gate, as before, but it is
    listed under §What Aaron runs.
- *Depends on: 2.4.* **Risk:** medium. This is the first per-frame code in the Godot scene, and it cannot
  run in WSL. Keep the drawing a pure function of `(grid, poses)` so it can be screenshot-tested on
  Windows.

### Slice 2.6 — L2a fine state: store, shell plumbing, the L1 wall *(no D2-B yet)*

- New namespace `Pockets.Core.Fine`, with `FineState`:
  - `Bodies: ImmutableDictionary<(LocationId, Guid Bag), FineBody(int Px, int Py, int Vx, int Vy,
    Direction8? Held)>`;
  - `Offsets: ImmutableDictionary<CellRef, (int Dx, int Dy)>`;
  - everything in 1/16ths, ints only, with `|offset| ≤ 6`.
- `GameSession.Fine` is an init property (P2-D9):
  - it is **not** in `GameState`, `SimState` or the VM;
  - undo snapshots carry it (`UndoMarks` gains the fine state at the mark);
  - realtime checkpoints carry it;
  - replay rebuilds it from the intent log.
- `Fine.Reconcile(fine, events, game')` runs in the shell after every Step:
  - it drops the offset of any cell named as a departure or arrival by `StackMoved`, `StackSplit`,
    `StackMerged`, `StackRemoved`, `Harvested` or `BagRearranged`;
  - it re-anchors a body to its cell centre on any `CursorMoved`/`BagEntered`/`BagLeft` that the
    integrator did not originate;
  - on undo it restores the snapshot's fine state.
- `Scene.FromState` now takes an `IFineView`. A stack's rest pose includes its offset, and a body's rest
  pose includes `(Px, Py)`.
- Debug: a `fine` query (canonical lines) and a test-only `setOffset {loc, index, dx, dy}` command, which
  builds a shell-handled `Intent.SetFineOffset` (P2-D11).
- **The L1 wall, as architecture tests:**
  - no file under `Sim/**` (except the shell glue in `GameSession`) or `Models/**` (except
    `GameSession.cs`) references `Pockets.Core.Fine`;
  - `Simulation.Step`'s signature takes no fine type (reflection).
- **Acceptance:**
  - Goldens identical: the fine state is empty in both journeys, so the rest poses are unchanged and the
    pose golden is unchanged.
  - Unit tests for reconcile, undo and replay of offsets, and the wall.
- *Depends on: 2.1. It can run in parallel with 2.2–2.5.* **Risk:** medium. Adding a field to the undo
  plumbing touches `UndoCore` and the checkpoint ring. Keep `UndoStack`'s positional shape, as 1.8 did.

### Slice 2.7 — D2-B: the per-bag flag, the integrator, steering input

- **The flag (P2-D12):** `Bag.Movement` (`MovementModel { GridLocked = 1, Free }`, no zero member) is an
  init property defaulting to `GridLocked`, like `Bag.Clock`. It travels with the bag. **L1 never reads
  it**; only the shell's fine path does. It is honoured only when `TickMode == Realtime` and the
  location's active bag is a world bag. Rogue sessions ignore it (D2).
- **Intents (P2-D7, P2-D8):** two new cases, both **shell-handled** like `Undo` (Step never sees them;
  its dispatcher rejects them as a programming error):
  - `Steer(LocationId At, Direction8? Dir)`: set the held direction, or release it with null. It is logged
    only when it changes `Held`.
  - `FineTick`: advance every free body one integrator tick. It is sent by the scheduler at **8 Hz**
    (`FineTicksPerSecond = 8`, D6's rate) while any free body has `Held != null` or is off-centre with
    velocity. An idle `FineTick` is a no-op, so it is not logged (the 1.3/1.7 rule).
- **Integrator** (`Fine/Integrator.cs`, pure, fixed-point):
  - `v` comes from a table over `Held`, in policy content: orthogonal 4/16 per tick (= 2 cells/s at
    8 Hz), diagonal 3/16 on each axis. There is no acceleration in v0.
  - `p += v`. When `|Px|` or `|Py|` would pass 8, that axis's crossing becomes `Move(At, dir)`, X before
    Y. The shell runs it through `Simulation.Step` and reads the result's events:
    - `CursorMoved` ⇒ the cross is accepted: `p −= 16·dir`;
    - `Bumped` ⇒ refused (a tree): clamp `p` to `±7` on that axis and zero that velocity component. The
      L1 `Bumped` drives the cue, as today.
  - At the **outer grid edge** the integrator clamps and emits **no** `Move`, so there is no wrap in a
    free bag (P2-D10).
  - Releasing the steer decelerates to the cell centre at 4/16 per tick, so the body settles on the grid
    and an interaction (harvest, enter) is never ambiguous about its cell.
  - **Nudge (P2-D11):** a free body overlapping a stack's cell by ≥ 8/16 pushes that cell's offset 1/16
    in `v`'s direction per tick, up to `|6|`. This is pure L2a.
- **Input:**
  - Godot maps arrow-key press and release (and the gamepad stick, quantized to 8-way) to `Steer` in a
    free bag, and to `Move` otherwise.
  - The TUI has no key-up events, so in a free bag it keeps sending `Move`. That is D2-A movement, and
    `Reconcile` re-anchors the body (P2-D14).
  - Debug and journey: `steer {dir}` and `setMovement {loc, model}`. `advanceTime` drives `FineTick`s
    from the same accumulator as `Tick`.
- Realization: a free body's pose is `(cell, Px, Py)` from L2a. The timeline adds no walk track for an
  integrator-originated `CursorMoved`; Godot interpolates between the last two fine ticks.
- **Acceptance:**
  - Integrator traces in zoomed ASCII: a held E crosses one cell at the expected tick; a tree refuses and
    the body clamps and bumps; a diagonal into a corner gives X then Y; the outer edge clamps without a
    `Move`; release settles to centre; nudge is capped.
  - A rogue session with the flag on behaves exactly like D2-A.
  - Both demo journeys are golden-identical, because the flag is off and no `Steer`/`FineTick` is sent.
  - Shape-pin tests for the intent count change by design.
- *Depends on: 2.6 (and 2.3 for the pose path).* **Risk:** medium to high. This is the phase's real
  experiment. The risky part is the shell running a derived `Move` *inside* the handling of a `FineTick`
  (undo, log and event bookkeeping). The rule: a derived `Move` is a normal Step with normal events, and
  it is **not** separately logged, because `FineTick` is the logged input.

### Slice 2.8 — The fuzz invariant in CI, and the `free-move` journey *(adds the D2-B goldens)*

- **`--fuzz-fine <seed>`** in the runner (tui and mock-godot). After every journey step, it overwrites
  the session's fine state with seeded random values: offsets ±6 on random occupied cells, and body
  `(Px, Py)` in ±7. It then asserts the **VM and event lines are identical** to the unfuzzed run.
  - It runs on smoke and target-demo in `parity-full`.
  - **Caveat, by design:** the fuzz runs *after* the step's input, so it never changes which `Move`s an
    integrator derives within the step. Fuzzing the integrator's *inputs* is the L1-projection test below.
- **The L1-projection test** (Core.Tests): run the free-move journey, collect the `Move`s the integrator
  derived (from the `IntentApplied(Move)` envelopes of `FineTick`-handled steps), and replay them on a
  **D2-A** session of the same profile with no fine state at all. The L1 checkpoints and events are
  identical. This is "L1 only ever sees `Move`", tested.
- **A property test:** random fine states × random L1 intent streams (no `Steer`) give identical L1.
  This makes the wall behavioural as well as textual.
- **`journeys/free-move.journey.json`:** the demo profile with `setMovement B Free` on entering Quiet 1.
  It walks with held directions, bumps a tree, crosses diagonally, nudges a stack, harvests, undoes and
  leaves. New goldens: `free-move.checkpoints`, `.events` and `.poses`. `parity-full` runs it on both
  drivers with `--replay-check`.
- **Acceptance:**
  - `make parity-full` is green with the new goldens; nothing else under `journeys/goldens/` changed;
  - a mutation that lets L1 read an offset (a test-only hook) fails the fuzz gate.
- *Depends on: 2.7 and 2.4.* **Risk:** low to medium.

### Slice 2.9 — Phase 2 close-out

- Docs:
  - `two-layer.md`: mark Phase 2 ✓ and add as-built notes (Pose, the body definition, fine state, D2-B);
  - `CLAUDE.md`: a "Realization and fine state (Phase 2)" section with the wall's tests;
  - `tools/journey-runner/README.md`: `--poses-out`, `--fuzz-fine`, the free-move journey, and Aaron's
    `parity-godot` steps.
- Write a drift note, `two-layer-phase2-drift.md`.
- Record Aaron's D2-B verdict: keep it as an option, make it the realtime default, or delete it (A1 says
  it is an experiment, and this is where it gets judged).
- **Acceptance:** docs only; all gates green.

## Order

```
 2.0 scaffold ──▶ 2.1 scene/rest poses ──┬──▶ 2.2 timelines ──▶ 2.3 choreography ──▶ 2.4 pose golden ──▶ 2.5 Godot
                                         └──▶ 2.6 fine state ──────────────────────────────┐
                                                                     2.3 + 2.6 ──▶ 2.7 D2-B ──▶ 2.8 fuzz + free-move journey
                                                                                 2.0–2.8 ──▶ 2.9 close-out
```

- **Recommended serial order:** 2.0 → 2.1 → 2.2 → 2.3 → 2.4 → 2.6 → 2.5 → 2.7 → 2.8 → 2.9. Putting 2.6
  before 2.5 lets Godot draw L2a offsets from its first build, so Aaron needs one Windows pass for
  2.5 + 2.7 rather than two.
- **Safe parallel pairs**, if two workers are free: (2.2–2.4 ∥ 2.6), and (2.5 ∥ 2.7) once both
  predecessors land. 2.5 and 2.7 both touch `GameSceneController` (poses vs. steering input), so the
  second to land rebases. Otherwise the default stays one worker per project, serial.

## What Aaron builds and runs on Windows

Godot runs only on Aaron's box. Everything before 2.5 needs nothing there.

| After | Run | Look for |
|---|---|---|
| 2.5 | Fresh Godot build (it now references `Pockets.Realization`), then `make parity-godot` | The pose diff against tui and the golden is clean. The body **walks** between cells in Quiet 1 rather than jumping, the tree bump lunges, sort slides, and a harvest pops. Mash the arrow keys: no lag and no queued animation, because every press snaps (D7). Ctrl-Z mid-walk: the body is at its cell centre (A3). |
| 2.7 | The same build, `setMovement B Free` over the debug WS (or the free-move journey through `parity-godot`) | A held arrow glides, diagonals work, a tree stops you at its edge with a bump, and the grid edge stops you without wrapping. On release the body settles to centre. Walking through a stack nudges it a little, and harvesting or moving it re-centres it. |
| 2.9 | — | **The D2-B verdict** (keep / default / delete) and a feel note on the 125 ms walk and the 2 cells/s speed. Both are policy values, so changing them is a re-record of the pose goldens, not a code change. |

## Risks (ranked)

1. **The shell grows a second step path (2.7).** `FineTick` → derived `Move` → `Step` has to keep the
   undo, intent-log, event-log and checkpoint rules exactly. Mitigation: the derived `Move` goes through
   the *same* `RunStepCore` as a key-press `Move`, and the free-move replay check plus the L1-projection
   test catch any drift.
2. **Godot per-frame code is unverifiable in WSL (2.5, 2.7).** The pose golden over mock-godot proves
   the numbers. Rendering is a pure function of `(grid, poses)`. The eyeball pass is Aaron's and is
   listed above.
3. **Entity identity without G7 (2.2).** Stacks are keyed by cell. A split-then-move or a sort
   permutation could mis-key a track. Mitigation: P2-D2's departure-key rule plus a test per case.
   Real ids are Phase 3 (G7).
4. **The undo surface (2.6).** Undo snapshots now carry fine state, and realtime fine ticks push no
   undo. Undoing in a free bag restores the body's sub-position at the last L1 snapshot, not "one fine
   tick ago". This is accepted as exact *and* coarse.
5. **Scope creep.** No 8 Hz L1, no prediction or dry-run (Phase 5), no new rules (Phase 3), no new L1
   event kinds, no `Moved`/`Spawned` taxonomy (Phase 3, with G7), no TUI sub-cell rendering. Any of
   these in a Phase 2 PR is a review flag.
6. **Intent-log growth.** At 8 Hz while steering, `FineTick`s add up to about 4.8k entries per 10 minutes
   of held input. The log has no retention until Phase 7. This is fine for an experiment; note it in the
   drift note.

## Open questions (not blocking; answer while building or at close-out)

- Does a free body need **acceleration** for feel, or is the constant speed with a settle enough?
  (Policy content either way; deferred to Aaron's 2.7 pass.)
- Should the TUI ever **show** sub-cell motion (half-blocks or braille, as two-layer.md suggests)? It is
  out of scope here because it changes the buffer goldens.
- With D2-B, should **harvesting** require the body to be settled (centred), or is "the cell you are in"
  enough? v0 says "the cell you are in", and the settle-on-release makes this rarely visible.
- If the W panel (`OpenAsWorld`) ever becomes the main world view, its cursor is a body by the same
  definition. Nothing in this plan is B-specific.

## Decisions for Aaron

**Ratified 2026-10-28: all as recommended** (Aaron, Discord: "Go with recs").

| # | Decision | Options | Recommendation | Status |
|---|---|---|---|---|
| **P2-D1** | Number type of Realization's sampling | (a) floats (simplest easing; non-goldenable, cross-machine drift in theory); (b) **integer: ms times, a fixed-point easing table, poses in 1/16ths** | **(b).** It makes the pose stream a golden that mock-godot can produce without Godot (principle 5), at the cost of 1/16-cell quantization, which frontends may smooth with floats on their side (L2b). A text-scan test bans `float`/`double` in `Pockets.Realization`. | ✓ Ratified 2026-10-28 |
| **P2-D2** | Entity identity in `Pose` while ids are GUIDs and stacks have none (G7) | (a) pull G7 (deterministic entity and bag ids) into Phase 2; (b) **realization-local `EntityKey`: `Body(loc)` or `Stack(cell)`; a stack in flight keeps its departure key until its track ends** | **(b).** G7 touches every `new Bag` and is a Phase 3 item for MP and saves. Keys are local to Realization and never enter L1. | ✓ Ratified 2026-10-28 |
| **P2-D3** | Which events animate in v0 | (a) everything with a cell; (b) **body walk and bump, failed-peek shake, same-bag stack moves, sort permutation, harvest/acquire pop (height), `Rewound` reset; cross-bag moves snap with a fade hint; the rest re-render at n = 1** | **(b).** Cross-bag flights (grid → hand) have no engine-neutral path between two bags' coordinate frames. A frontend may fade. | ✓ Ratified 2026-10-28 |
| **P2-D4** | Who consumes Realization this phase | (a) all three animate; (b) **TUI stays n = 1 (its frame is the `u = 1` sample, render unchanged); Godot is the first animated consumer; mock-godot and the runner sample poses through `DebugCommandHandler`'s `poses`** | **(b).** TUI sub-cell rendering would re-record the buffer goldens. mock-godot is how L2 gets goldened without Godot. | ✓ Ratified 2026-10-28 |
| **P2-D5** | Pose golden form and timing values | (a) dump the internal tracks; (b) **black-box samples of moving entities at u ∈ {0, ¼, ½, ¾, 1} of each commit's span**. Durations: walk 125 ms (one D6 step), bump 120, shake 150, slide 125, sort 200, pop 160; reactions overlap at 50% | **(b)**, with the values above. Samples survive a refactor of the track representation, and the values are policy, so tuning one after Aaron's pass is a pose re-record and nothing else. | ✓ Ratified 2026-10-28 |
| **P2-D6** | What the body is in L1 | (a) a new body entity with `Moved(entity, from, to, walk)` and `Blocked` events; (b) **the cursor of any location whose active bag is a world bag; walk = `CursorMoved`, refusal = `Bumped`** | **(b).** It is golden-neutral and needs no new event kind. It also matches the demo, which walks Quiet 1 as B. The `Moved`/`Blocked`/`Spawned` taxonomy arrives with the rule engine and G7 (Phase 3). | ✓ Ratified 2026-10-28 |
| **P2-D7** | D2-B's time base, with L1 still at 1 Hz (P1-D4) | (a) move L1 to 8 Hz now (rescales every duration; a behaviour change); (b) **a shell-handled `FineTick` intent at 8 Hz drives only the integrator; L1 still ticks at 1 Hz, and derived `Move`s apply immediately like any realtime player intent (P1-D2)** | **(b).** It is golden-neutral and deletable (principle 7). L1 at 8 Hz is still Phase 3/5. | ✓ Ratified 2026-10-28 |
| **P2-D8** | The shape of steering input | (a) `Steer(vx, vy)` with velocity on the intent; (b) **`Steer(At, Direction8?)`, a held direction, logged on change, with speeds in a policy table** | **(b).** It is data-only with no float (G1). The replay log carries what the player pressed, not a derived quantity, and speed can be tuned without changing old logs' meaning. | ✓ Ratified 2026-10-28 |
| **P2-D9** | Where L2a lives | (a) a field on `GameState` (L1 equality and the VM would see it); (b) a field on `SimState` that Step must not read; (c) **`GameSession.Fine`, in its own `Pockets.Core.Fine` namespace, carried by undo snapshots and checkpoints, rebuilt by replay, never in the VM** | **(c).** The wall becomes a namespace rule a test can scan (principle 6), and the fuzz gate can compare the VM directly. | ✓ Ratified 2026-10-28 |
| **P2-D10** | Grid edges in a free bag (D2-A wraps today) | (a) wrap, as D2-A does; (b) **clamp at the outer edge with no `Move`** | **(b).** A body gliding off one edge and appearing at the other reads as a bug. D2-A's wrap is unchanged (golden-neutral). Revisit wrap for D2-A separately if it bothers anyone in world bags. | ✓ Ratified 2026-10-28 |
| **P2-D11** | What sets the vase offset (the brief's L2a example) | (a) a debug setter only; (b) the offset on drop is taken from the body's sub-position; (c) **nudge on contact (a free body pushes a stack's offset 1/16 per tick, capped at 6) plus a test-only `SetFineOffset`; any L1 move of the stack clears it** | **(c).** It is the one visible, purely-L2a effect, which makes it the natural fuzz target, and it needs no new L1 verb. (b) can follow if dropping in world bags becomes a thing. | ✓ Ratified 2026-10-28 |
| **P2-D12** | How the per-bag flag is set | (a) a data-file field on the wilderness templates; (b) **a `Bag.Movement` init property (default `GridLocked`), set by the debug/journey `setMovement` command; the demo profile is unchanged; rogue ignores it** | **(b)** for the experiment. If the verdict is "keep", promoting it to a data field is a small follow-up. | ✓ Ratified 2026-10-28 |
| **P2-D13** | What the D7 snap covers in realtime | (a) every commit snaps everything; (b) **a player intent snaps all in-flight tracks; a clock `Tick` adds ambient tracks without snapping; a `FineTick` neither snaps nor adds body tracks** | **(b).** D7 is about input. A 1 Hz tick or an 8 Hz fine tick cutting a walk short would make motion stutter for no player reason. | ✓ Ratified 2026-10-28 |
| **P2-D14** | TUI input in a free bag (Terminal.Gui has no key-up) | (a) a key press = steer for one cell's worth of fine ticks; (b) **the TUI keeps sending `Move` (D2-A semantics), and `Reconcile` re-anchors the body** | **(b).** It is honest about the terminal's limits and keeps the TUI golden-neutral. D2-B input is a Godot-and-debug-protocol feature, and mock-godot covers it in the gate. | ✓ Ratified 2026-10-28 |

## Worker notes

- Each brief inherits the repo `CLAUDE.md`: TDD, LINQ method syntax, a doc comment per function, Sonnet
  sub-agents for implementation, land on `main` (fetch, rebase, push, never force), and
  `make parity-full` green before push.
- **Golden-neutral is the default acceptance.** Only 2.4 (`target-demo.poses`) and 2.8 (`free-move.*`)
  may add files under `journeys/goldens/`. None may modify an existing one.
- No slice edits `Simulation.Step`'s dispatch except 2.7's rejection of the two shell-handled intents.
- Godot is outside `Pockets.sln`. Build it in WSL with
  `dotnet build src/Pockets.Godot/Pockets.Godot.csproj` in every slice that touches it.
- Every slice brief should quote its rows from §Decisions, so the worker knows which recommendation it is
  building on.

## Addendum — Slice 2.0 as built (2026-10-28)

Golden-neutral. `dotnet test` green (Core 1032, App 54, Realization 27) and `make parity-full` green.
The VM, events and 11 buffer goldens are byte-identical, and no call site changed.

### Projects

- `src/Pockets.Realization` (net8.0, references `Pockets.Core` only) and `tests/Pockets.Realization.Tests`
  (xunit, same package versions as Core.Tests) are both in `Pockets.sln`. `make test` already runs
  `dotnet test Pockets.sln`, so it picks up the new suite with no change. Only its comment changed.

### Contract types (`src/Pockets.Realization/`)

| Type | Shape | Notes for later slices |
|---|---|---|
| `Pose` | `readonly record struct (EntityKey Entity, Guid Bag, int CellCol, int CellRow, int SubX, int SubY, int? Height, Facing? Facing)`, plus `const SubUnits = 16` | A struct, as in two-layer.md §L2. No validation. SubX/SubY are centre-relative, −8…+8 by convention. 2.2 may add fields. |
| `Facing` | `N = 1 … NW` (clockwise, no zero member) | 2.1 defaults the body to `S`. |
| `EntityKey` | abstract record, private ctor; `Body(LocationId Loc)`, `Stack(CellRef At)` | Value equality, usable as a dictionary key. The departure-key rule (P2-D2) is documented on the type, and the timeline (2.2) enforces it. |
| `EaseKind` | `Linear = 1, Smooth = 2` | |
| `RealizationPolicy` | sealed record of int-ms init properties: Walk 125, Bump 120, Shake 150, Slide 125, Sort 200, Pop 160, `ReactionOverlapPercent` 50, `Ease` = Smooth; `Default` | Values are P2-D5's. |
| `Ease` | `One = 65536`. `Linear(num, den)` floors. `Smooth(num, den)` uses a **pinned 65-entry smoothstep table** (`(192i²−2i³+2)/4`, i = 0…64), linearly interpolated with 16 fractional bits. `Apply(kind, …)` dispatches. | num is clamped to [0, den], and `den ≤ 0` returns `One` (a zero-length track is finished). Both endpoints are exact and both curves are monotone (tested). |
| `PoseText` | `entity=Body(B) bag=bag#1 col=2 row=1 subX=3 subY=-4 height=- facing=S`; a stack key is written `Stack(bag#1[4])` | Invariant culture, null as `-`. 2.4 adds the golden's line prefix (journey step, commit, u). |

### Shared bag naming (the one Core edit)

`EventText`'s private bag dictionary moved into a new public `Pockets.Core.Sim.BagNames` (`Name(Guid)` →
`bag#N` on first sight). `EventText` gained `EventText(BagNames)` and `Names`, and `new EventText()` and
`EventText.Lines` behave exactly as before. `PoseText(BagNames)` takes the same object. **For 2.4:**
build the pose writer as `new PoseText(eventText.Names)` with the run's single `EventText`. The event
and pose lines then name a bag the same way. Naming follows first appearance across both streams in the
order they are written, so the writer must interleave deterministically (for example, a step's events,
then its poses).

### Architecture tests (`tests/Pockets.Realization.Tests/RealizationArchitectureTests.cs`)

- `Assembly_ReferencesNoGodotOrTerminalGui` checks both the referenced assemblies and the csproj text,
  and that the only ProjectReference is Core.
- `Source_HasNoFloatingPoint` scans `src/Pockets.Realization/**` with `//` comments stripped, using
  `\b(float|double|decimal|Single|Double|Half)\b|\bMathF?\.`. This also rules out `Math.Clamp`, so
  `Ease` clamps with ternaries. Later slices must do the same, or add an int helper.
- `CoreSimAndModels_DoNotReferenceRealization` scans the text of `Pockets.Core/Sim/**` and `Models/**`,
  and checks that the Core assembly has no reference to Realization (principle 6). 2.6 extends the scan
  to `Pockets.Core.Fine`.

## Addendum — Slice 2.1 as built (2026-10-28)

Golden-neutral, with no Core, App or Godot edit. `dotnet test` is green (Core 1032, App 54, Realization 45)
and `make parity-full` is green. The VM, events and 11 buffer goldens are byte-identical.

### Production (`src/Pockets.Realization/`)

- **`IFineView`** (P2-D9) has two members: `StackOffset(CellRef) → (Dx, Dy)` and
  `BodyOffset(LocationId, Guid bag) → (Px, Py)`, both centre-relative in 1/16ths. `FineView.Empty` is the
  all-zero view. It lives in Realization, not in Core, so Core stays untouched. **For 2.6:** back it with
  a small adapter over `FineState` (Realization may read Core). Alternatively, move the interface to
  `Pockets.Core.Fine` and let `FineState` implement it directly; either way, the call sites keep the
  same shape.
- **`Scene(ImmutableArray<Pose> Poses)`**:
  - `Scene.FromState(GameState, IReadOnlyList<LocationId> shown, IFineView? fine = null,
    IReadOnlyDictionary<LocationId, Facing>? facings = null)`.
  - It walks `shown` in order and skips absent locations. Each location's bag is `ActiveBagAt(loc)`.
  - For each location it emits a `Body(loc)` first (only in a world bag), at the location's cursor and
    facing `facings[loc]`, or `S` if there is none. It then emits one `Stack(cell)` per non-empty cell in
    index order.
  - Everything is at Sub (0, 0) plus the fine offset, with `Height = null`. Stacks have `Facing = null`.
  - A bag shown by two locations emits its stacks once, at its first occurrence.
  - `Find(EntityKey) → Pose?`.
  - **For 2.2:** the timeline passes the last walk direction through `facings`.
- **The world-bag predicate:** `Scene.IsWorldBag(bag) = GameState.IsWildernessType(env) || bag.EnterOnly`.
  This **widens the plan's body definition.** The demo's Quiet 1 has env `"Quiet 1"`, which is not a
  wilderness-type name, but it is `EnterOnly`, and without that clause the demo would have no body. It
  matches `GameState.IsDescendableBag`'s existing reasoning.

### Tests (`tests/Pockets.Realization.Tests/`)

- **`ZoomAscii.Render(poses, bag, n = 4)`** is a test-side helper:
  - each cell is n×n `.`; cells are joined by `|`, and grid rows are separated by `-`/`+` lines;
  - the sub index is `floor((sub + 8)·n / 16)`, so a centred pose at n = 4 sits on index 2, and
    `SubX = +8` lands on index 0 of the next cell (the two-layer.md boundary example);
  - stacks draw `GlyphRenderer.GlyphFor`, or `*` if the keyed cell is empty (useful for in-flight
    departure keys in 2.2); the body draws `@` on top;
  - other bags' poses and off-grid poses are skipped.
- **`ZoomAsciiTests`** covers an empty bag, row separators, a body, a stack glyph, the boundary example,
  and body-over-stack with foreign and off-grid poses skipped.
- **`SceneTests`** covers the demo start (no body, centred stacks), a world bag's body at the cursor
  facing S, the facing override, fine offsets, absent locations, a bag shown twice, H/T bags,
  `FineView.Empty` and `Find`.
- **`SceneJourneyTests`** is the **n = 1 ≡ VM** property. It runs both journeys through
  `DebugCommandHandler`, and at the start and after every step it checks `Scene.FromState(state, [B, H, T])`
  against `ViewModelSerializer`:
  - B's stack cells equal the VM's non-empty cells;
  - there is a body iff B's bag is a world bag, and it sits on the VM cursor;
  - the hand count and the toolbar slot indices match the VM;
  - every pose is centred, with no height.
  It also asserts that target-demo reaches a body (Quiet 1), so the check is not vacuous.

## Addendum — Slice 2.2 as built (2026-10-28)

Golden-neutral, with no Core, App or Godot edit. `dotnet test` is green (Core 1032, App 54, Realization 76)
and `make parity-full` is green. The VM, events and 11 buffer goldens are byte-identical. The tests were
written first and seen failing (compile) before any production code.

### Production (`src/Pockets.Realization/`)

- **`Track(EntityKey Entity, Pose From, Pose To, int StartMs, int DurMs, Curve Curve, Channel Channel)`**,
  plus `Overlay` (init) and `EndMs`. `Track.ProgressAt(ms)` gives signed fixed-point progress.
  - `Entity` is the key the sampled pose carries, which is the **departure key** (P2-D2).
  - `To.Entity` is the rest key the track **lands on**, which is also its group key.
  - A non-overlay track **substitutes** that rest pose while it is live. An overlay track is drawn as an
    **extra** pose (merges, cursor glides).
- **`Curve { Linear = 1, Smooth, PingPong, Shake }`**:
  - PingPong is out and back, with smooth halves (lunge, pop).
  - Shake is 3 triangle cycles that start positive.
  - Walk, slide and sort use `policy.Ease`.
- **`Channel { Motion = 1, Height, Fade }`**:
  - Motion writes the cell, sub-offset, facing, entity and bag.
  - Height writes `Height` only.
  - Fade never affects sampling. It is the cross-bag hint, carrying `From` (the departure cell, when that bag
    is shown) and `To` (the arrival rest pose).
- **`Timeline(Scene Scene, ImmutableArray<Track> Tracks, ImmutableDictionary<LocationId, Facing> Facings)`**:
  - `Rest(scene)` creates a timeline with no tracks.
  - `SpanMs` is the latest `EndMs`, or 0.
  - `Commit(events, next, policy)`:
    1. **Snap (D7):** it drops every old track. New tracks are relative to the commit's 0 ms.
    2. It maps the events in order and updates the facings.
    3. It stores `next` with each body's facing taken from `Facings`. Facings persist across commits and
       through `Rewound`.
  - `Sample(ms)` walks the scene poses in order. Each rest pose is substituted by its live Motion track and
    lifted by its live Height track, and the live overlays landing on that key follow it. Overlays with no
    rest pose (cursor glides) come last.
  - Before a track starts, `Sample` gives its `From`. From `EndMs` on, it gives the rest pose.
- **Coordinates:**
  - `a = cell·16 + sub` is interpolated as `a + ((b − a)·f ≫ 16)`.
  - It is then normalized to `cell = ⌊(a + 8)/16⌋`, so sub is in −8…+7.
  - A walk's exact midpoint is therefore the next cell at sub −8 (the two-layer.md boundary picture).
- **Chaining:** a track whose departure key equals an earlier track's landing key, on the same channel and
  overlay flag, plays **after** it: it starts at the earlier track's `EndMs` and inherits its departure key.
  The earlier track's landing key is retargeted. As a result:
  - two walks in one commit play back to back;
  - a stack moved 0→1→2 stays `Stack(0)` and lands on `Stack(2)`.
  - Sort slides are never chained.
- **Mapping notes** (beyond the P2-D3 table):
  - Only bags in the new `Scene.Columns` animate; events about unshown bags add no track.
  - A body is a `Body(loc)` rest pose whose bag is the event's bag. Any other shown bag gets the
    **inventory cursor glide**: a new `EntityKey.Cursor(loc)` overlay, linear, using the new
    `RealizationPolicy.CursorGlideMs = 60`. It is drawn only while live, so the rest scene never holds a cursor.
  - A **jump** (`|dc| + |dr| > 1`: a wrap or a click teleport) snaps. The facing still turns to the
    wrap-normalized direction.
  - A walk's `To` is the event's `to` cell at sub 0, not `next`'s body pose, so that chained walks land
    correctly. **For 2.6:** when the rest body pose carries L2a `(Px, Py)`, the last hop should land on it.
  - `Bumped(Unnavigable)` with `Dir = null` adds no track. FailedPeek shakes the stack at `At`, if there
    is one.
  - A same-bag slide whose landing cell has no rest pose (an intermediate hop) lands on a synthesized
    centred `Stack(to)`.
  - `Harvested` pops its cell plus each **shown** yield cell, each once.
  - `Rewound` also drops the tracks built earlier in the same commit.
- **`Scene.Columns`** (`ImmutableDictionary<Guid, int>`) holds each shown bag's grid width.
  `Scene.FromState` fills it.

### P2-D2 pins (the risky cases)

- `StackSplit_SameBag_MovingPartKeepsDepartureKey`: in the middle of a slide, **two poses share
  `Stack(from)`**: the remainder at rest and the moving part. At the end they are `Stack(from)` and
  `Stack(to)`.
- `StackMerged_SameBag_SlidesOverTheTarget`: the target stays drawn at rest, and the moving part is an
  extra `Stack(from)` pose.
- `BagRearranged_SlidesInParallel`: in-flight keys are the old indices, which are unique because the
  permutation's entries are.

### Tests (`tests/Pockets.Realization.Tests/`)

- **`TimelineTests`** (26 tests) has one trace per P2-D3 row, at u = 0, ¼, ½, ¾, 1:
  - zoomed-ASCII centre bands for the walk, the two-walk chain and the lunge;
  - pose fields for the rest.
  It also covers:
  - snap (step 2 committed at 40 ms; its 0 ms sample is step 1's end pose);
  - reset (`Rewound` mid-walk puts the body at its cell centre);
  - PoseText determinism;
  - unshown bags, chaining, out-of-range sample times, and the policy durations.
- **`TimelineJourneyTests`** runs both journeys and commits every controller result. After every commit:
  - `Sample(SpanMs)` equals `Scene.FromState` of the new state, so **n = 1 ≡ VM** holds at u = 1;
  - intermediate samples stay in shown bags, with |sub| ≤ 8.
  It asserts that target-demo walks the body, and that two runs give an identical PoseText stream at the
  quarter points.

### Notes for 2.3

- Every track starts at 0 ms, or chains. 2.3's phase layout should set `StartMs` per phase, and chaining
  already sequences same-entity tracks.
- The P2-D13 realtime `Tick` must **not** snap. That needs a commit variant that keeps live tracks,
  rebased by the elapsed ms (for example `Commit(events, next, policy, atMs, snap: false)`), because today
  every `Commit` snaps and every time is relative to the latest commit.
- `Sample` regroups the tracks on every call. That is fine at today's sizes; precompute the groups if
  Godot's per-frame sampling (2.5) shows up in profiles.

## Addendum — Slice 2.3 as built (2026-10-28)

Golden-neutral, with no Core, App or Godot edit. `dotnet test` is green (Core 1032, App 54, Realization 89)
and `make parity-full` is green. The VM, events and 11 buffer goldens are byte-identical. The tests
(`ChoreographyTests`, 13 cases) were written first and seen failing (compile) before any production code.
The existing `TimelineTests` and `TimelineJourneyTests` pass unchanged, so u = 1 still equals the rest scene
over both journeys.

### Production (`src/Pockets.Realization/`)

- **`TurnPhase { Player = 1, Reaction, Ambient }`** and **`Choreography`** (`Choreography.cs`):
  - `Phases(events) → ImmutableArray<TurnPhase>` works **by kind and position**, never by intent type.
    It walks the events with an `ambient` flag, which resets on a new step (a new `Step`, or `Seq == 0`):
    - an envelope (`IntentApplied`, `IntentRejected` or `Rewound`) is Player and ends ambient;
    - `TimeAdvanced` is Ambient, and so is everything after it in the same step;
    - otherwise the kind decides. **Player:** `CursorMoved`, `BagEntered`/`Left`, `PanelOpened`/`Closed`,
      `Stack*`, `BagRearranged`, `Acquired`, `RecipeSelected`. **Reaction:** everything else (`Bumped`,
      `Harvested`, craft, plant, dialogue, triggers…).
    - A composite `Click` (or `SetCursor` + `Primary` in one commit) therefore lays out exactly like one
      turn (pinned).
  - `IsClock(events)` is true iff the commit is **non-empty** and every event lies in a clock span. A
    clock span is opened by a `Tick`/`SetElapsed` envelope or by `TimeAdvanced`; any other envelope,
    `Rewound` or new step without either closes it. A bare `Bumped` (the controller's failed-peek replay,
    target-demo lines 72/88) counts as input. **An empty commit is input and snaps**, which keeps 2.2's
    idle-commit pin.
- **`Timeline.Commit(events, next, policy, int atMs = 0)`**:
  - **Snap rule (P2-D13):** a player commit (`!IsClock`) drops every old track (D7). A clock commit keeps
    the old tracks that end after `atMs` and land in a bag `next` still shows, rebased by
    `StartMs −= atMs`. `StartMs` may go negative; sampling copes. `atMs` defaults to 0, so existing
    callers are unchanged. Snap is decided **from the events**, not from `TickMode`. A rogue tool step
    carries its own non-clock envelope, so it always snaps, and a bare clock `Tick` never does, in either
    mode.
  - **Layout:** each event's tracks are built once, in the original order (facings update in that order,
    as before). They are then added in `(phase, index)` order:
    - Player tracks start at 0, chained as in 2.2.
    - Reaction **and** Ambient tracks start at
      `offset = span0 · ReactionOverlapPercent / 100`, where `span0` is the latest `EndMs` of this
      commit's Player tracks (0 if there are none). So a refused walk's lunge plays at 0, and a harvest's
      pops start at 50% of the walk.
    - Fade tracks count toward `span0`, so a grab's ambient pop waits for half of its 125 ms fade.
    - In a clock commit there is no Player phase, so ambient tracks start at 0, next to the kept tracks.
  - **Chaining** now starts a chained track at `max(earlier.EndMs, its own StartMs)`, and it also chains
    onto kept tracks.
  - **`Rewound`:** only events after the **last** `Rewound` in the commit build tracks (the same effect
    as 2.2's clear).
- `ReactionOverlapPercent` (50) is the only offset knob, used for both reactions and ambient ("ambient
  starts when the reactions start"). No new policy field was added.

### Tests (`tests/Pockets.Realization.Tests/ChoreographyTests.cs`)

- **Classification:**
  - the phases by kind and position;
  - a new envelope or step ends an ambient run;
  - `IsClock` cases: Tick, SetElapsed, SetElapsed + TimeAdvanced + craft as in the golden, empty, Move,
    `Rewound`, bare `Bumped`, Grab + TimeAdvanced.
- **The rogue traces from the acceptance:**
  - grab-then-tick: the fade at 0, the ambient `Acquired` pop at 64 of a 128 ms phase, and the walk
    from before is snapped;
  - a walk into a tree: only the lunge, `StartMs = 0`, with the 2.2 trace;
  - a harvest: the walk at 0, and both pops (the tree cell and the shown yield) at 64. The same turn as
    `SetCursor` + `Primary` gives identical tracks.
  - Overlap at 0/50/100% gives a reaction start of 0/64/128.
- **Realtime:**
  - A tick at 40 ms mid-walk does not snap: the new 0 ms sample equals the old 40 ms sample, and the span
    is 88.
  - A key at 40 ms snaps.
  - A tick adds its pop at 0 next to the kept walk (`StartMs −40`), and a tick at 200 ms drops the
    finished walk.
  - `atMs` defaults to 0 and keeps the tracks in place.

### Notes for 2.4

- **What to pass as `atMs`:** `DebugCommandHandler` (and the TUI driver) should pass the clock delta of
  an `advanceTime`/`tick` response, i.e. the ms the clock advanced. Every other response passes 0; it
  snaps anyway.
  - In the journeys every `advanceTime` is ≥ 1000 ms, longer than any track, so clock commits in the
    golden normally keep nothing and show only their own ambient tracks.
  - Whatever the choice, it must be deterministic (never wall-clock), or the pose golden flaps.
  - `TimelineJourneyTests` passes 0 throughout. That is harmless for its u = 1 property, but it is not
    what a real driver does.
- **`SpanMs` after a clock commit** can include kept tracks with a negative `StartMs`. Sampling
  "u ∈ {0, ¼, ½, ¾, 1} of the last commit's span" over `[0, SpanMs]` is still right: 0 is "now", and kept
  tracks are mid-flight there.
- In v0, ambient events produce almost no tracks. Only an `Acquired`/`Harvested` after `TimeAdvanced`
  pops; craft and plant events re-render at n = 1 (P2-D3). Expect target-demo's clock commits to be
  mostly empty pose blocks.
- **For 2.7:** `FineTick` should join `IsClock`'s clock envelopes (no snap). The timeline should add no
  body walk for integrator-originated `CursorMoved` (2.7's own rule). `IsClock`'s pattern match on
  `Intent.Tick or Intent.SetElapsed` is the one place to extend.
