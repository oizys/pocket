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

### Slice 1.1 — Determinism groundwork: `SimRng` in state (G2) *(parallel with 1.2)*
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

### Slice 1.3 — Intents everywhere: route every controller path through `Step`
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

### Slice 1.4 — Rule registry, `TimeClass` everywhere, `IBagView`/`IBagWriter`, SetItem ban
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

### Slice 1.5 — The SimEvent stream for existing mechanics (+ `BeltAdvanced` shape)
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

