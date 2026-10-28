> **Source of truth: this repo copy.** Vault reading copy:
> `~/obsid/paths/projects/pockets/two-layer-phase1-plan.md` (mirrored with a hash header).
> Parent design: [two-layer.md](two-layer.md) (#23), Phase 1. Model: [target-demo-build-plan.md](target-demo-build-plan.md).

---

# Pockets — Two-Layer Phase 1 Slice Plan: Step Contract + Event Stream

*(Plan written 2026-10-27, after Phase 0 landed at `6265f8c`. It is grounded in `main` as
of that commit: `GameSession` ≈ 1,200 lines with `ApplyResult` / `TickFacilities` / `Tick`,
`GameController` (`HandleKey` / `HandleGridClick` / `HandleDsl` / `SyncClock` /
`AdvanceClock` / `Tick`), `DebugCommandHandler` shared by Godot and mock-godot, the
journey runner with VM and buffer goldens, and `make parity` / `parity-full`.)*

**PLAN.** Decisions are numbered **P1-D1 … P1-D9** at the end. **Ratified 2026-10-28:
Aaron accepted all nine as recommended.**

## Goal

Every state change in the game goes through one pure function:

```
Sim.Step(SimState s, IReadOnlyList<Intent> intents) → StepResult(SimState s', ImmutableArray<SimEvent> events)
```

Frontends (TUI, Godot, mock-godot) send `Intent`s and consume `SimEvent`s at **n = 1**,
meaning events are applied instantly and nothing is animated yet. Parity gains an
**event-stream golden** next to the VM golden. **No behaviour changes:** the VM checkpoint
golden and the TUI buffer goldens stay **byte-identical through every slice**.

Phase 1 also carries the Phase 0 revisit items that belong here:
- the counter RNG held in state;
- the ban on per-change `SetItem` in step paths;
- the `IBagView` / `IBagWriter` seam;
- `TimeClass` declared on every rule;
- the event-log retention policy;
- the `BeltAdvanced` event shape.

## Principles (inherited from the target-demo plan, plus two new ones)

1. **Core-first, view-model out**, unchanged. Events are a *second* projection next to the
   VM. They do not replace it.
2. **The parity gate is the ratchet.** A slice lands only if:
   - `dotnet test` is green;
   - `make parity-full` is green;
   - the goldens are untouched, except where the slice says it adds a golden.
3. **The golden-neutral rule (new).** No Phase 1 slice may need `make record-goldens` for
   the existing VM or buffer goldens. If a slice finds it must re-record, that is a
   behaviour change. The slice stops, and the change becomes a decision for Aaron.
4. **No slice lands touching only one frontend (new for events).** Whatever the TUI starts
   consuming, `DebugCommandHandler` (Godot and mock-godot) exposes in the same slice.

## What the code looks like today, and what Phase 1 makes of it

```
 TODAY                                                PHASE 1 END STATE
 ─────                                                ─────────────────
 GameView / GameSceneController                       GameView / GameSceneController / mock
   │ HandleKey(key, rng)  HandleGridClick  Dsl          │ HandleKey … (unchanged signatures)
   ▼                                                    ▼
 GameController ── _session.ExecuteGrab() …           GameController ── maps input → Intent[]
   │                SyncClock → N × _session.Tick()     │                 clock → Intent.AdvanceTime / Tick
   ▼                                                    ▼
 GameSession (record)                                 GameSession = shell: undo stack, action log, event log
   ApplyResult: tool result → TickFacilities            │  Step(intents) ─▶ Sim.Step(SimState, intents)
     → PlantLogic.TickPlants → RunTransitionHooks       ▼
     → push undo, log, TickCount++                    Sim.Step (pure, Core)
   Tick(): same minus the tool                          1 apply intents (existing tool code)
                                                        2 rule phase: registered IRules (facility, plant),
 FeedbackPulse (controller field, consume-on-read)         each with Kind + TimeClass, writes via IBagWriter
 Random: passed in / new Random() fallbacks             3 transition hooks (UiLedger, known recipes, zones)
 Bag.Id = Guid.NewGuid()                                4 events collected in order (Step, Seq)
                                                      FeedbackPulse → SimEvent.Bumped
                                                      SimRng (counter RNG) inside SimState
```

## Slices

Each slice is one worker session. It lands Core + TUI + `DebugCommandHandler` (Godot /
mock) where relevant, plus tests, and is gated as in principle 2. The order and
dependencies are given in §Order below.

### Slice 1.0 — Contract types *(the keystone, pure additions)* ✓ landed (see §Addendum)
- Add the new namespace `Pockets.Core.Sim`, containing:
  - `Intent` (sealed record hierarchy, empty except for `Intent.Tick`);
  - `SimEvent` (a sealed record hierarchy with `Step` and `Seq` on every event);
  - `StepResult`;
  - `RuleKind { Field, Transfer }`;
  - `TimeClass { ClosedForm, EventSkippable, BoundedCatchUp, MustSimulate }`, plus `MaxDelta` for bounded;
  - `IRule` (`Kind`, `Time`, `EventKinds`).
- The `SimEvent` taxonomy is *declared* here. It is filled in by slice 1.5, and it
  reserves `BeltAdvanced(ChainId, Dir, Count, HeadCell)` (P1-D3).
- **Write `SimState` boundary inventory (in the plan addendum):** classify every field of
  `GameSession` and `GameState` as L1 sim state, session shell or frontend-ephemeral.
  - **L1 sim state:** `GameState` (grid, bags, cursor, hand, toolbar, `Ui` ledger,
    dialogue, known recipes) plus `TickCount` and `Elapsed`. These are serialized in the
    VM, so they must be L1.
  - **Session shell:** `UndoStack`, `ActionLog`, and the event log to come.
  - **Frontend-ephemeral:** controller `_focus` and the `FeedbackPulse` field.
  - **Modal state** (`SplitMode`, `RecipeMenu`, dialogue): L1, per P1-D5 (ratified).
- **Acceptance:** the types compile, and there are unit tests for `SimEvent` ordering and
  equality. Zero call sites change, so the goldens trivially stay identical.
- *Depends on: nothing. Everything depends on it.*
- **Risk:** low. The only way it goes wrong is over-designing the taxonomy before 1.5.
  Keep the records minimal.

### Slice 1.1 — Determinism groundwork: `SimRng` in state (G2) *(parallel with 1.2)* ✓ landed (see §Addendum — Slice 1.1)
- Add `SimRng`, a counter-based RNG: `Hash(WorldSeed, Step, Stream, Draw)`, where
  `Stream` is an enum of purposes. It is an integer-only `SplitMix64`/`PCG`-style hash and is
  held in `SimState` (`WorldSeed`, plus a draw counter per stream per step). Add
  **cross-runtime golden vectors**: a unit test that pins the first 64 outputs to literal
  values, so a future .NET version cannot silently change them.
- Replace every in-step use of an unseeded or passed-in `Random`. The sites found today:

  | Site | Today | Phase 1 |
  |---|---|---|
  | `GameController.HandleKey` → `ExecuteAcquireRandom(rng ?? new Random())` | caller-supplied or unseeded | `Intent.AcquireRandom` draws from `SimRng(Stream.Acquire)` |
  | `GameView._rng = new()`, `GameSceneController._rng = new()` | frontend-owned RNG | **removed**; frontends no longer own randomness |
  | `Dsl/Opcodes.cs:169` `ToolAcquireRandom(new Random())` | unseeded | `SimRng(Stream.Dsl)` |
  | `Data/RecipeRegistry.cs:75` (seedling → `WildernessGenerator.Generate(template, new Random())`) | **unseeded, inside a facility tick** | the recipe output factory takes a `SimRng` (signature change) |
  | `Data/GeneratorBuiltins.cs:43,109` `rng ??= new Random()` | unseeded fallback | parameter becomes required; callers pass `SimRng` (in step) or the init RNG |
  | `GameInitializer.cs:521,543,585,658` `random ??= new Random()` | unseeded fallback | parameter becomes required |
  | `GameInitializer.cs:60,243` `new Random(seed ?? DemoSeed)`, `new Random(WildernessSeed)` | **seeded, init-time, outside Step** | **kept as is** in Phase 1, so the demo world and the goldens are unchanged (P1-D7) |

- Bridge: an adapter `SimRng.AsRandom()` (subclassing `System.Random`) lets the existing
  `Random`-typed generator code take a `SimRng` without being rewritten.
- **Acceptance:**
  1. `grep -rn "new Random()" src/` returns zero hits, enforced by an architecture test in
     Core.Tests (a text scan of `src/**/*.cs`).
  2. A new test: two sessions with the same seed, given the same `AcquireRandom` ×50 and
     seedling-craft sequence, produce identical `GameState`.
  3. The goldens are unchanged. As of `6265f8c`, neither journey presses AcquireRandom or
     crafts the seedling; the slice re-verifies this before landing.
- *Depends on: 1.0.*
- **Risk:** medium. Recipe output factories are `Func<RecipeOutput>` closures, so threading
  an RNG through them changes the `Recipe` signature. That touches every recipe definition
  site, but mechanically.
- Also recorded for later, not done here: `WildernessTemplate` uses `double` fill ratio and
  loot weights (a G1 violation inside a step path, via the seedling). See P1-D7.

### Slice 1.2 — `Sim.Step` skeleton: ApplyResult / Tick on top of Step *(no new behaviour)* ✓ landed (see §Addendum — Slice 1.2)
- `Sim.Step(SimState, intents)` is extracted from `ApplyResult` + `TickFacilities` +
  `PlantLogic.TickPlants` + `RunTransitionHooks`, keeping the **exact existing order**:
  1. apply the intent's tool;
  2. if the step advances time, tick facilities;
  3. tick plants;
  4. run the transition hooks.
- The step carries an explicit `advancesTime` flag, so rogue and realtime keep today's
  semantics (P1-D2):

  | Mode | Today | Phase 1 Step |
  |---|---|---|
  | Rogue, successful action | tool → tick facilities → plants → hooks | `Step([action])`, advancesTime = true |
  | Realtime, player action | tool → hooks (no tick) | `Step([action])`, advancesTime = false (immediate, no queuing) |
  | Realtime, clock tick (`SyncClock` loop, `Tick()`) | facilities → plants → hooks | `Step([Intent.Tick])`, advancesTime = true |
  | Failed action | log "FAILED", no undo push | `Step` returns `IntentRejected(reason)`; the shell logs exactly as today |
  | No-op success (`result.State == Current`) | nothing | `Step` returns s' == s and **no events**; the shell does nothing |

- `GameSession` becomes the **shell**. It calls `Sim.Step`, pushes undo, appends the
  action-log string (produced **exactly as today** by the existing `formatLog` lambdas,
  P1-D6), and increments `TickCount`. The public `Execute*` methods keep their signatures
  and become thin wrappers that build one intent and call `Step`.
- In this slice the events are coarse: `IntentApplied(intent)`, `IntentRejected`,
  `TimeAdvanced(step)`, `CraftCompleted(facility, recipe)` (taking over today's
  `completionLogs`), and `UiTriggerFired(trigger)`.
- **Acceptance:**
  1. All Core and App tests pass **unmodified**.
  2. `parity-full` is green with byte-identical goldens.
  3. A new test asserts, for every journey step of `smoke.journey.json` driven in-process,
     that `Sim.Step` is the only path that changed `Current`. This is done with a debug
     counter on the shell.
- *Depends on: 1.0.*
- **Risk: high, and this is the biggest slice.** `ApplyResult` has subtle rules (the no-op
  check by record equality, the order of hooks against ticks, the log-ordering of
  completion lines). The mitigation is to extract by moving code, with no rewriting, and to
  have the parity goldens catch any ordering slip. If the worker cannot keep it
  golden-neutral, split it into 1.2a (`Tick` path) and 1.2b (`ApplyResult` path).

### Slice 1.3 — Intents everywhere: route every controller path through `Step` ✓ landed (see §Addendum — Slice 1.3)
- `GameController` maps every input to `Intent`s:
  - `HandleKey` → one intent per `GameKey`;
  - `HandleGridClick` → `Intent.Click(panel, pos, type)`;
  - `HandleBackClick` → `Intent.Back`;
  - `HandleDsl` → `Intent.Dsl(expr)`;
  - `SyncClock` / `AdvanceClock` → `k × Intent.Tick`, keeping today's `TickLengthMs = 1000`
    semantics (P1-D4);
  - modal and menu actions (split adjust, recipe menu, dialogue advance) → intents per P1-D5;
  - every intent carries its target `LocationId`, so focus never leaks into replay.
- `DebugCommandHandler` (Godot and mock-godot) builds intents the same way, so the
  journey's `advanceTime` and `keys` steps both become intent streams.
- The session's `Execute*` methods become `internal` and are called only from `Sim.Step`'s
  intent dispatcher. An architecture test asserts that no frontend or tool project calls
  them.
- Add an **intent log** to the shell, as an append-only `ImmutableList<(int Step,
  Intent)>`. This is the A6 "intents are the only input" guarantee, and it is what 1.7's
  replay check consumes.
- **Acceptance:**
  1. Goldens identical.
  2. **Replay test:** for `smoke` and `target-demo`, record the intent log, re-run it from
     the initial profile in a fresh session, and get an identical VM checkpoint stream.
  3. The architecture test above passes.
- *Depends on: 1.2. Also depends on 1.1 for the AcquireRandom intent (or stub that intent
  until 1.1 lands).*
- **Risk:** medium. The controller holds hidden state (`_focus`, split modal, recipe menu).
  P1-D5 decides which of it becomes L1.

### Slice 1.4 — Rule registry, `TimeClass` everywhere, `IBagView`/`IBagWriter`, SetItem ban ✓ landed (see §Addendum — Slice 1.4)
- Add `IBagView` (read-only: `Columns`, `Rows`, `Cell(i)`, `Neighbourhood(i)`) and
  `IBagWriter` (`Set(i, cell)`, `Commit() → Grid`).
  - The one Phase 1 implementation is `ImmutableBagWriter`, backed by
    `ImmutableArray<Cell>.Builder` with a single `MoveToImmutable` per bag per step. This is
    the cheap a1 style from spike B.
  - The interface is the seam a mutable struct-of-arrays bag implementation (model B) slots
    into in Phase 7 without rule rewrites.
- Rewrite the existing tick behaviours as registered rules, each declaring `Kind` and `TimeClass`:

  | Rule | Kind | TimeClass | Reason |
  |---|---|---|---|
  | `FacilityCraftRule` (`FacilityLogic.Tick`: consume inputs → countdown → outputs) | Transfer | `EventSkippable` | The countdown is closed-form, and completion is a discrete event you can jump to (Δ ≥ remaining ⇒ complete, then re-check inputs). |
  | `PlantGrowthRule` (`PlantLogic.TickPlants`) | Field (own cell only) | `ClosedForm` | Per-cell growth counter with a cap; no neighbours. |
  | Seedling wilderness generation (inside the facility output) | (part of FacilityCraftRule) | the same, with an RNG stream | uses `SimRng(Stream.Wilderness, step)` from 1.1 |

- **The SetItem ban:**
  - **Rule:** inside any `IRule` and any code reachable from `Sim.Step`'s rule phase, grid
    writes go through `IBagWriter`. Never call `Grid.SetCell`/`Cells.SetItem` in a loop.
  - **Today's violations, to port:**
    - `FacilityLogic.cs:190,191,206` (input consume and output place, in loops);
    - `PlantLogic.cs:56` (per-plant in a loop).
  - **Allowed:** player-tool single-cell writes in `GameState` (one `SetCell` per action;
    there are about 10 sites) are not step-path loops and stay as they are.
  - **Enforcement:** an architecture test scans `Models/*Logic.cs`, `Sim/**` and `Rules/**`
    for `.SetCell(` / `.SetItem(` and fails on any hit.
- **Registry test:** every `IRule` in the assembly declares `TimeClass` explicitly. There is
  no default value: the enum has no zero member, and reflection asserts the declarations.
  `BoundedCatchUp` must give `MaxDelta > 0`.
- A per-bag scheduler field `BagClock { Rate = 1, Mode = Observed }` is added and is always
  Observed/1 in Phase 1. Nothing reads it yet (Phase 4 does).
- **Acceptance:**
  1. Goldens identical.
  2. Facility and plant unit tests pass unmodified.
  3. The new architecture tests (the ban and the TimeClass declarations) are green.
  4. A micro-benchmark asserts that a `FacilityCraftRule` step on the Workshop does not
     allocate more than today. Measure with a Stopwatch harness like spike B; the
     regression bound is ≤ today's allocations.
- *Depends on: 1.2. It can run in parallel with 1.3, since the touch-points barely overlap:
  `FacilityLogic`/`PlantLogic` against the controller and session.*
- **Risk:** low to medium. Facility completion ordering (`OrderedFacilities`) must be
  preserved exactly; the registry iterates rules then facilities in the existing canonical
  order.

### Slice 1.5 — The SimEvent stream for existing mechanics (+ `BeltAdvanced` shape) ✓ landed (see §Addendum — Slice 1.5)
- Replace the coarse 1.2 events with semantic events. Each tool and rule emits what
  happened, along with the cause and direction:

  | Mechanic | Events |
  |---|---|
  | Cursor move / bump on an unnavigable cell / failed peek | `CursorMoved(bag, from, to)`, `Bumped(bag, at, dir, reason)` (**replaces `FeedbackPulse`**) |
  | Grab / drop / pickup-to-toolbar / split / quick split | `StackMoved(item, count, fromLoc, toLoc, cause)`, `StackSplit`, `StackMerged` |
  | Sort | `BagRearranged(bag, permutation)` |
  | Enter / leave / look-in | `BagEntered(bag, depth)`, `BagLeft`, `PanelOpened(loc)` |
  | Facility | `CraftStarted`, `CraftProgressed(facility, remaining)`, `CraftCompleted(facility, recipe, outputs)` |
  | Plants / harvest | `PlantGrew(cell, stage)`, `Harvested(cell, yields)` |
  | Acquire | `Acquired(item, count, cell, source)` |
  | Chrome / dialogue / recipes | `UiTriggerFired(trigger)`, `DialogueAdvanced`, `RecipeLearned(id)` |
  | Rejections | `IntentRejected(intent, reason)` |
  | **Belts (reserved, no belts in game yet)** | `BeltAdvanced(chainId, dir, count, headCell)`, one per chain per step. Per-item `Moved` events are emitted only at chain boundaries (`Emitted` / merge entry / `Consumed`). See P1-D3. |

- **Coverage check (the Phase 1 form of the completeness contract, P1-D1):** for every step
  of both journeys, every cell, hand slot, toolbar slot or cursor that differs between s and
  s' must be **named by at least one event**. A test helper diffs the states and checks the
  event payloads.
- **Acceptance:**
  1. Goldens identical.
  2. The coverage check is green on both journeys.
  3. One ASCII-style event test per mechanic, in the spike A trace style (input state +
     intent → expected event lines).
- *Depends on: 1.3 and 1.4.*
- **Risk:** medium. Event payloads must not contain process-fresh GUIDs where they will be
  serialized (see P1-D8).

### Slice 1.6 — Frontends consume events at n = 1
- `ControllerResult` gains `Events`. The TUI's `GameView` and Godot's `GameSceneController`
  read the step's events:
  - **Bump and failed-peek cues** come from `Bumped` events. `FeedbackPulse` and
    `ConsumeFeedbackPulse` are deleted.
  - **Craft completion and dialogue cues** come from events.
  - Everything else still re-renders from the VM. At n = 1 an event is just "apply now".
- `DebugCommandHandler` returns `events` (canonical JSON, P1-D8) in every response, so the
  Godot WebSocket driver and mock-godot see the same stream the TUI sees.
- **Acceptance:**
  1. Goldens identical, including the TUI buffer goldens. Bump rendering must be visually
     identical.
  2. The App tests for bump and failed peek pass, rewritten to assert via events.
  3. `make parity` cross-driver is green with `events` present on both drivers.
  4. Godot builds. The real-Godot pass (`parity-godot`) is on Aaron's Windows box and not a
     gate (as before).
- *Depends on: 1.5.*
- **Risk:** low to medium. The Godot side cannot be run in WSL; it is covered by mock-godot,
  which shares `DebugCommandHandler`.

### Slice 1.7 — Event-stream golden + replay gate in parity
- The journey runner gets `--events-out <file>`: one canonical line per event
  (`step seq kind payload…`, invariant culture, no GUIDs per P1-D8), emitted per journey step.
- Makefile changes:
  - **`parity`**: cross-driver diff of the smoke event streams (tui against mock-godot).
  - **`parity-full`**: cross-driver diff of the target-demo event streams, plus a regression
    check against the new golden **`journeys/goldens/target-demo.events`**.
  - **`record-goldens`**: also writes the events golden.
- **Replay gate** (`--replay-check`): re-run the recorded intent log from the initial
  profile, then assert an identical VM stream **and** an identical event stream. This is
  A6/G6 in its Phase 1 form.
- **Acceptance:**
  1. `make parity-full` is green with the **new** events golden committed. This is the only
     golden this phase adds.
  2. The VM and buffer goldens are unchanged.
  3. The replay check passes on both journeys.
  4. A deliberately mutated event (in a test) fails the gate.
- *Depends on: 1.6.*
- **Risk:** low. The Windows shell-agnostic Makefile rule from the portability slice
  applies: no sh-only recipes, and comparison is folded into the runner.

### Slice 1.8 — Event-log retention and compaction policy (+ undo interplay)
- The shell keeps an **`EventLog`**, a chunked append-only log of struct events in the
  spike B (b) style.
  - **Retention policy (P1-D9):** keep events back to the **oldest retained snapshot**.
  - **Snapshots:** in rogue mode these are the undo stack (up to 1000). In realtime they
    are a checkpoint every 8 steps, ring-bounded to the last 1000.
  - **Compaction:** older events are dropped. Phase 7 flushes them to the save file
    instead.
  - **Hard memory cap:** 8 MB for the log. Past the cap, the oldest chunks are dropped and
    a `LogTruncated` marker is recorded.
- **Undo:** undo restores the snapshot, **truncates** the event and intent logs back to that
  step (the log mirrors the current timeline), and emits a single `Rewound(toStep)` event
  so L2 snaps (lossy L2 history, A3).
- **Acceptance:**
  1. Unit tests for retention, compaction, the cap and undo truncation.
  2. A soak test: 10 minutes of simulated realtime on the demo profile stays under the cap,
     with steady-state gen0 GC equal to or below today's.
  3. Goldens identical.
  4. The events golden is unchanged. Neither journey uses undo as of `6265f8c`; `Rewound`
     is covered by unit tests.
- *Depends on: 1.5 (it can run in parallel with 1.6 and 1.7).*
- **Risk:** low.

### Slice 1.9 — Phase 1 close-out
- Update the docs:
  - `two-layer.md`: mark Phase 1 ✓, update the G1–G8 status column (G2, G5 and G6 → ✓;
    G1 shows the remaining `WildernessTemplate` doubles);
  - `CLAUDE.md`: an architecture section (Sim.Step, intents, events, the rule registry,
    and the bans);
  - `tools/journey-runner/README.md`: the events golden and the replay check.
- Write a short as-built drift note in the style of `parity-drift-report.md`.
- **Acceptance:** docs only; all gates green.
- *Depends on: 1.0 through 1.8.*

## Order

```
 1.0 contract types
  ├──▶ 1.1 SimRng (G2) ────────────────────────┐
  └──▶ 1.2 Step skeleton ──┬──▶ 1.3 intents ───┤
                           └──▶ 1.4 rules/BagWriter/TimeClass/ban ──┴──▶ 1.5 events ──┬──▶ 1.6 frontends n=1 ──▶ 1.7 event golden + replay
                                                                                      └──▶ 1.8 event log + undo
                                                                    1.0–1.8 ──▶ 1.9 close-out
```

- **Recommended serial order:** 1.0 → 1.2 → 1.1 → 1.3 → 1.4 → 1.5 → 1.6 → 1.7 → 1.8 → 1.9.
- **Safe parallel pairs**, if two workers are free: (1.1 ∥ 1.2) and (1.3 ∥ 1.4), and later
  (1.7 ∥ 1.8). As in the target-demo plan, the default is one worker per project and serial.

## Risks (ranked)

1. **1.2 golden drift.** `ApplyResult`'s ordering is load-bearing (hooks after ticks, completion
   logs after the action log line, no-op detection by record equality). Mitigation: move code,
   don't rewrite it, keep goldens byte-identical, and split into 1.2a/1.2b if needed.
2. **Hidden controller state.** Focus, the split modal, the recipe menu and the feedback
   pulse are not in `GameState` today. P1-D5 settles what becomes L1. If it is put in the
   wrong place, either replay breaks (state missing from L1) or the VM changes (state moved
   into it).
3. **Recipe factory signature churn** (1.1). `Func<RecipeOutput>` → `Func<SimRng,
   RecipeOutput>` touches every recipe definition. The change is mechanical but wide.
4. **GUIDs in events** (1.5 and 1.7). Bag ids are `Guid.NewGuid()`, so events naming bags
   would break cross-driver diffs. P1-D8.
5. **Godot is unverifiable in WSL.** As before, mock-godot plus the shared
   `DebugCommandHandler` is the gate, and the real-Godot pass happens on Aaron's box.
6. **Scope creep into Phase 3 and later.** No new rules (plaque, belts), no realization,
   no prediction, no 8 Hz. Any of these appearing in a Phase 1 PR is a review flag.

## Decisions for Aaron

**Ratified 2026-10-28: all as recommended** (Aaron: "Go with recs"). P1-D1's amendment is
applied to contract rule 1 in [two-layer.md](two-layer.md).

| # | Decision | Options | Recommendation | Status |
|---|---|---|---|---|
| **P1-D1** | Event **completeness** in Phase 1 | (a) state reconstructible from events alone (heavy: every payload carries full data); (b) **intent-log replay determinism + an event coverage check** (every changed cell/slot is named by some event) | **(b)**. Amend contract rule 1 of two-layer.md to "replay(intents) ≡ state; events cover every change". Full state-from-events moves to Phase 7, when the log becomes the save format. | ✓ Ratified 2026-10-28 |
| **P1-D2** | Realtime player actions: immediate, or queued to the next step | (a) **immediate, intent-only step, no time advance** (today's behaviour); (b) queue to the next tick, with prediction | **(a)** in Phase 1, because (b) is a behaviour change. Queuing plus dry-run prediction is Phase 5 (D6). | ✓ Ratified 2026-10-28 |
| **P1-D3** | `BeltAdvanced` event granularity | (a) per-item `Moved` (spike B: ≈ 203 events/step); (b) **one `BeltAdvanced(chain, dir, count, head)` per chain per step**, with per-item events only at chain boundaries | **(b)**. L2 derives per-item poses from the chain plus occupancy. Only the shape is declared in Phase 1, because no belts exist yet. | ✓ Ratified 2026-10-28 |
| **P1-D4** | Step rate in Phase 1 | (a) **keep `TickLengthMs = 1000`** (1 tick/s, crafting durations unchanged); (b) switch to 8 Hz now | **(a)**. 8 Hz rescales every duration and is a behaviour change. It moves to Phase 3/5 with a `StepsPerSecond` constant and durations in seconds. | ✓ Ratified 2026-10-28 |
| **P1-D5** | Is modal UI state (split modal, recipe menu, dialogue cursor, panel focus) L1? | (a) all of it is L1, via intents; (b) **state that changes outcomes or is in the VM is L1; pure presentation is frontend-only** | **(b)**. Recipe menu and dialogue are serialized in the VM, and `SplitMode` lives on `GameSession` and decides the split amount. All three therefore must replay, so they are L1 and are changed by intents. **Panel focus** stays a controller field, but every intent carries its **target location explicitly** (as `ExecutePrimary(loc)` already does), so replay never depends on focus. `FeedbackPulse` becomes the `Bumped` event. | ✓ Ratified 2026-10-28 |
| **P1-D6** | Action-log strings | (a) **keep them produced as today** (they are in the VM golden); (b) derive them from events now | **(a)**. Deriving the log from events is a nice follow-up but a golden risk. Revisit in Phase 3. | ✓ Ratified 2026-10-28 |
| **P1-D7** | Init-time seeded `Random` and the `double` weights in `WildernessTemplate` | (a) **keep both in Phase 1** (golden-neutral; they are init-time, or in-step only via the seedling); (b) convert everything to `SimRng` and per-mille ints now (re-records goldens) | **(a)**, recorded as open G1/G2 debt. Convert them in Phase 3, when the rule engine re-homes generation. The cross-runtime risk (`System.Random`'s seeded sequence is not contractual) is accepted for single-player until then. | ✓ Ratified 2026-10-28 |
| **P1-D8** | How events reference bags (ids are process-fresh GUIDs) | (a) deterministic bag ids now (G7: counter in state; touches every `new Bag`); (b) **canonical renaming at serialization** (`bag#1, bag#2…` in order of first appearance in the stream; in-memory events keep the GUIDs) | **(b)** for Phase 1. It makes the golden and the cross-driver diff deterministic at zero refactor cost. Deterministic ids (G7) stay with Phase 3, where they are needed for multiplayer and save/replay. | ✓ Ratified 2026-10-28 |
| **P1-D9** | Event-log retention | (a) unbounded; (b) **back to the oldest retained snapshot, 8 MB hard cap, dropped (Phase 7: flushed to the save)** | **(b)**. Spike B: ~23 MB per 10 minutes for one fully churning bag, so unbounded is not an option for realtime. | ✓ Ratified 2026-10-28 |

## Worker notes

- Each brief inherits the repo `CLAUDE.md`: TDD, LINQ method syntax, doc comment per
  function, Sonnet sub-agents for implementation, land on `main` (fetch, rebase, push, never
  force), and `make parity-full` green before push.
- **Golden-neutral is the default acceptance.** The only slice allowed to touch
  `journeys/goldens/` is 1.7, which adds `target-demo.events`.
- Spikes stay in `spikes/`. Phase 1 does not import from them. `ImmutableBagWriter` is
  re-implemented under TDD in Core, in the spike's a1 style.
- Every slice brief should quote its row from §Decisions, so the worker knows which
  recommendation it is building on.

## Addendum — Slice 1.0 as built (2026-10-28)

### Contract types (`src/Pockets.Core/Sim/`)

| Type | Shape | Notes for later slices |
|---|---|---|
| `Intent` | abstract record, private ctor, sealed nested cases; only `Intent.Tick` | 1.3 adds one case per controller path, each with its target `LocationId` (P1-D5). |
| `SimEvent` | abstract record, private ctor, `Step` + `Seq` on every case; `SimEvent.Order` compares `(Step, Seq)` only | Declared cases: `IntentApplied(intent)`, `IntentRejected(intent, reason)`, `TimeAdvanced`, `CraftCompleted(facilityBagId, recipeId)`, `UiTriggerFired(trigger)` (the coarse 1.2 set) and reserved `BeltAdvanced(ChainId, Dir, Count, HeadCell)` (P1-D3; `Dir` is `Models.Direction`, `HeadCell` a row-major cell index). 1.5 adds the semantic events. |
| `StepResult<TState>` | `(State, ImmutableArray<SimEvent> Events)`, element-wise event equality, `Unchanged(s)` | Generic so 1.2 picks the concrete `SimState` (inventory below). |
| `RuleKind` | `Field = 1, Transfer = 2` | No zero member. |
| `TimeClass` | `ClosedForm = 1, EventSkippable, BoundedCatchUp, MustSimulate` | No zero member, so 1.4's reflection test can reject undeclared values. |
| `IRule` | `Kind`, `Time`, `MaxDelta` (default 0), `EventKinds` (`ImmutableArray<Type>` of `SimEvent` cases) | `BoundedCatchUp` rules must override `MaxDelta > 0` (1.4 asserts it). |

**`Seq` is step-global in Phase 1.** Contract rule 2 orders by `(Step, ScheduleOrder(bag), Seq)`.
Phase 1 steps every bag in one canonical pass, so the step's emission order already encodes
`ScheduleOrder`, and `Seq` is simply the event's index in the step's list. When per-bag scheduling
arrives (Phase 4), either keep `Seq` global or add `BagId` to the key; nothing in 1.0 forecloses it.

The hierarchies are closed by private constructors. The one non-private constructor is the
compiler's protected record copy constructor (C# forbids making it private on a non-sealed
record); it can only clone an existing case, and a test pins that every case is a sealed nested type.

### SimState boundary inventory

Every field of `GameSession` and `GameState` (plus the controller and frontend fields that hold
state), classified per P1-D5 (ratified). **L1** = must be in `SimState` and change only through
`Sim.Step`. **L1 content** = read-only inputs to `Step` that no step changes (definitions loaded at
startup). **Shell** = `GameSession` bookkeeping around `Step`. **Ephemeral** = frontend or
controller only; must never influence an outcome.

| Owner | Field | Class | Reason |
|---|---|---|---|
| `GameState` | `Store` (all bags, grids, cells, stacks, facility state, nested bags) | **L1** | The world. VM-serialized. |
| `GameState` | `Locations` (H, T, B, W, C: bag id, cursor, breadcrumbs) | **L1** | Cursor, hand, toolbar, open panels (W, C). VM-serialized; tools resolve against it. |
| `GameState` | `ItemTypes` | L1 content | Static definitions; no step changes them. |
| `GameState` | `Ui` (UiLedger) | **L1** | Changed by triggers in the transition hooks; VM-serialized. |
| `GameState` | `Dialogue` (DialogueState) | **L1** | Dialogue cursor/queue changes outcomes and is in the VM (P1-D5). Note: undo carries it forward (monotonic); 1.8 must keep that. |
| `GameState` | `ToolbarPickup` | L1 content | Profile flag set at init; read by tools. Never changed by a step. |
| `GameState` | `KnownRecipes` | **L1** | Grows on pickup; decides the recipe menu; VM-serialized. |
| `GameState` | `ZonesReached` | **L1** | Grows on entering wilderness; VM projects count + wedges. |
| `GameSession` | `Current` | **L1** | Becomes the `GameState` part of `SimState`. |
| `GameSession` | `TickCount` | **L1** | The step number; VM-serialized. Carried forward across undo. |
| `GameSession` | `Elapsed` | **L1** | Clock readout; VM-serialized; monotonic across undo. |
| `GameSession` | `SplitMode` (SplitModeState) | **L1** | Decides the split amount (P1-D5). Not in the VM. Today it lives on the session, not in `GameState`; 1.2/1.3 move it into `SimState`. |
| `GameSession` | `RecipeMenu` (RecipeMenuState) | **L1** | Decides which recipe Enter selects; VM-serialized (P1-D5). Same move as `SplitMode`. |
| `GameSession` | `Recipes`, `FacilityRecipeMap`, `Book` (DialogueBook) | L1 content | Definitions loaded from `/data`; `Step` reads them. |
| `GameSession` | `TickMode` | L1 content | Fixed per session; decides `advancesTime` (P1-D2). |
| `GameSession` | `UndoStack` | Shell | Snapshot history; 1.8 retention baseline. |
| `GameSession` | `ActionLog` | Shell | Strings produced as today (P1-D6); VM-serialized, but written only by the shell. |
| `GameSession` | `MaxUndoDepth` | Shell | Config for the undo stack. |
| `GameSession` | intent log, event log *(to come, 1.3 / 1.8)* | Shell | Append-only; mirror the current timeline. |
| `GameController` | `_session` | Shell holder | The controller owns the session reference. |
| `GameController` | `_focus` | Ephemeral | Input routing only. Every intent carries its target location (P1-D5), so replay never reads focus. |
| `GameController` | `_feedbackPulse` | Ephemeral | Consume-on-read cue; becomes `SimEvent.Bumped` in 1.5/1.6 and is deleted. |
| `GameController` | `_clock` (IGameClock) | Ephemeral | Wall/virtual time source; the scheduler turns it into `Intent.Tick`s (P1-D4). Not an input to `Step`. |
| `GameView` / `GameSceneController` | `_rng` | **Removed in 1.1** | Frontends stop owning randomness; draws come from `SimRng` in `SimState`. |
| `GameSceneController` / `GridDrawControl` | widget refs, `_hoveredCell`, `_cursorPos` mirror | Ephemeral | Presentation only. |

**Consequence for 1.2:** `SimState` ≈ `GameState` + `TickCount` + `Elapsed` + `SplitMode` +
`RecipeMenu` (+ `SimRng` from 1.1), with the L1-content fields reachable from it but never
written by `Step`. Moving `SplitMode`/`RecipeMenu` must change no VM field: the VM serializes
`recipeMenu` from the session today and does **not** serialize `SplitMode` at all, so the move is
golden-neutral only if the serializer keeps exactly that projection.

## Addendum — Slice 1.2 as built (2026-10-28)

**Golden-neutral, done in one slice; no 1.2a/1.2b split was needed.** The VM checkpoint golden and
the 11 TUI buffer goldens are byte-identical, and smoke and target-demo diff clean across tui and
mock-godot.

| Piece | Where | Notes |
|---|---|---|
| `Simulation.Step(SimState, IReadOnlyList<Intent>) → StepResult<SimState>` | `src/Pockets.Core/Sim/Simulation.cs` | This is the plan's `Sim.Step`. The class is named `Simulation` because a type named `Sim` inside `Pockets.Core.Sim` is shadowed by the namespace when it is called from `Pockets.Core.Models`. The code was **moved** from `GameSession` and not rewritten: `TickFacilities`, `OrderedFacilities` (now public, and `ActiveCrafts` reads it), `RunTransitionHooks`, and every hook helper. |
| `SimState(Game, TickCount, Elapsed, SplitMode, RecipeMenu, Content)` | `Sim/SimState.cs` | It is **projected** from the session (`GameSession.ToSimState()`) and written back after a state-changing step. It is not stored on the session, so the public shape of `GameSession` (and every test) is unchanged. `Step` does not touch `Elapsed`, `SplitMode` or `RecipeMenu` yet. The VM projection is untouched: `recipeMenu` is serialized and `SplitMode` is not. |
| `SimContent(Recipes, FacilityRecipeMap, Beats)` | `Sim/SimState.cs` | L1 content. `RecipesForFacility(env, knownRecipes)` is the old `GetRecipesForFacility`, with the known-recipe set made an explicit parameter (see the subtlety below). |
| `Intent.Tool(Func<GameState, ToolResult> Apply, bool AdvancesTime)` | `Sim/Intent.cs` | **Transitional.** The 1.2 vehicle for every tool action. It carries a closure, so it is not replayable, and it compares by delegate reference. `AdvancesTime = (TickMode == Rogue)` is the explicit P1-D2 flag. 1.3 deletes it in favour of data-only per-path intents. |
| Shell | `GameSession.RunStep` / `Act` / `ApplyResult` / `Tick` | The shell runs Step, and on a state change it bumps `DebugStepCount`, pushes the pre-step state for undo, and writes back. Action-log lines are built **as before** (P1-D6): `FAILED: {log} — {reason}` for a rejection; otherwise the tool line followed by `✦ {env} crafted: {name}` per `CraftCompleted`; and for a tick, the completion lines or `Tick #N`. Eleven `Execute*` methods now build one `Intent.Tool` around their tool (`g => g.ToolGrab()` and so on). `ExecuteAcquireRandom`, `ExecutePickupToToolbar`, `Execute(dsl)` and `ApplyToolResult` still precompute the result and pass `_ => result`, because their log lines need it. |

**Ordering preserved, with the subtleties written down:**
1. A **tool no-op** is detected by record equality on the *post-tool* state, *before* any tick. In rogue mode, a no-op does not tick. A **tick no-op** is detected *after* facilities, plants and hooks. These are two different checks, and both are kept.
2. `GetRecipesForFacility` read `Current.KnownRecipes`, the **pre-action** state, even while ticking the post-tool state. Inside `Step` the tick phase is passed the pre-intent known set, and the shell resolves completion-log names the same way. A recipe card learned in the same action therefore does not enable a craft until the next step, exactly as before.
3. Event order per state-changing intent: `IntentApplied`, then `TimeAdvanced` and `CraftCompleted*` in `OrderedFacilities` order (rogue and tick only), then `UiTriggerFired*`. The tick path has no `IntentApplied`. All events carry `Step = TickCount + 1` and a step-global `Seq`. `UiTriggerFired` is emitted only when `UiLedger.Fire` actually grew the ledger.
4. A rejected tool emits only `IntentRejected(reason = ToolResult.Error ?? "")`, and the shell neither logs a completion nor pushes undo.
5. `TickCount` is incremented **in `Step`**, because it is L1, rather than in the shell as the plan text says. The effect is the same: +1 per state-changing intent.

**Acceptance:**
- `dotnet test`: Core 901 (892 + 9 new), App 53, all green.
- `make parity-full` is green with goldens byte-identical.
- One test was edited: the 1.0 shape-pin `Intent_Phase1Slice0_DeclaresOnlyTick` became `…Slice2_DeclaresTickAndTransitionalTool`, because adding any case breaks it by design. No behaviour test was touched.
- New tests:
  - `Sim/SimulationStepTests` (8): rejected, no-op, realtime versus rogue time flag, Seq order, `FirstPickup` fires once, a tick no-op, `CraftCompleted`, and completion-log ordering through the session.
  - `Sim/StepOnlyPathJourneyTests` (1): this drives `smoke.journey.json` in-process through `DebugCommandHandler` and asserts, on every step, that ΔTickCount = Δ`GameSession.DebugStepCount` (thread-static) and that a step with no Step call leaves the `BagStore` instance, `KnownRecipes` and `ZonesReached` untouched.

**Still outside Step in 1.2 (all 1.3 scope):**
- cursor moves, tree bumps and cursor-rest triggers;
- dialogue advance, failed-peek and peek-while-carrying beats;
- the split modal (`Begin`/`Adjust`/`Cancel`);
- the recipe menu (`Open`/`Move`/`Close`), plus `SetRecipeOn` and `ApplyRecipeSwitch`, which push undo directly with no tick and no hooks;
- `Undo` (which still carries dialogue forward);
- `WithElapsed`;
- the `Execute(dsl)` parse-failure log and the `ExecuteCycleRecipe` failure logs.

1.3 must keep each one's semantics exactly: no `TickCount` bump and no hooks where there are none today.

**For 1.1 (SimRng):** `ExecuteAcquireRandom` still draws from its `Random` *before* `Step`, outside the intent. Moving the draw inside Step (`SimRng(Stream.Acquire)`) is the natural point to turn it into a real `Intent.AcquireRandom`. When `SimRng` joins `SimState`, add it to `ToSimState` and `WithSimState`. The seedling's `new Random()` sits in `RecipeRegistry`, reached from `Simulation.TickFacilities` via `FacilityLogic.Tick`, so the `SimRng` has to be threaded from the `SimState` there.

## Addendum — Slice 1.1 as built (2026-10-28)

**Golden-neutral.** The VM checkpoint golden and the 11 TUI buffer goldens are byte-identical, and
smoke and target-demo diff clean across tui and mock-godot. Neither journey presses AcquireRandom, runs
DSL, or crafts the seedling (re-verified against both journey files). The only recipe either journey
completes is the compass, whose factory draws nothing.

| Piece | Where | Notes |
|---|---|---|
| `SimRng` | `src/Pockets.Core/Sim/SimRng.cs` | An immutable record of `(WorldSeed, Step, per-stream draw counters)`. A draw is `Hash(WorldSeed, Step, Stream, Draw)`: SplitMix64's finalizer `Mix` folds in the seed, then the step, then `(stream << 32 \| draw)`. It is integer-only. `AtStep(n)` resets the counters when the step changes (and returns the same instance otherwise). Equality is by value. `Mix` is pinned against the SplitMix64 reference output (`0xE220A8397B1DCDAF`). **Golden vectors:** the first 64 draws (seed 20260803, step 1, Acquire) were computed by an independent Python implementation and frozen in `SimRngTests`. |
| `SimStream` | same file | `Acquire = 1`, `Dsl = 2`, `Craft = 3`. The values are hash input, so they must never be renumbered. |
| `SimRandom` (the plan's `AsRandom()` bridge) | same file | `rng.AsRandom(stream)` returns a `System.Random` subclass. It overrides every public draw method, so the base class's runtime-specific generator is never used. After use, the caller writes `random.Rng` (the advanced RNG) back into state. `Next(n)` is `draw % n`. `NextDouble` is `(draw >> 11) · 2⁻⁵³`. |
| `SimState.Rng` | `Sim/SimState.cs` | A new required positional field. `GameSession` stores it as `Rng` (nullable; `WorldRng` falls back to seed 0), projects it in `ToSimState`, and writes it back in `WithSimState`. `GameSession.WithWorldSeed(seed)` sets it. `DemoProfile.WorldSeed` = `seed ?? DemoSeed`. `NewSession()` applies it, and `GameView` takes `worldSeed:` (from `Program.cs` and the TUI driver), so all three drivers share it. It is **not** serialized in the VM. Like `TickCount`, it is carried across undo: the step number keeps rising, so a redo draws fresh values. |
| `Intent.AcquireRandom(bool AdvancesTime)` | `Sim/Intent.cs`, `Simulation.StepOne` | The first data-only player intent. Step draws the type from `Acquire` at the step, runs `ToolAcquireRandom`, and then follows the same rejected/no-op/applied/tick path as `Tool`; the two now share a local `ApplyTool`. `GameSession.ExecuteAcquireRandom()` takes no argument. Its log line (P1-D6, unchanged text) comes from a **preview**: the shell runs the identical draw (`WorldRng.AtStep(TickCount + 1)`, which is pure) on the pre-step state to name the acquired stack. 1.5's events can replace this. |
| Craft stream | `Simulation.AdvanceTime` → `TickFacilities` → `FacilityLogic.Tick(…, Random rng)` → `CompleteCraft` → `recipe.OutputFactory(rng)` | One `Craft` cursor per tick phase, drawn in `OrderedFacilities` order, so two pots finishing on the same step get different worlds. Its advance is committed to `SimState.Rng`. |
| Recipe factory signature | `Models/Recipe.cs` | `Func<RecipeOutput>` → **`Func<Random, RecipeOutput>`**, not `Func<SimRng, …>`. A factory needs a cursor that advances across factory calls in the same tick, and the generators it calls are `Random`-typed. In play, the argument is always a `SimRandom`. Factories that don't draw take `_`. This was the risk-#3 churn: 4 sites in `GameInitializer`, 3 in `RecipeRegistry`, 2 in `ContentLoader`, and ~10 in tests. |
| Generators | `Data/GeneratorFunc.cs`, `PipelineExecutor.Execute(…, Random rng)`, `GeneratorBuiltins` | `GeneratorFunc` gains a third parameter, `Random rng`. In `Wilderness` and `Shuffle`, `rng` is now required (`rng ??= new Random()` is gone). Content-loaded pipeline factories pass their factory RNG through. |
| DSL | `OpResult.Rng` (init property carried through `Chain*`), `DslInterpreter.RunProgram(state, program, Random? rng = null)`, `Opcodes.AcquireRandom` | `acquire-random` draws from `OpResult.Rng`. **With no RNG it fails** (`ChainError`) instead of drawing from an unseeded source. `GameSession.Execute(dsl)` passes the next step's `Dsl` stream. |
| Init-time | `GameInitializer.CreateRandomStage{1,2,3}Game`, `CreateFromRegistry` | `Random random` is now required. The demo's seeded `new Random(seed ?? DemoSeed)` and `new Random(WildernessSeed)` are **kept** (P1-D7). |
| Frontends | `GameView`, `GameSceneController`, `GameController.HandleKey(GameKey)` | `_rng` was removed from both frontends, and `HandleKey` lost its `Random?` parameter. Godot's no-`/data` fallback now seeds `new Random(GameInitializer.DemoSeed)`. Godot is outside `Pockets.sln`; it was checked by grep and not compiled here (risk #5). |

**Acceptance:**
1. `grep -rn "new Random()" src/` returns zero hits. This is enforced by `SimRngDeterminismTests.Src_HasNoUnseededRandom`, which scans `src/**/*.cs` (excluding obj/bin) and also bans the target-typed `Random x = new()` (the form the frontends used) and `Random.Shared`.
2. `SameSeed_AcquireRandomX50_AndSeedlingCraft_ProduceIdenticalWorlds`: a rogue session with a loaded Seedling Pot runs AcquireRandom ×50 (the pot completes at tick 8). The two runs match on a GUID-free world fingerprint, on the action log, and on the RNG. A different seed gives a different world. Further tests cover DSL determinism, DSL-without-RNG failure, Step's draw equalling the pinned hash with the counter committed, the log line naming the type Step drew, and undo moving the RNG forward.
3. The goldens are unchanged; `make parity-full` is green.
- `dotnet test`: Core 917 (901 + 16 new: `SimRngTests` 8, `SimRngDeterminismTests` 8), App 53, all green. The spikes still build.
- Edited tests: the shape pin `…Slice2_DeclaresTickAndTransitionalTool` → `Intent_Phase1Slice1_DeclaresTickToolAndAcquireRandom`. The rest are mechanical signature updates (`() =>` → `_ =>`, an explicit seeded `Random` for `FacilityLogic.Tick` / `PipelineExecutor` / generators, `ExecuteAcquireRandom()`). No behaviour assertion changed.

**Debt recorded (P1-D7, unchanged):**
- The seeded `System.Random` at init time (`GameInitializer` demo seed and wilderness seed, and the Godot fallback) is still in use.
- `WildernessTemplate.FillRatio` and the loot weights, and `LootTableTemplate`'s weights in `GeneratorBuiltins.Wilderness`, are still `double`. Via the seedling, that `double` math now runs **in step** on `SimRandom.NextDouble()`. It is deterministic on IEEE-754 hardware but is a G1 violation, to be converted in Phase 3.

**For 1.3 (intents everywhere):**
- `Intent.AcquireRandom` has no target location yet. `ToolAcquireRandom` acquires into the *active* bag, while the log diff reads the *root* grid. That quirk is unchanged: inside a nested bag the log says "added random item". When adding `LocationId`, keep that text or treat changing it as a decision.
- `Execute(dsl)` still runs the program *before* Step and rides in as a precomputed `Intent.Tool`. Its `Dsl` draws are taken at the next step's coordinates, but their counter is not written back. This is harmless today: each state-changing step gets a fresh step number, so counters reset. A data-only `Intent.Dsl` should move the run, and the draw, inside Step.
- `ExecuteAcquireRandom` previews the draw only to build its log line. If 1.3 turns logging into a post-step projection, the preview can go.
- `HandleKey(GameKey)` no longer takes an RNG. Any new randomness belongs in a `SimStream` (add a member; never renumber) and is drawn inside Step.

## Addendum — Slice 1.3 as built (2026-10-28)

**Golden-neutral.** The VM checkpoint golden and the 11 TUI buffer goldens are byte-identical. The
smoke and target-demo checkpoint streams, on both tui and mock-godot, are byte-identical to the
pre-slice streams (diffed against a baseline captured at `7d6f43c`, not just against each other). No
action-log text changed (P1-D6). No golden was re-recorded.

| Piece | Where | Notes |
|---|---|---|
| `Intent`: 32 data-only cases | `Sim/Intent.cs` | **`Intent.Tool` is deleted.** There is one case per controller path, and every panel verb names its `LocationId`. Clock: `Tick`, `SetElapsed(Elapsed)`. Cursor: `Move(At, Dir)`, `SetCursor(At, Pos)`, `Click(At, Pos, Type)`. Routed verbs: `Primary(At)`, `Peek(At)`, `Leave(At)`. Tools: `ToolPrimary`, `Secondary`, `QuickSplit`, `ModalSplit(At, LeftCount)`, `Sort`, `AcquireRandom(At)`, `Grab`, `Drop`, `Interact`, `EnterBag`, `LeaveBag`, `Harvest`, `CycleRecipe`, `Dsl(Expression)`. Modals: `AdvanceDialogue`, `Begin/Adjust/Commit/CancelSplit`, `Open/Move/Confirm/CloseRecipeMenu`. Shell: `Undo`. A test pins that no case has a delegate- or `Guid`-typed property, so an intent log replays in a fresh session whose bag ids differ. The plan's `Intent.Back` is `Leave(At)`: the Q key and the back button were the same verb with different status text, and the status text stays in the controller. |
| `AdvancesTime` left the intents | `SimContent.TickMode` | The time flag (P1-D2) is now read from L1 content (the inventory already classed `TickMode` as content), so an intent means the same thing in any replay of the same profile. `SimContent` gained a 4th positional field. |
| Dispatcher | `Sim/Simulation.Intents.cs` (`Simulation` is now `partial`) | `StepOne` switches on every case. The branching that lived in the controller (`ExecuteFocusedPrimary`, `ExecutePeek`, the Q/back close-or-leave) and in the session (`RunAt`, pickup-to-toolbar, cursor and tree-bump and cursor-rest, the failed-peek and peek-while-carrying beats, dialogue advance, split modal, recipe menu, `SetRecipeOn`, `ApplyRecipeSwitch`) was **moved** into it, with `_focus` replaced by the intent's `At`. The `RunAt` swap is now `SwapIn`/`SwapOut` on `GameState`. A tool at a panel still runs its whole step (tool, rogue tick, hooks) in the swapped frame, exactly as before. |
| Non-timed intents | `Simulation.Changed` | Cursor, dialogue, split, recipe-menu and `SetElapsed` intents change their L1 field and emit `IntentApplied`. They do **not** tick, run hooks or bump `TickCount`, which keeps 1.2's list of bypass semantics intact. `ConfirmRecipeMenu` (SetRecipe) and `CycleRecipe` (recipe switch) also don't tick, but the shell pushes undo for them, as before. |
| Composites | `Simulation.Expand` | `Click` = `SetCursor`, then `Primary`/`Secondary`. `CommitSplit` = `ModalSplit`, then `CancelSplit`. The shell runs each sub-intent as its own step, so the undo snapshot of a click is the state after the cursor moved, as before. The intent log records the composite once. |
| `StepTrace` / `Route` (internal) | `Simulation.Intents.cs` | `Simulation.StepTraced` reports how each intent resolved: the route (e.g. `OpenContainer`, `PeekRefused`, `Bumped`), the tool intent it ran, and a detail (bag environment, pickup count, DSL error suffix, dismissed beat). The shell writes the old log lines from it plus the pre-step state. The controller moves focus, picks its status message and sets the `FeedbackPulse` from it. It is internal plumbing, not contract: 1.5's semantic events replace most of it, and 1.6 replaces the pulse with `Bumped`. |
| Shell | `GameSession.Apply(Intent)` / `ApplyTraced` / `RunStep` | `Apply` is the one public mutation entry. It handles `Undo` in the shell, which owns the snapshots, so undo never reaches Step. It expands composites, runs one `Simulation.Step` per intent, writes back, pushes undo per route, and builds the log lines with the **same** formatting as the old lambdas. The `Execute*`, modal, menu, cursor, `Undo`, `Tick` and `WithElapsed` methods are now `internal` one-line wrappers that build an intent. `InternalsVisibleTo` is set for the two test projects only. `ApplyToolResult`, `ExecutePickupToToolbar`, `FireFailedPeek`, `FirePeekWhileCarrying` and `TryMoveCursorAt` are gone; their logic is in the dispatcher. |
| Intent log | `GameSession.IntentLog : ImmutableList<(int Step, Intent)>` | An init-only property (the session's positional shape is unchanged). It is append-only and records each intent that changed the session, at `TickCount + 1`. **No-op intents are left out**: they change no L1 state and no shell output, so replay can't tell. This keeps `GameController.Tick()`'s `session != before` change check exact, and keeps idle realtime polling out of the log. There is no undo truncation yet (1.8). |
| DSL | `Intent.Dsl` | The program runs **inside** Step. `Dsl`-stream draws are taken at the step and the counter is committed with the state (1.1's open item). A parse failure is `IntentRejected(ex.Message)`, logged as `FAILED: {expr} — {msg}` (unchanged text). Op errors still apply the state, and the log suffix rides in the trace, so the program runs once. |
| AcquireRandom | `Intent.AcquireRandom(At)` | It is run at `At` via the swap (the controller always sends `B`). **Quirk kept:** the log line still previews the draw and diffs the *root* grid, so inside a nested bag it still says "added random item". The preview stays until logs come from events. |
| Controller | `GameController` | `HandleKey`, `HandleGridClick`, `HandleBackClick`, `HandleDsl`, `SyncClock` (`SetElapsed` + k × `Tick`, TickLengthMs = 1000, P1-D4) and `Tick` only build intents, via `Send`. Focus cycling is the only key with no intent: focus stays controller-only (P1-D5). `DebugCommandHandler` (Godot, mock-godot, the TUI driver) goes through the controller, so the journey's `keys`, `click`, `back`, `tick` and `advanceTime` steps are intent streams on every driver. **No Godot file changed** (it calls only `GameController` and public session getters), so no Windows build check is needed for this slice. |

**Acceptance:**
1. Goldens identical; `make parity-full` is green.
2. **Replay:** `Sim/IntentReplayTests` (a Theory over smoke and target-demo) drives each journey through `DebugCommandHandler`. It then replays the recorded intent log on an independently loaded profile and asserts the canonical VM after every journey step, including the initial one, plus a pairwise-identical replayed intent log.
3. **Architecture:** `Sim/IntentArchitectureTests` has two parts. A text scan of `src/Pockets.App`, `src/Pockets.Godot` and `tools/` finds no `Execute*`, modal, menu, cursor or `WithElapsed` session calls. A reflection check finds no public `Execute*`, `Undo`, `Tick` or similar on `GameSession`, and requires a public `Apply(Intent)`.
- `dotnet test`: Core 929 (922 + 7 new in `Sim/IntentStepTests`), App 53, spikes 109. All green.
- Edited tests:
  - The shape pin became `Intent_Phase1Slice3_DeclaresOneDataCasePerControllerPath`, and a no-closure/no-Guid pin was added.
  - `SimulationStepTests`: the `Intent.Tool` closures became data intents, and the time flag moved from the intent to content.
  - `SimRngDeterminismTests`: `AcquireRandom(LocationId.B)`, realtime content.
  - `PeekTests` and `UiLedgerTests`: `FireFailedPeek()` and `ApplyToolResult(...)` became `Apply(new Intent.Peek(B))`.
  - `StepOnlyPathJourneyTests` was **tightened**. ΔTickCount ≤ ΔStepCount, because non-timed intents step without a tick. A journey step with no Step result must leave the same `GameState` instance, modal state and clock, with no exemptions now. Every state-changing step must grow the intent log.
  - No behaviour assertion changed.

**Found while moving code, kept as is (behaviour-neutral, flagged for the PM):** a tool run at a non-B panel
pushes the **swapped** frame (B = that panel's location) onto the undo stack. This is because `RunAt`
wrapped the whole `Act`. Undoing such an action would restore a state whose B location is the panel's.
Neither journey undoes, and no test covers it. The shell reproduces it exactly
(`Simulation.SwapIn(Current, at)` as the snapshot). Fixing it is a one-line behaviour change and a
decision for later.

**For 1.4 (rules / TimeClass / IBagView / SetItem ban):**
- The tick phase is unchanged and still reached from two places: `ApplyToolResult` (rogue) and `ApplyTick`.
  Both call `AdvanceTime` → `TickFacilities` + `PlantLogic.TickPlants`, which is the seam for the rule
  registry. The order is still `OrderedFacilities` order, then plants.
- The ban scan should cover `Sim/**`. The new dispatcher has two loop-free single-cell `SetCell`s
  (pickup-to-toolbar's source clear), and `RegisterKnownRecipes` already uses a builder. The recipe switch
  writes through `ReplaceBagById`/`Store.Set`. These are player-tool single writes, not rule-phase loops.
  Decide whether the scan exempts `Simulation.Intents.cs` or 1.4 routes the pickup through `IBagWriter`.
- `SimContent` is now 4 fields (with `TickMode`). A `BagClock` per bag can sit beside it without touching intents.
- `Route`/`StepTrace` is internal and small. Don't build on it; 1.5's events should replace it, after
  which the shell's log lines (P1-D6) and the controller's focus can read events instead.

## Addendum — Slice 1.4 as built (2026-10-28)

**Golden-neutral.** The VM checkpoint golden and the 11 TUI buffer goldens are byte-identical. All 15
parity artifacts (the 11 buffers plus the smoke and target-demo checkpoint streams on tui and mock-godot)
are byte-identical to a baseline captured at `f871e5a`. No action-log text changed and no golden was
re-recorded.

| Piece | Where | Notes |
|---|---|---|
| `IBagView` / `IBagWriter` | `Sim/IBagView.cs` | View: `Columns`, `Rows`, `Count`, `Cell(i)`, `Neighbourhood(i)`. The neighbourhood is the in-bounds **Moore** set (up to 8, no wrap), because field rules gather from Moore (#23 §Rule kinds); nothing calls it yet. Writer: `Set(i, cell)`, `Commit() → Grid`. A writer is single-use, and `Commit` with no writes returns the **source grid instance**, so an untouched bag stays reference-equal (the no-op checks depend on it). |
| `ImmutableBagWriter` | `Sim/ImmutableBagWriter.cs` | The a1 style, re-implemented under TDD. The first `Set` copies the cells into one `Cell[]`; `Commit` wraps it with `ImmutableCollectionsMarshal.AsImmutableArray`, so there is no second copy. It is a **struct**, so the writer itself allocates nothing. The plan's `ImmutableArray<Cell>.Builder` was dropped because the builder object made a single-write commit (the owner-progress write) cost more than the old `SetCell`, which would break acceptance 4. Use it as a local, never copy it; boxing to `IBagView` for reads is fine. |
| `ITickRule : IRule`, `RuleContext`, `RuleRegistry.TickPhase` | `Sim/Rules/RuleRegistry.cs` | `ITickRule.Apply(GameState, RuleContext)` is one coarse whole-state pass per step. Phase 3's `IFieldRule`/`ITransferRule` replace it. `RuleContext(Step, KnownRecipes /*pre-step*/, Content, CraftRandom, Emit)`: `Emit` queues a `seq → SimEvent` factory, so `Seq` stays step-global. The registry is `[FacilityCraftRule, PlantGrowthRule]`. |
| `FacilityCraftRule` | `Sim/Rules/FacilityCraftRule.cs` | Transfer / `EventSkippable`, `EventKinds = [CraftCompleted]`. **Moved verbatim** from `Simulation.TickFacilities` (now deleted). It runs in the same `OrderedFacilities` order, keeps the list snapshot, the `ticked == facility` skip and the one Craft cursor per tick phase, and applies each facility to the state in turn. The owner `Progress` write (the old `ReplaceBagById` owner transform) goes through `ImmutableBagWriter`, using one owner lookup instead of two. The seedling's wilderness draw is part of this rule. |
| `PlantGrowthRule` | `Sim/Rules/PlantGrowthRule.cs` | Field (own cell) / `ClosedForm`, no events. It delegates to `PlantLogic.TickPlants`, whose public signature is unchanged. |
| `AdvanceTime` | `Sim/Simulation.cs` | It emits `TimeAdvanced`, builds one `RuleContext`, runs `RuleRegistry.TickPhase` in order, then commits the Craft cursor. The order is unchanged (facilities, then plants), and both callers (rogue tool, tick) still reach it. |
| The ban, ported | `FacilityLogic.CompleteCraft`, `PlantLogic.TickPlants`, `Simulation.Intents.PickupToToolbar` | Consume and output now share **one writer and one commit per craft** (it was 3 arrays). Plants use one writer per bag, committed only if a plant grew. The pickup-to-toolbar source clear is **routed** through `IBagWriter` rather than exempted (the handoff's preference); the result is identical. `FacilityLogic.Tick`'s signature is unchanged. |
| `BagClock` | `Sim/BagClock.cs`, `Bag.Clock` | `BagClock(Rate, Mode)` with `BagClockMode { Observed = 1, Background, Dormant, Frozen }` (the #23 §Time modes; no zero member). It is a per-bag **init property on `Bag`**, defaulting to the shared `BagClock.Observed` (1, Observed). Putting it on `Bag` rather than in `SimContent` makes it travel with the bag through grab/drop/sort, like `EnterOnly`. Nothing reads it, and the VM does not project it. |

**Acceptance:**
1. Goldens are identical; `make parity-full` is green (and baseline-diffed, see above).
2. The facility and plant unit tests pass **unmodified**: `FacilityLogicTests`, `FacilityProgressResolutionTests` and `PlantLogicTests` are untouched. No existing test was edited in this slice.
3. Architecture tests, in `Sim/RuleArchitectureTests` (9):
   - **The ban scan.** It covers `Models/*Logic.cs`, `Sim/**` and `Rules/**` (the last does not exist; the rules live in `Sim/Rules/`), looks for `.SetCell(` / `.SetItem(` on non-comment lines, and fails on any hit. It has **one exemption, with its reason:** `Sim/SimRng.cs` `_draws.SetItem(`, the RNG's per-stream counter dictionary, which is not a grid.
   - **The registry checks.** The registry holds every concrete `IRule` type exactly once, in facilities-then-plants order. Each declares a defined `Kind`/`Time` on its own type. `BoundedCatchUp` requires `MaxDelta > 0`, and every other class requires 0. `EventKinds` are sealed `SimEvent` cases. Each rule has its planned kind and class. `Bag.Clock` defaults to Observed/1.
4. **Allocations**, in `Sim/FacilityCraftRuleAllocationTests` (3). "Today" is kept **inside the test** as a verbatim copy of the pre-1.4 `TickFacilities` + `FacilityLogic.Tick`/`CompleteCraft`, so both are measured in the same process: 50 warm-up passes, then 500 measured with `GC.GetAllocatedBytesForCurrentThread`. This avoids a machine-specific byte constant. The test also asserts the rule's result equals the legacy result. On the Workbench, owned in an 8×4 root: start 3976 vs 4160 B/pass, countdown 3736 vs 3920, completion 4776 vs 5016 (rule vs legacy). The plan said "the Workshop"; the facility actually measured is the Workbench, the loaded-recipe facility the step tests already use.
- `ImmutableBagWriterTests` (10).
- `dotnet test`: Core 951 (929 + 22 new), App 53, spikes 109. All green.
- **Godot:** no Godot file changed, so no Windows build check is needed.

**Known, kept (Phase 7 hot-set material):**
- A parent bag that owns N facilities is still materialized N times per step: one owner-progress commit per facility, applied in sequence. That matches the old `ReplaceBagById` semantics exactly. Batching it into one writer per bag per step would change behaviour in the nested-facility edge cases (a facility inside another facility's slots, where the old code lets the later whole-bag replace win). It is left for the hot set.
- A bag touched by both rules (facility owners and planters in the same root) commits once per rule.

**For 1.5 (the SimEvent stream):**
- Rules emit through `RuleContext.Emit(seq => …)`. That is the hook for new rule-phase events, and the `EventKinds` on each rule must list them, because the architecture test checks that they are `SimEvent` cases. `FacilityCraftRule` is the natural home for any craft-start or progress event. `PlantGrowthRule` emits nothing today; a `Grown` event would be its first, so add it to `EventKinds`.
- `StepTrace`/`Route` is untouched. The rule phase does not feed it.
- `TimeAdvanced` is still emitted by `AdvanceTime` before any rule, not by a rule.

## Addendum — Slice 1.5 as built (2026-10-28)

**Golden-neutral.** The VM checkpoint golden and the 11 TUI buffer goldens are byte-identical. All 15 parity
artifacts (the 11 buffers plus the smoke and target-demo checkpoint streams on tui and mock-godot) are
byte-identical to a baseline captured at `02fb1bb`. No action-log text changed (P1-D6) and no golden was
re-recorded.

| Piece | Where | Notes |
|---|---|---|
| The taxonomy | `Sim/SimEvent.cs` | 28 cases (with the reserved belt one). **Envelope:** `IntentApplied(intent)` (kept: it opens every applied intent's events and names the cause; a modal, menu or clock intent changes no cell, so its envelope is its whole story), `IntentRejected`, `TimeAdvanced`. **Cursor/navigation:** `CursorMoved(loc, bag, from, to)`, `Bumped(loc, bag, at, dir?, reason)` (`BumpReason.Unnavigable` / `FailedPeek`), `BagEntered/BagLeft(loc, bag, depth)`, `PanelOpened/PanelClosed(loc, bag)`. **Stacks:** `StackMoved(item, count, from, to, cause)`, `StackSplit(item, from, to, left, moved, cause)`, `StackMerged`, `StackRemoved`, `Acquired(item, count, cell, source)`, `BagRearranged(bag, permutation)`. **Facilities:** `CraftStarted`, `CraftProgressed(facility, recipe, remaining, owner)`, `CraftAborted`, `CraftCompleted(facility, recipe, outputs, consumed, owner)`, `RecipeSelected(facility, recipe, owner, slotsRebuilt)`. **Plants:** `PlantGrew(cell, stage, of)`, `Harvested(cell, yields)`. **Chrome/dialogue/recipes:** `UiTriggerFired`, `DialogueQueued(beat)`, `DialogueAdvanced(beat, dismissed)`, `RecipeLearned(id, card)`, `ZoneReached(bag)`. Reserved: `BeltAdvanced` (unchanged, P1-D3). Helpers: `CellRef(Bag, Index)` (record struct), `Placed(item, count, cell)`, `StackCause` (Grab, Drop, Swap, Split, Pickup, Harvest, Slot, Acquire, Dsl, RecipeSwitch). The four array-carrying cases override equality so arrays compare element-wise. Not in the plan's table, added because coverage needs them: `CraftAborted`, `RecipeSelected`, `StackRemoved`, `DialogueQueued`, `ZoneReached`. |
| Tool events: derived, not rewritten | `Sim/EventDeriver.cs` | The tools (`GameState.ToolPrimary` …) still return only a state. Rewriting ~15 tool methods to emit would have risked the golden-neutral rule, so `Step` **derives** each tool's events from the state it handed the tool and the state it got back, *knowing the intent*, which supplies cause and direction. `Locations` diffs the location map (with the `RunAt` frame relabelled: in a tool run at C, slot B means C). `Stacks` diffs cells of bags present before and after, matches departures to arrivals by item kind (type + contained bag) into moves/splits/merges, calls both-ways a swap, into the hand a grab or split, out of the hand a drop or a slot, a Primary between non-hand bags a pickup; unmatched arrivals are `Acquired`, departures `StackRemoved`; a sort is one `BagRearranged`; a harvest (nested item → parent, or a grown plant's produce) is one `Harvested`. A property- or frame-only change (same item, same count) is never a stack event: the explicit event that caused it names the cell. |
| Determinism | `EventDeriver.CanonicalOrder` | Store iteration is GUID-hash order, so changed bags are sorted by their place in the world: root rank (hand, toolbar, other roots by environment and size), then the cell-index path from that root. Cells go by index. The owner index is built only when two or more bags changed. `BagStore.Entries` (new) exposes the store keys: the drop tool stores a fresh hand bag (new `Bag.Id`) under the old key, so diffs go by key. |
| Rule events | `FacilityCraftRule`, `PlantGrowthRule` | Facility: `CraftStarted` / `CraftProgressed` / `CraftAborted` / `CraftCompleted`, each naming the owner cell (progress lives there); completion vs abort is told apart by whether the grid changed. Payloads are built **lazily inside the `Emit` factory** (bags are immutable), and the captured state is allocated only on the branch taken, so the 1.4 allocation bound still holds: start 4056 vs 4160, countdown 3816 vs 3920, completion 4680 vs 4944 B/pass (rule vs legacy). Plants: one `PlantGrew` per grown plant, canonical bag order. Both `EventKinds` updated. |
| Hooks and dispatcher | `Simulation.cs`, `Simulation.Intents.cs` | Hooks emit `UiTriggerFired`, `RecipeLearned` (with the card's cell), `ZoneReached`; beats that fired are diffed into `DialogueQueued` at the end of the step. Cursor-rest triggers (`FirstCursorRest`, `NoticeClock`) now report `UiTriggerFired` too. The dispatcher emits `Bumped`, `DialogueAdvanced` and `RecipeSelected` explicitly. **Order per applied intent:** envelope → location events → stack events → explicit events → (rogue) `TimeAdvanced` and rule events → hook events → `DialogueQueued`. |
| Bumps with no state change | `Simulation.Intents.cs` | A refused move or peek emits `Bumped` **even when nothing else changed** (a beat is showing, so the bump counter is inert; the beat has already fired). This keeps the cue firing on every bump, as `FeedbackPulse` did. It is the one case of events with an unchanged state, so the shell now detects a change by `result.State != pre` rather than by "any event". |
| `StepTrace` replaced where it could be | `GameController`, `GameSession` | The controller's focus moves from `PanelOpened` (C/W takes focus) / `PanelClosed` (→ B), and its `FeedbackPulse` is raised from `Bumped` (`Unnavigable` → Bump, `FailedPeek` → FailedPeek); `ApplyTraced` returns the events of every sub-step. The AcquireRandom log line is named from the `Acquired` event in the frame's root bag (the 1.1 preview, which re-ran the draw, is gone; the "added random item" quirk inside nested bags is unchanged). What remains in `StepTrace` is log and status wording only: routes for the log line text (P1-D6) and the controller's status messages. |
| Canonical text | `Sim/EventText.cs` | `step seq Kind name=value …`, fields in primary-constructor order, invariant culture; bags renamed `bag#1, bag#2…` by first appearance in the stream (P1-D8); cells `bag#1[4]`, positions `(r,c)`, items quoted, placed stacks `3×"Rock"@bag#1[4]`, intents `Grab(B)`. One instance per stream. This is the format 1.7's golden can use as is. |

**Acceptance:**
1. Goldens identical; `make parity-full` green and baseline-diffed (above).
2. **Coverage check** (`Sim/EventCoverage.cs`, test-side): every cell (hand and toolbar slots are cells of their bags) and every location that differs between a step's pre- and post-state must be named by an event; what each event names is spelled out case by case. `Sim/EventJourneyTests` replays both journeys' intent logs one `Simulation.Step` per sub-intent and runs the check on **every step**: green on smoke and target-demo. A mutation test proves it bites (dropping the grab's `StackMoved` reports both cells; dropping `CursorMoved` reports the location).
3. **One trace per mechanic** (`Sim/EventTraceTests`, 18 tests, spike A style: board + intents → event lines, each step also coverage-checked): move / bump / failed peek; grab then drop; merge and swap; quick and modal split; pickup to toolbar; sort with its permutation; enter / leave / look-in / close; facility start → countdown → complete; facility abort; recipe-menu confirm; plant grows then is harvested; AcquireRandom; first pickup of a recipe card (chrome + learn); a bump-queued beat advanced then dismissed; rejection; rogue tool-then-tick in one step; the `BeltAdvanced` line shape.
- Also: the journeys' canonical event text is **identical across two independent sessions** (fresh GUIDs each) and contains no GUID: target-demo emits 303 events of 21 kinds, smoke 148.
- `dotnet test`: Core 973 (951 + 22 new), App 53, spikes 109. All green.
- Edited tests (shape pins, by design): `SimulationStepTests.Tool_Realtime_…` and `Tool_Rogue_…` now expect the grab's `StackMoved` (and the rogue `CraftStarted`); `RuleArchitectureTests.Rules_DeclareThePlannedKindAndTimeClass` pins the new `EventKinds`; `SimContractTests` builds `CraftCompleted` with its new fields. No behaviour assertion changed. The App bump and failed-peek tests pass unmodified on the event-driven pulse.
- **Godot:** no Godot file changed. `GameSceneController` still reads `ConsumeFeedbackPulse()`, which the shared controller now raises from events, so the TUI, Godot and mock-godot get the event-driven cue and focus in the same slice (principle 4). No Windows build check is needed.

**Found while building, kept as is (behaviour-neutral, flagged for the PM):**
- **An aborted craft logs "✦ … crafted: …".** Before 1.5 the facility rule reported any "was crafting, now idle" transition as `CraftCompleted`, aborts included, and the shell wrote a completion line for each. The events now tell the two apart (`CraftAborted`), but the shell still writes the line for both so log text is unchanged (P1-D6). Neither journey aborts a craft. Fixing it is a one-line log change and a decision.
- `ItemStack` equality compares its property dictionary by reference, so a tool that rebuilds a stack with equal properties shows up as a changed cell. The deriver treats a same-item, same-count change as "not a stack event", so nothing spurious is emitted, and the coverage check still demands a name for it.

**For 1.6 (frontends consume events at n = 1):**
- The controller already holds the step's events (`_lastEvents`, set by `Send`). Putting them on `ControllerResult` is a one-field change; the frontends can then switch from `ConsumeFeedbackPulse()` to `Bumped` and `FeedbackPulse` can be deleted. Bumped carries the location, bag and cell, so a frontend can aim the shake.
- `DebugCommandHandler` should return `EventText` lines (one `EventText` per controller/run, so bag names stay stable across responses), not raw records.
- Craft completion cues: `CraftCompleted` carries outputs and consumed cells; dialogue cues: `DialogueQueued` / `DialogueAdvanced`.

**For 1.7 (event golden + replay gate):**
- `EventText` is the line format; keep one instance for a whole journey run. `EventJourneyTests.Replay` (one `Simulation.Step` per expanded sub-intent from the profile's `SimState`) is the replay loop the gate needs; the runner can do the same from the session's `IntentLog`.
- Steps are numbered `TickCount + 1`, so non-timed intents (cursor, dialogue, panels in realtime) share a step number with the next timed one, and each restarts `Seq` at 0. `(Step, Seq)` is therefore **not unique across intents** within a step. The golden should either add the intent index or note this; changing the numbering would be a contract change.
- No `IntentApplied`-only lines need suppressing, but cursor moves dominate (65 of 303 target-demo events are `CursorMoved`, 130 are envelopes).
