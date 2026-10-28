# Two-Layer Phase 2: As-Built Drift Note

> **Source of truth: this repo copy.** Vault reading copy:
> `~/obsid/paths/projects/pockets/two-layer-phase2-drift.md` (mirrored with a hash header).

*Slice 2.9 of the Phase 2 slice plan (see [two-layer-phase2-plan.md](two-layer-phase2-plan.md); parent
design [two-layer.md](two-layer.md), #23; Phase 1's note: [two-layer-phase1-drift.md](two-layer-phase1-drift.md)).
Dated 2026-10-28. Records where the build drifted from the plan text, what was decided along the way, and what
stays open with a written reason. The per-slice detail lives in the plan's addenda (2.0–2.8). This note is the
summary.*

Standing goal: **L1 does not change.** Every Phase 2 slice landed golden-neutral for L1. The VM checkpoint golden,
`target-demo.events` and the 11 TUI buffer goldens are byte-identical from the start of the phase to the end. The phase
added exactly the two golden sets the plan allowed: `target-demo.poses` (2.4, 305 lines) and the `free-move.*` set
(2.8: `.checkpoints` 57, `.events` 87, `.poses` 66). After 2.4, `target-demo.poses` itself never changed. `Simulation.Step`
was never edited (no `Sim/Simulation*.cs` change in any slice); the only `Sim/**` edits are the four new shell-handled
intent records in `Intent.cs`.

---

## How Phase 2 is enforced now

- **`make parity` / `parity-full`.** Every run also writes a pose stream (`--poses-out`), diffed cross-driver (tui
  against mock-godot); `--replay-check` also replays the pose lines (`PoseReplayCheck`). `parity-full` adds the pose
  golden, the fine-state fuzz on smoke and target-demo (both drivers, checkpoints and events equal to the unfuzzed run),
  and the free-move journey on both drivers with `--replay-check`, its goldens and an offsets-only fuzz. 26 compares in
  all, about 42 s in WSL.
- **Architecture tests** (named in `CLAUDE.md` § Realization and fine state):
  - Realization references Core only, no Godot and no Terminal.Gui, and has no floating point or `Math.`
    (`RealizationArchitectureTests`);
  - `Sim/**` and `Models/**` name neither `Pockets.Realization` nor `Pockets.Core.Fine`, except `GameSession.cs`; L1
    never reads `Bag.Movement`; `Simulation.Step*` takes and returns no fine type; no L1 state type carries one
    (`FineWallTests`).
- **Behavioural wall:** the fuzz gate, `FineFuzzTests.RandomFineStates_TimesRandomL1Streams_GiveIdenticalL1` (10 seeds ×
  120 commands), the L1-projection test
  `FreeMoveJourneyTests.TheDerivedMoves_ReplayedOnAPlainD2ASession_GiveIdenticalL1`, and the mutation check
  `FineFuzzTests.Mutation_L1ReadsAnOffset_FailsTheFuzz` (also run once through the real gate: 32 failures, exit 2).
- **n = 1 ≡ VM:** `SceneJourneyTests` (rest poses vs the VM at every step of both journeys) and `TimelineJourneyTests`
  (`Sample(SpanMs)` equals the rest scene after every commit).
- **Suite size at close-out:** Core 1167, App 54, Realization 169 (Phase 1 closed at Core 1025, App 54). All green.

---

## Drift from the plan text

| # | Slice | Plan said | As built | Status |
|---|---|---|---|---|
| 1 | 2.0 | `PoseText` keeps its own bag naming in step with `EventText` | `EventText`'s naming moved into a public `Pockets.Core.Sim.BagNames`; `EventText(BagNames)` and `PoseText(BagNames)` share one object per run. | As intended (the one Core edit of 2.0) |
| 2 | 2.1 | The body is the cursor of a location whose active bag passes `GameState.IsWildernessType(env)` | **Widened:** `Scene.IsWorldBag(bag) = IsWildernessType(env) \|\| bag.EnterOnly`. The demo's Quiet 1 (env `"Quiet 1"`) is enter-only, not a wilderness-type name, so without the clause the demo has no body. `Integrator.IsWorldBag` is the same rule, pinned by a cross-test. | Necessary; matches `IsDescendableBag` |
| 3 | 2.1 | `IFineView` as the offset source | It lives in Realization, not Core (`FineView.Empty`, later `FineView.Of(FineState)`); 2.7 added a default member `IsDriven`. | Equivalent |
| 4 | 2.2 | `Track(…, Curve, Channel)` | Plus `Overlay` and chaining (same-entity tracks play back to back; a stack moved 0→1→2 stays `Stack(0)` and lands on `Stack(2)`). Inventory cursor glide is an `EntityKey.Cursor(loc)` overlay (`CursorGlideMs = 60`), drawn only while live. A wrap or click teleport snaps. | As intended |
| 5 | 2.3 | Snap decided per mode | Snap is decided **from the events** (`Choreography.IsClock`), not from `TickMode`. An empty commit counts as input and snaps. `ReactionOverlapPercent` is the only offset knob (reactions and ambient). | As intended (P2-D13) |
| 6 | 2.4 | The `Timeline` lives inside `DebugCommandHandler` | **Deviation:** the handler is in Core, which may not reference Realization. Core gained a seam (`Handle(…, extend)`, `EventTextFor`), and Realization's `PoseDebugCommandHandler` holds the per-controller `PoseStream` and is what Godot and mock-godot now call. `ReplayCheck` gained a public `Replay(fresh, log)` enumerator so `PoseReplayCheck` needs no Core internals. | Necessary (principle 6) |
| 7 | 2.4 | Pose golden prefix like the events golden | `<journeyStep>:<k>` with k the **commit** ordinal (an empty commit keeps its ordinal, so k can skip). A `state`/`fine`/`poses` query is not a commit. | Documented in the README |
| 8 | 2.5 | `GameSceneController` gains `_Process(delta)` sampling a `Timeline` | The Godot-free parts went into Realization (`PosePlayer`, `FrameClock`, `PoseProjection`, `DrawnPose`/`SampleDrawn`), all integer and tested in WSL. Godot draws from its **own** `PosePlayer`, fed through a debug-server `onCommit` hook for socket commands. One visible change at rest: the 3 px cursor border is always grey (it used to take a framed cell's colour). | As intended |
| 9 | 2.5 | Route or pause the Godot clock under the debug server | Neither was needed: the Godot scene has no live clock (it keeps `VirtualGameClock`), so every clock commit already goes through the socket. Rule recorded in the controller: a live clock, if ever added, must route through the handler or pause while driven. | Decided |
| 10 | 2.6 | `UndoMarks` gains the fine state | **Deviation:** `LogMark` is in `Sim`, behind the wall. `GameSession` holds `FineMarks` (parallel to the undo stack) and `FineCheckpoints` (parallel to the checkpoint ring) instead. | Necessary (the wall) |
| 11 | 2.6 | `Direction8` in `Pockets.Core.Fine` | In `Models`: `Intent.Steer` in `Sim/Intent.cs` must not cross the wall. | Necessary |
| 12 | 2.6 | — | `Intent.SetFineOffset` (test-only, P2-D11) made the intent cases **32 → 33**. `Timeline` slides now start from the previous scene's offset (`PrevSub`), so a nudged stack doesn't jump to centre when it moves. | As intended |
| 13 | 2.7 | Two shell-handled intents (`Steer`, `FineTick`) | **Three:** `SetMovement(At, MovementModel)` is the replayable flag setter. It is the one shell path that writes L1 state (`Bag.Movement`); L1 never reads it (scanned). Intent cases **33 → 36**. Undo caveat: a snapshot from before `setMovement` restores `GridLocked`, and the next `FineTick` drops the stale body. | Accepted |
| 14 | 2.7 | A tree refusal clamps and bumps | A **blocked axis** (held, `v == 0`, `p == ±7` toward the held direction) stays put, so a tree bumps **once per press**, not 8 times a second. | Better feel; pinned |
| 15 | 2.7 | `FineTick` while a body moves | Ticks **pause while a dialogue beat shows** (inert), and stale bodies (bag left or no longer free) are dropped first. `GameController.SyncClockCore` runs the old loop unchanged when no body is stored (the golden-neutrality guarantee); otherwise fine ticks every 125 ms, fine before `Tick` on whole seconds. | As intended |
| 16 | 2.7 | "Godot interpolates between the last two fine ticks" | **No interpolation track.** A free body has no tracks; its pose is its fine position, re-read on every commit, so Godot moves it in 4/16-cell steps 8 times a second. See follow-up (c). | Open (Aaron's feel pass) |
| 17 | 2.7 | P2-D13: what snaps | Answered twice: `IsClock(events, driven)` treats derived Moves and tree bumps at a driven location as clock-like, and `Timeline.Commit(…, fineTick: true)` never snaps (Godot's event-less frame ticks). A driven body's `CursorMoved`/`Bumped` add no walk or lunge track. | As intended |
| 18 | 2.8 | The pose golden pins the glide | The sampled lines alone can't (no tracks). **`fine` lines** (`<journeyStep>:<k> fine <PoseText>`) are added to **driven** commits only: the driven body's new rest pose, and — beyond the PM's wording — every stack whose offset changed in place (the nudge). No driven body ⇒ nothing added, so `target-demo.poses` is unchanged. | PM decision; extension accepted |
| 19 | 2.8 | Fuzzing is part of the gate | `FineFuzz` / `GameSession.WithFuzzedFine` / `GameController.FuzzFine` are **public writers that bypass `Apply`**, on purpose: fuzzed state must never enter the intent log. Test and gate only; no game path calls them. Under the fuzz the replay check is skipped and the pose stream is not compared. | Accepted deviation from "Apply is the only public mutation" |
| 20 | 2.8 | Full fuzz on every journey | Full fuzz on smoke and target-demo only. A fuzzed **steered** body derives different Moves (pinned by `FullFuzz_OfASteeredBody_DoesChangeL1_ByDesign`), so free-move gets an offsets-only fuzz (`--fuzz-fine-offsets`). | By design; meets the exit criterion |
| 21 | 2.8 | free-move enters Quiet 1 as target-demo does | Its own 6-step route (dismiss, Down, Right, Right, dismiss, enter), not target-demo steps 0–73: a shorter, independent golden with the same Quiet 1 layout. | Equivalent |
| 22 | 2.8 | New goldens `free-move.checkpoints`, `.events`, `.poses` | Exactly those. **No buffer goldens:** the journey flags no `"golden": true` rows, and the TUI cannot show sub-cell motion (P2-D4, P2-D14). | As intended |
| 23 | 2.8 | — | Not pinned in the poses: the harvest clearing an offset and the undo restoring it (neither commit is driven). Covered by `FineReconcileTests`/`FineSessionTests`, and the undo is in `free-move.events` (`Rewound toStep=4`). | Covered elsewhere |

---

## The D2-B verdict: **pending Aaron's Windows pass**

A1 calls D2-B an experiment, and this is where it gets judged. Nobody has seen it move yet: Godot can't run in WSL.
The verdict needs the [Windows check](../tools/journey-runner/README.md#windows-check-for-aaron-phase-2-close-out).
The three options and what each would cost:

| Option | What changes | Code cost | Goldens |
|---|---|---|---|
| **Keep it as an option** (the as-built state) | Nothing. Optional follow-ups: promote the flag to a data-file field on the wilderness templates (P2-D12's small follow-up); decide interpolation (c) and acceleration. | None now. | None now. Any policy tuning (speed, settle, 8 Hz) re-records `free-move.poses`, plus `.events`/`.checkpoints` wherever crossings move to other ticks. Never the demo goldens. |
| **Make it the realtime default** | World bags in realtime sessions start `Free`: a data field (or a default) instead of the debug-only `setMovement`. Godot arrows steer in every realtime world bag. Rogue stays D2-A (D2). TUI keeps sending `Move` (P2-D14). | Small in Core (where the flag comes from), plus deciding (c) and the harvest-while-unsettled question first. Free-move's `setMovement` step becomes redundant. | Expected to leave the demo goldens alone (the TUI and the journeys send key `Move`s, which an undriven body walks D2-A style, and `SyncClockCore` is unchanged while no body is stored), but that must be proven by `parity-full`, not assumed. Free-move goldens re-record if the flag source changes the intent log. |
| **Delete it** (principle 7) | D2-A only. L2a fine state, `SetFineOffset`, `FineFuzz` and the fuzz gate on smoke/target-demo **stay** (they're not D2-B). | Delete `Fine/Integrator.cs`, `Fine/FinePolicy.cs`, `Models/MovementModel.cs` + `Bag.Movement`; `Intent.Steer`/`FineTick`/`SetMovement` (36 → 33) and their `GameSession` branches (`FineTickCore`, `IsFree`, `NeedsFineTick`, `FineTickLengthMs`) and the `SyncClockCore` fine-tick loop; the controller wrappers and the `steer`/`setMovement` debug commands; Realization's driven-body rule (`IFineView.IsDriven`, `Scene.Driven`, `IsClock`'s `driven`, `Timeline`/`PosePlayer` `fineTick`, `PoseStream`'s `fine` lines) and `SteerInput`; Godot's steering input and `DriveFineTicks`/`CommitFineTick` in `GameSceneController` and `HasPeers` in `DebugWebSocketServer`; the runner's `steer`/`setMovement` steps; the Makefile's free-move legs and `parity-godot-free-move`. Tests: `IntegratorTests`, `FreeMoveSessionTests`, `FreeMoveDebugCommandTests`, `FreeMoveJourneyTests`, Realization `FreeMoveTests` and `DrivenPoseLineTests`, the `.Movement` wall scan, and `setMovement` in the property test's stream. **No `Sim/Simulation*` or other L1 edit.** | Delete `journeys/free-move.journey.json` and `journeys/goldens/free-move.*`. `target-demo.*`, the buffers and smoke are untouched. |

Also wanted from the same pass (plan §What Aaron runs, row 2.9): a feel note on the 125 ms walk and the 2 cells/s
speed. Both are policy values, so changing them re-records pose goldens only.

---

## Open / follow-ups (not fixed; awaiting Aaron's ruling or a later phase)

| # | Item | Pinned by / where | Why it is open |
|---|---|---|---|
| a | **Pre-existing crash:** an Undo that closes an open C look-in leaves `GameController`'s focus on C; the next tool key (Sort, in the repro) is routed to the missing location and throws `KeyNotFoundException` in `Simulation.SwapIn`. Fine-independent (it's L1/controller). | Property-test seed 18 (`FineFuzzTests.RandomFineStates_…`, which tolerates it only when both controllers throw identically); 2.8 addendum | **Not fixed. Aaron hasn't ruled.** The likely fix is the controller's focus-follow after Undo (`FollowFocus`): small and golden-neutral (no journey undoes a look-in). |
| b | **Aaron's Windows pass:** build, `make parity-godot`, restart fresh, `make parity-godot-free-move`, then the hand-play list from 2.5 and 2.7. Also closes the Phase 1 item (c), the real-Godot events diff. | [README § Windows check for Aaron](../tools/journey-runner/README.md#windows-check-for-aaron-phase-2-close-out) | Godot can't run in WSL. The exit criterion "Aaron has done the eyeball pass" is the one left unchecked. |
| c | **Possible stutter in D2-B:** no interpolation track, so Godot moves a free body in 4/16-cell steps at 8 Hz. | 2.7 addendum § Notes for 2.8 | Feel question for (b). The candidate fix is a short `Body` interpolation track over `TickMs` for fine commits: a `RealizationPolicy` change plus a `free-move.poses` re-record. `target-demo.poses` unaffected. |
| d | **Intent-log growth:** every moving or settling `FineTick` is logged, 8 per second while a body moves. The free-move journey's 55 steps log 92 intents, 32 of them `FineTick`s (and 35 `SetElapsed`s). | Risk 6; 2.8 addendum | No retention until Phase 7 (save = checkpoint + intents since). Fine for an experiment. |
| e | **D2-B verdict** (above). | This note | Needs (b). |
| f | **Acceleration** for a free body, or constant speed plus settle? | Plan § Open questions | Policy content either way; Aaron's feel pass. |
| g | With D2-B, should **harvesting require a settled body**? v0: "the cell you are in". | Plan § Open questions | Rarely visible because release settles in ≤ 2 ticks. Decide if D2-B becomes the default. |
| h | **Undo in a free bag is exact but coarse:** it restores the fine state at the last L1 snapshot (realtime fine ticks push none), so the body jumps back to its pre-snapshot sub-position. | Risk 4; 2.6/2.7 addenda | Accepted by the plan. |
| i | **TUI sub-cell display** (half-blocks/braille) | Plan § Open questions | Re-records the buffer goldens. Out of scope. |
| j | **Godot cursor border** at rest is now always grey (it took a framed cell's colour before 2.5). | 2.5 addendum | Cosmetic; confirm or reject in (b). |
| k | The runner doesn't compare **fine state at replay boundaries**; the pose replay covers it indirectly (driven commits' `fine` lines, and rest poses of moving entities). | 2.6 addendum | Add a `FineText` compare to `ReplayCheck` if a gap ever shows. |
| l | `Timeline.Sample` regroups tracks on every call. | 2.2 addendum | Fine at today's sizes; precompute if Godot's per-frame sampling shows up in profiles. |
| m | A full fuzz of free-move is not run (rule 20). | 2.8 addendum | Options if wanted: fuzz only bodies with `Held == null`, or skip a step while `NeedsFineTick`. Not needed for the exit criterion. |
| n | Entity identity is still realization-local (`EntityKey` by cell, P2-D2). | Risk 3 | Real ids with G7 (Phase 3). |
| o | The Phase 1 follow-ups (d)–(h) carry over unchanged: P1-D7 seeded `Random`, G7 bag ids, the optional `ReplayCheck` event-log compare, intent-log retention, the shell-built action log. | [two-layer-phase1-drift.md](two-layer-phase1-drift.md) | Phases 3 and 7. |
