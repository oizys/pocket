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
make parity                     # fast gate: smoke journey, tui vs mock-godot, events + replay check
make parity-full                # full gate (pre-push): all three journeys + VM, buffer, events and poses goldens + fine fuzz
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

## Simulation architecture (two-layer Phase 1)

Design: `design/two-layer.md` (#23). As-built details: `design/two-layer-phase1-plan.md` (addenda 1.0–1.9). Code: `src/Pockets.Core/Sim/`.

```
 input (TUI key / Godot / debug WS / journey)
   ▼
 GameController ── builds Intent[] ──▶ GameSession.Apply(Intent)        shell: undo stack, action log,
                                          │ (Undo handled here)            IntentLog, EventLog
                                          ▼
                              Simulation.Step(SimState, intents)  →  StepResult<SimState>(State, Events)
                                1 dispatch the intent (tools, cursor, modals; tool events derived)
                                2 rogue tool / Tick: TimeAdvanced + RuleRegistry.TickPhase
                                3 transition hooks (UiLedger, known recipes, zones, dialogue)
   ◀── ControllerResult.Events / DebugCommandHandler "events" (EventText lines) ──┘
```

- **`Simulation.Step` is the only state transition.** It is pure, and the class is called `Simulation` (not `Sim`, which the namespace would shadow).
  - `SimState` = `GameState` + `TickCount` + `Elapsed` + `SplitMode` + `RecipeMenu` + `Rng`.
  - `SimContent` (recipes, facility map, beats, `TickMode`) is read-only L1 content.
  - Rogue: a tool step also advances time. Realtime: player intents apply immediately with no time advance, and the clock sends `SetElapsed` + `Tick` (1 Hz, `TickLengthMs = 1000`).
- **Intents** (`Sim/Intent.cs`): 32 data-only cases (36 since Phase 2, which added four shell-handled ones; see below), one per controller path, each naming its target `LocationId`. No delegates and no GUIDs, so a log replays in a fresh session.
  - `GameSession.Apply(Intent)` is the **only public mutation**. The `Execute*`/modal/cursor/`Undo`/`Tick` methods are `internal`.
  - Composites (`Click`, `CommitSplit`) expand into sub-steps.
  - Panel focus is controller-only and never reaches replay.
- **Events** (`Sim/SimEvent.cs`): 28 cases emitted by Step (`BeltAdvanced` is reserved) plus `Rewound`, which only the shell emits on undo. Every event carries `Step` + `Seq`, and `Seq` restarts per Step call.
  - Tool events are **derived** by `EventDeriver` from the pre/post state plus the intent. Rules emit through `RuleContext.Emit`.
  - Every changed cell, slot or cursor must be named by an event (the coverage check).
  - `EventText` is the canonical line: `step seq Kind field=value…`, invariant culture, bags renamed `bag#N` by first appearance. Keep **one `EventText` per run or controller** so names stay stable.
  - Frontends read `ControllerResult.Events` at n = 1. The `Bumped` event drives the bump and failed-peek cues (`FeedbackPulse` is gone).
- **Rules** (`Sim/Rules/`): `RuleRegistry.TickPhase` = `[FacilityCraftRule (Transfer, EventSkippable), PlantGrowthRule (Field, ClosedForm)]`, in that order.
  - Every `IRule` declares `Kind`, `Time` (`TimeClass`: `ClosedForm`/`EventSkippable`/`BoundedCatchUp`/`MustSimulate`, no zero member) and `EventKinds`.
  - `BoundedCatchUp` needs `MaxDelta > 0`.
  - A new rule must be added to the registry and its `EventKinds`.
  - `Bag.Clock` (`BagClock`, always Observed/1) is a Phase 4 hook that nothing reads yet.
- **Grid writes in step paths** go through `IBagView`/`IBagWriter`. `ImmutableBagWriter` is a struct used as a local: the first `Set` copies once, `Commit()` returns the grid, and with no writes it returns the source instance (no-op checks rely on reference equality).
- **Randomness:** `SimRng` is a counter RNG, `Hash(WorldSeed, Step, Stream, Draw)`, held in `SimState`. `SimStream` is `Acquire = 1`, `Dsl = 2`, `Craft = 3`; add new members, never renumber them.
  - `rng.AsRandom(stream)` bridges to `Random`-typed generator code. Write `random.Rng` back afterwards.
  - Init-time seeded `Random` (demo and wilderness seeds) is accepted debt (P1-D7).
- **Logs and undo:**
  - The action-log strings are still built by the shell as before (they are in the VM golden).
  - `IntentLog` is append-only. It records every intent that changed the session or emitted an event, **including `Undo`**. It is the full replay history (ratified: undo does not truncate it), and it has no retention yet (Phase 7).
  - `EventLog` is chunked and persistent. Undo truncates it to the snapshot mark and emits one `Rewound(toStep)`.
  - Event-log retention: events are kept back to the oldest snapshot. The snapshots are the undo stack in rogue mode, and in realtime a checkpoint every 8 steps in a ring of 1000.
  - An 8 MB cap drops the oldest chunks and records a `LogTruncated` marker on the log (not a `SimEvent`).
- **Bans, enforced by architecture tests** (`tests/Pockets.Core.Tests/Sim/`):
  - No `new Random()`, `Random x = new()` or `Random.Shared` in `src/`: `SimRngDeterminismTests.Src_HasNoUnseededRandom`.
  - No per-change `.SetCell(`/`.SetItem(` in `Models/*Logic.cs`, `Sim/**` or `Rules/**`. Use `IBagWriter`. Enforced by `RuleArchitectureTests.StepPaths_HaveNoPerChangeGridWrites` (sole exemption: `SimRng`'s counter dictionary).
  - No direct session mutation from frontends or tools: `IntentArchitectureTests.Frontends_DoNotCallSessionMutatorsDirectly` and `GameSession_PublicSurface_HasApplyAndNoDirectMutators`.
  - Every rule declares its kind and time class: `RuleArchitectureTests.EveryRule_DeclaresADefinedKindAndTimeClass`, `EveryRule_DeclaresTimeOnItsOwnType`, `BoundedCatchUpRules_HavePositiveMaxDelta_OthersZero`, `EveryRule_EventKinds_AreSimEventCases` and `Registry_HoldsEveryRuleType_ExactlyOnce`.
  - Intents stay data: `SimContractTests.Intent_Cases_CarryNoClosuresOrBagIds`.
  - Step is the only path: `StepOnlyPathJourneyTests.SmokeJourney_OnlyStepAdvancesTickCountAndChangesWorld`.
  - Replay and coverage: `IntentReplayTests`, `ReplayCheckTests`, `EventJourneyTests`. In parity, `--replay-check` and the golden `journeys/goldens/target-demo.events` enforce them.
- **Golden-neutral by default:** a refactor must not need `make record-goldens`. If it does, that is a behaviour change and a decision for Aaron.

## Realization and fine state (Phase 2)

Design: `design/two-layer.md` §L2 Realization and §Movement ("As built"). As-built details: `design/two-layer-phase2-plan.md` (addenda 2.0–2.8). Drift and open items: `design/two-layer-phase2-drift.md`. Code: `src/Pockets.Realization/` and `src/Pockets.Core/Fine/`.

```
 GameSession.Apply(Intent)
   ├─ Undo, SetFineOffset, Steer, SetMovement, FineTick ── handled in the shell (Step throws on them)
   │                                  FineTick ─▶ Integrator.Advance ─▶ 0..2 × Move ─▶ RunStep (normal Step, not logged)
   ▼
 Simulation.Step ─▶ events ─▶ FineLogic.Reconcile(Fine, events, game')        L2a: GameSession.Fine (never in L1 or the VM)
   ▼
 Pockets.Realization (pure, int):  Scene.FromState(state, shown, FineView.Of(session.Fine))
                                   Timeline.Commit(events, scene, policy, atMs, fineTick) → Sample(ms) → Pose[]
   ├─ TUI: n = 1 (renders nothing new)   ├─ Godot: PosePlayer per frame   └─ PoseDebugCommandHandler "poses" → golden
```

- **`Pockets.Realization`** references `Pockets.Core` only. It is integer-only: no `float`/`double`/`decimal` and no `Math.` (use ternaries or an int helper; `Math.Clamp` is banned too).
  - `Pose(EntityKey Entity, Guid Bag, CellCol, CellRow, SubX, SubY, int? Height, Facing? Facing)`, positions in 1/16 cell, centre-relative. `EntityKey` is `Body(loc)`, `Stack(cell)` or `Cursor(loc)`; a stack in flight keeps its **departure** key until its track ends (P2-D2).
  - `RealizationPolicy`: int-ms durations (walk 125, bump 120, shake 150, slide 125, sort 200, pop 160, cursor glide 60, reaction overlap 50%). `Ease`: pinned fixed-point smoothstep table. Tuning a value is a pose-golden re-record, never an L1 change.
  - **The body** is the cursor of a location whose active bag is a world bag: `IsWildernessType(env) || EnterOnly` (`Scene.IsWorldBag`, mirrored by `Integrator.IsWorldBag`).
  - `Scene.FromState` gives rest poses (B, H, T shown). `Timeline` maps events to tracks (P2-D3): body walk/lunge, failed-peek shake, same-bag slides, sort permutation, harvest/acquire pop; cross-bag moves snap (`Channel.Fade`); `Rewound` resets to rest (A3). `Choreography` lays a turn out as player → reactions (at 50%) → ambient, by event kind and position.
  - **Snap rules (D7, P2-D13):** an input commit drops every in-flight track. A clock commit (`Choreography.IsClock`: Tick/SetElapsed/TimeAdvanced, or a driven body's derived Moves and bumps) keeps live tracks, rebased by `atMs`. `Commit(…, fineTick: true)` never snaps. An **empty** commit counts as input and snaps. `atMs` is the clock delta (`advanceTime` ms, `TickLengthMs` for `tick`, else 0), never wall-clock.
  - Frontends: the TUI stays n = 1. Godot draws from its own `PosePlayer` (`FrameClock`, `PoseProjection`). mock-godot and live Godot's debug server go through `PoseDebugCommandHandler`, which wraps Core's `DebugCommandHandler` (Core can't hold a `Timeline`). One `EventText`/`BagNames` per run names both streams.
- **L2a fine state** (`Pockets.Core.Fine`): `GameSession.Fine` is a `FineState` (bodies `(Px, Py, Vx, Vy, Held)` per `(loc, bag)`, stack offsets per cell, |offset| ≤ 6, 1/16ths, sparse, content equality). It is **not** in `GameState`, `SimState` or the VM.
  - Undo restores it from `FineMarks` (parallel to the undo stack); realtime checkpoints carry it in `FineCheckpoints`; replay rebuilds it from the intent log.
  - `FineLogic.Reconcile` runs after every Step: an L1 move/split/merge/removal/harvest/sort clears offsets; a non-integrator `CursorMoved`/`BagEntered`/`BagLeft` re-anchors the body.
  - **Shell intents** (handled in `GameSession` like `Undo`; `Simulation.Step` throws on them; each logged only when it changes something; no undo push):
    - `SetFineOffset(At, Index, Dx, Dy)` — test/debug (`setOffset`);
    - `Steer(At, Direction8?)` — a held direction, null releases (`steer`);
    - `SetMovement(At, MovementModel)` — the D2-B flag `Bag.Movement`, the one shell path that writes L1 state; L1 never reads it (`setMovement`);
    - `FineTick` — 8 Hz (`FinePolicy`: 4/16 orthogonal, 3/16 diagonal, settle 4/16). Inert with no body or while a dialogue beat shows. A crossing runs as an ordinary `Move` through `RunStep` and is not logged separately; a refusal clamps to ±7 and bumps once; the outer edge clamps with no wrap; a moving body nudges stacks. `GameController.SyncClockCore` (and so `advanceTime`) drives it from the clock accumulator only while a body is stored (`NeedsFineTick`), otherwise the old loop runs unchanged.
  - D2-B is honoured only in realtime world bags; rogue ignores the flag. The TUI keeps sending `Move` in a free bag (no key-up, P2-D14). A driven commit's pose lines gain `fine <PoseText>` lines (body, then nudged stacks).
  - `FineFuzz` / `GameSession.WithFuzzedFine` / `GameController.FuzzFine` bypass `Apply` on purpose (fuzz noise must never be logged). Gate and tests only; never call them from a game path.
- **The L1 wall** (principle 6), enforced by:
  - `RealizationArchitectureTests`: `Assembly_ReferencesNoGodotOrTerminalGui`, `Source_HasNoFloatingPoint`, `CoreSimAndModels_DoNotReferenceRealization`;
  - `FineWallTests`: `SimAndModels_DoNotReferenceFine_ExceptTheSessionShell` (only `Models/GameSession.cs` may name `Pockets.Core.Fine`), `SimAndModels_DoNotReadTheMovementFlag_ExceptTheBagAndTheSessionShell`, `SimulationStep_TakesAndReturnsNoFineType`, `L1State_CarriesNoFineType`;
  - `FreeMoveSessionTests.Step_RejectsTheThreeShellHandledIntents`;
  - behaviourally: `FineFuzzTests` (`Fuzz_AfterEveryStep_LeavesEveryViewModelAndEventLineIdentical`, `RandomFineStates_TimesRandomL1Streams_GiveIdenticalL1`, and the mutation check `Mutation_L1ReadsAnOffset_FailsTheFuzz` via the internal `GameSession.FineLeakForTests` hook), `FreeMoveJourneyTests` (`TheDerivedMoves_ReplayedOnAPlainD2ASession_GiveIdenticalL1`, the offsets-only fuzz, and `FullFuzz_OfASteeredBody_DoesChangeL1_ByDesign`);
  - poses: `SceneJourneyTests` and `TimelineJourneyTests` (n = 1 ≡ VM over both journeys), `PoseGoldenTests` (handler = direct stream, replay, `Mutation_Walk126_FailsTheGolden`), `DrivenPoseLineTests` (the `fine` lines);
  - in parity: `--poses-out` + `--replay-check` (pose replay), the goldens `target-demo.poses` and `free-move.{checkpoints,events,poses}`, and `--fuzz-fine` on smoke and target-demo (`--fuzz-fine-offsets` on free-move).
- **Golden-neutral for L1:** no Realization or fine-state change may move the VM, events or buffer goldens. A policy change re-records pose goldens only (`make record-goldens`), which is still a decision for Aaron. **D2-B is an experiment awaiting Aaron's verdict** (keep / realtime default / delete); keep it deletable with no L1 edit (the file list is in the drift note).
- Godot can't run in WSL: build it with `dotnet build src/Pockets.Godot/Pockets.Godot.csproj` in every slice that touches it. Aaron's pass is `tools/journey-runner/README.md` § Windows check for Aaron.
