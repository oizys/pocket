# Two-Layer Phase 1: As-Built Drift Note

*Slice 1.9 of the Phase 1 slice plan (see [two-layer-phase1-plan.md](two-layer-phase1-plan.md); parent
design [two-layer.md](two-layer.md), #23). Dated 2026-10-28. Records where the build drifted from the
plan text, what was ratified after the fact, and what stays open with a written reason. The per-slice
detail lives in the plan's addenda (1.0–1.8). This note is the summary.*

Standing goal: **no behaviour change.** Every Phase 1 slice landed golden-neutral. The VM checkpoint
golden and the 11 TUI buffer goldens were byte-identical through 1.0–1.8, and so were the per-slice
baselines of all 15 parity artifacts. The one added golden is `journeys/goldens/target-demo.events`
(1.7), and 1.8 left it unchanged. No action-log text changed (P1-D6).

---

## How Phase 1 is enforced now

- **`make parity` / `parity-full`.** tui and mock-godot run with `--events-out --replay-check`, and
  their event streams are diffed cross-driver. `parity-full` also checks the events golden. Replay
  re-applies `GameSession.IntentLog` on a fresh profile and requires an identical VM and event stream
  at every journey step: smoke 65 intents / 76 checkpoints / 149 lines; target-demo 141 / 156 / 305.
- **Architecture tests** (`tests/Pockets.Core.Tests/Sim/`) cover:
  - the bans on unseeded `Random` and on per-change `SetCell`/`SetItem` in step paths;
  - `Apply(Intent)` being the only public mutation, with frontends calling no session mutators;
  - every rule declaring its `Kind`/`TimeClass`/`EventKinds`;
  - intents that carry no closures or GUIDs;
  - Step being the only path.

  `CLAUDE.md` § Simulation architecture lists them by name.
- **Coverage check** (`EventJourneyTests`). On every step of both journeys, every changed cell, slot
  and cursor is named by an event.
- **Suite size at close-out:** Core 1025, App 54, spikes 109. All green.

---

## Drift from the plan text

| # | Plan said | As built | Status |
|---|---|---|---|
| 1 | `Sim.Step` | `Simulation.Step`. A type `Sim` is shadowed by the `Pockets.Core.Sim` namespace. | Naming only |
| 2 | Intent cases named in the 1.3 text (`Intent.Back`, …) | 32 data cases. `Back` = `Leave(At)` (the Q key and the back button were the same verb). The transitional `Intent.Tool` closure (1.2) was **deleted** in 1.3. `AdvancesTime` moved from the intents to `SimContent.TickMode`. | As intended |
| 3 | Recipe factory → `Func<SimRng, RecipeOutput>` | `Func<Random, RecipeOutput>`, fed a `SimRandom` (`SimRng.AsRandom(stream)`), because the generators are `Random`-typed and one cursor must advance across factories in a tick. | Equivalent |
| 4 | `ImmutableBagWriter` backed by `ImmutableArray<Cell>.Builder` | A struct over one `Cell[]` wrapped without a copy. With the builder, a single write cost more than the old `SetCell`. | Better allocation |
| 5 | The ban scan's `Rules/**` | The rules live in `Sim/Rules/`, covered by `Sim/**`. One exemption: `SimRng`'s counter dictionary. Pickup-to-toolbar was routed through `IBagWriter` rather than exempted. | As intended |
| 6 | 1.5 event table (≈ 15 kinds) | 28 Step kinds. Coverage forced in `CraftAborted`, `RecipeSelected`, `StackRemoved`, `DialogueQueued` and `ZoneReached`. Tool events are **derived** (`EventDeriver`), not emitted by rewritten tools. 1.8 adds `Rewound` (shell-only, so 29 cases in all). | As intended |
| 7 | `(Step, Seq)` orders events | Unique per Step call only: untimed intents share the next timed step's number and `Seq` restarts. The golden prefixes `<journeyStep>:<k>`. Step/Seq were not renumbered (contract). | Documented |
| 8 | Intent log: an intent that changed nothing is left out (1.3) | 1.7: an intent that changed nothing **and emitted nothing** is left out. A refused peek with its beat already fired emits `Bumped` with no state change, and replay must reproduce the cue. | As intended |
| 9 | 1.8: undo truncates the event **and intent** logs | Only the event log. The intent log is the full replay history, undos included. | **Ratified 2026-10-28, P1-D10 (Aaron: "Keep it as built")** |
| 10 | 1.8: struct events (spike B style) | `EventLog` is chunked and persistent, but holds the 1.5 record classes. The cap is applied to a size estimate. | Struct events are Phase 7 |
| 11 | Phase 1 in two-layer.md: a stable `ScheduleOrder(bag)` (G3) | Not added. One canonical pass plus `OrderedFacilities` and `EventDeriver.CanonicalOrder` fix the order. | Moves to Phase 4 |
| 12 | 1.6: craft-completion and dialogue cues from events | Not added, because neither frontend has such a one-shot cue and adding one changes the buffer goldens. The events are on every `ControllerResult` and debug response, ready for L2. Only `Bumped` drives a cue. | Deferred (behaviour change) |
| 13 | 1.4 benchmark "on the Workshop" | Measured on the Workbench (the loaded-recipe facility). The legacy path is copied verbatim into the test, so it is measured in the same process. | Equivalent |

## Retention as built (P1-D9 and 1.8)

- **Rogue:** the snapshots are the undo stack (≤ 1000). A depth trim moves the retention horizon.
- **Realtime:** a checkpoint every 8 steps, ring-bounded to 1000. An eviction moves the horizon.
- **Hard cap:** 8 MB, applied to an estimate. Past it, the oldest whole chunks are dropped and a
  `LogTruncated(Before, LastStep, Count)` marker is set **on the log**. The marker is not a `SimEvent`
  and never reaches a stream or golden.
- An **idle realtime session takes no checkpoints** (a tick that changes nothing is a no-op, so step 8
  never arrives) and relies on the cap alone. This works as designed.
- The **intent log has no retention**: ≈ 6k entries per 10 min at the soak's 10 Hz clock sync, and
  ≈ 36k at 60 Hz. Phase 7 (save = checkpoint + intents since) bounds it.
- Soak (10 simulated realtime minutes, demo profile): 6,341 events, ~0.6 MB retained, no truncation.
  gen0 is at or below the pre-1.8 baseline, and allocation is +4.4%.

---

## Fixed after Phase 1 (Aaron, 2026-10-28: "Fix both bugs")

Both landed in `5dfaf22`, golden-neutral (`make parity-full` green; VM, buffer and events goldens byte-identical, as
no journey undoes or aborts a craft). Shared code only (`GameSession.RunStepCore` / `CraftLogs`); no Godot file changed.

| # | Item | Fix | Tests |
|---|---|---|---|
| a | ~~Undo after a tool at a panel other than B restores the swapped frame~~ **Fixed.** | The tool route (and `RecipeSwitched`, which had the same shape) now pushes the unswapped pre-step `Current` as the undo snapshot; the swapped frame is only the tool's view and its log line's. | `EventLogSessionTests.ToolAtPanelOtherThanB_UndoRestoresThePreToolState` (C, W; replaces the `KnownBug_…` pin), `ToolAtPanelB_UndoRestoresThePreToolState` (unchanged path), `RecipeCycleTests.CycleRecipeAtPanelC_UndoRestoresThePreSwitchState` |
| b | ~~An aborted craft writes a "✦ … crafted: …" line~~ **Fixed.** | `CraftAborted` now writes `✦ {facility} craft aborted: {recipe name}` (same `✦ {facility} …: {recipe}` shape as the completion line). A line rather than nothing: an in-progress craft vanishing from the action queue should be explained, and the abort line keeps the old one-line-per-craft-end count (a tick that only aborts still logs the abort instead of `Tick #N`, as before). | `RecipeCycleTests.Tick_CompletedCraft_WritesTheCraftedLine`, `Tick_AbortedCraft_WritesTheAbortedLine_NotACraftedLine` |

## Open / follow-ups (not fixed; awaiting Aaron's ruling or a later phase)

| # | Item | Pinned by / where | Why it is open |
|---|---|---|---|
| c | **The real-Godot `make parity-godot` run with the events diffs** is still pending on Windows. | `tools/journey-runner/README.md` § Running `make parity-godot` on Windows | Godot cannot run in WSL. mock-godot shares `DebugCommandHandler`, and the Godot project compiles in WSL. Start a fresh build first (`bag#N` naming). |
| d | **P1-D7 debt:** the init-time seeded `System.Random` (demo and wilderness seeds, the Godot fallback) and the `double` fill ratio and loot weights in `WildernessTemplate`/`LootTableTemplate`, which run in step via the seedling. | two-layer.md G1/G2 | Converting them re-records goldens. Phase 3, when generation moves into the rule engine. |
| e | **Deterministic bag ids (G7).** Bags are still `Guid.NewGuid()`; events are renamed `bag#N` at serialization (P1-D8). | two-layer.md G7 | Phase 3 (needed for multiplayer and save/replay). |
| f | *(optional)* `ReplayCheck` could also compare `session.EventLog` with the replayed log, alongside the streamed lines. | 1.8 addendum | Left out to keep the gate unchanged. Not yet checked on the journeys. |
| g | Intent-log retention (above). | 1.8 addendum | Phase 7. |
| h | The action log is still built by the shell, not derived from events (P1-D6). This includes AcquireRandom's "added random item" quirk inside nested bags. | 1.1/1.3/1.5 addenda | Phase 3 revisit, as a golden risk. |
