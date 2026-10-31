# journey-runner

The parity journey runner (Slice 0). Drives **one** journey script against a pluggable
frontend driver and emits a diffable **view-model checkpoint stream**, running the
**invariant pack** after every step. This is the machinery behind the repo's `make parity`
gate — see [design/parity-drift-report.md](../../design/parity-drift-report.md) and
[design/target-demo-build-plan.md](../../design/target-demo-build-plan.md) (principle 2).

## One-command entry points

```bash
make parity          # FAST gate: tui + mock-godot over journeys/smoke.journey.json, diff the VM, event and pose streams, replay check
make parity-full     # FULL gate: all three journeys on both drivers + goldens + the fine-state fuzz (--fuzz-fine)
make record-goldens  # regenerate journeys/goldens/ after an INTENTIONAL journey/state change
make parity-godot    # the full journey vs a live Godot build + screenshot set (needs a Godot runtime)
make parity-godot-free-move  # the free-move journey vs a live Godot build (restart Godot fresh first)
make test            # the shipping suites (Core + App + Realization)
```

Two standing gates (build plan sync policy — both pre-push):

- **`make parity`** — the **fast** gate: [`journeys/smoke.journey.json`](../../journeys/smoke.journey.json),
  the first ~10 minutes (through Slice 4's look-in-vs-enter). Run it constantly. It passes when both
  drivers finish invariant-green, their checkpoint, event **and pose** streams diff clean, and each driver's
  `--replay-check` passes.
- **`make parity-full`** — the **full** demo gate:
  [`journeys/target-demo.journey.json`](../../journeys/target-demo.journey.json), the complete
  30-minute thread. Runs both journeys on both drivers (it depends on `parity`), then
  regression-checks the committed **goldens** (below). Smoke is a proper VM-prefix of the target
  demo, so any smoke regression is also a target-demo regression. Since Phase 2 Slice 2.8 it also runs the
  [fine-state fuzz](#fine-state-fuzz-phase-2-slice-28) on smoke and the target demo, and the
  [free-move journey](#the-free-move-journey-phase-2-slice-28) with its goldens.

### Goldens (Slice 8)

Committed under [`journeys/goldens/`](../../journeys/goldens/):

- `target-demo.checkpoints` — the canonical VM checkpoint stream (driver-independent — byte-identical
  across drivers by construction, so recorded from tui). `parity-full` diffs a fresh run against it.
- `buffers/<label>.txt` — the TUI character-buffer render at each of the 11 progressive-UI **ledger
  rows** (every chrome materialization moment: dialogue box, grid, description pane, toolbar, look-in
  overlay, breadcrumbs, shrine view, clock readout, action queue, minimap, fullness pips). Captured at
  the checkpoints flagged `"golden": true`.
- `target-demo.events` (Phase 1 Slice 1.7). This is the canonical **event stream** of the target demo:
  305 lines, 21 kinds, no GUIDs. It is in the format described below and recorded from tui. `parity-full`
  compares a fresh run against it (`golden: event stream`). It is LF-pinned by `.gitattributes` like the
  other goldens.

- `target-demo.poses` (Phase 2 Slice 2.4). This is the canonical **pose stream** of the target demo, the one L2
  golden: what Godot will draw, proved without Godot. It is in the format described under
  [Pose golden](#pose-golden-phase-2-slice-24), recorded from tui, diffed cross-driver, and compared by
  `parity-full` (`golden: pose stream`). LF-pinned like the others.

- `free-move.checkpoints`, `free-move.events`, `free-move.poses` (Phase 2 Slice 2.8). The D2-B journey's VM, event and
  pose streams (57, 87 and 66 lines), recorded from tui, diffed cross-driver and against these by `parity-full`. The
  journey flags no `golden` checkpoints, so it has **no buffer goldens**: the TUI cannot show sub-cell motion (P2-D4,
  P2-D14), and its buffers would only repeat the VM.

`make record-goldens` regenerates them all after an intentional change (then commit the diff). On Aaron's
Windows box, `make parity-godot` additionally saves a Godot **screenshot** per ledger row into
`artifacts/godot-screenshots/` — the render-golden set for a vision-model pass (see drift report).

## Direct usage

```bash
dotnet run --project tools/journey-runner -- \
  --driver tui|godot|mock-godot \
  --script journeys/target-demo.journey.json \
  [--out artifacts/parity/tui.checkpoints] \
  [--goldens <dir>] [--screenshots <dir>] \
  [--events-out <file>] [--replay-check] [--poses-out <file>] \
  [--fuzz-fine <seed> | --fuzz-fine-offsets <seed>] \
  [--data <dir>] [--seed <int>] [--url ws://localhost:9080] [--dump]
```

Exit code `0` = pass, `1` = a failed assertion / invariant / driver error, `2` = bad args.
`--dump` also prints the checkpoint lines to stdout (handy for authoring scripts).

- `--goldens <dir>` — at every `"golden": true` checkpoint, write the driver's render probe to
  `<dir>/<label>.txt`. Only drivers with a render surface (tui) populate it; others emit a note.
- `--screenshots <dir>` — at every `"golden": true` checkpoint, ask a screenshot-capable driver
  (real **godot** only) to save `<dir>/<label>.png` (the transport-local `screenshot` action). Other
  drivers note-skip; it never blocks the run.
  A relative `<dir>` resolves against the runner's cwd and is sent to Godot as an absolute path, so the PNGs land in
  the repo-root `artifacts/godot-screenshots/` (not under `src/Pockets.Godot/`, where Godot resolves relative paths).

- `--events-out <file>` — write the run's event golden (below).
- `--replay-check` — replay the run's intent log on a fresh session (tui, mock-godot); fails if any
  step's view-model or event stream differs, or any commit's pose lines differ. Skipped with a note for `godot`.
- `--poses-out <file>` — write the run's pose golden (below).
- `--fuzz-fine <seed>` / `--fuzz-fine-offsets <seed>` — overwrite the session's L2a fine state with seeded noise after
  every step (tui, mock-godot; [below](#fine-state-fuzz-phase-2-slice-28)).

## Event golden + replay check (Phase 1 Slice 1.7)

`--events-out` writes one line per simulation event, LF-terminated:

```
<journeyStep>:<k> <step> <seq> <Kind> field=value…
```

`<step> <seq> <Kind> field=value…` is the canonical `EventText` line (invariant culture, bags named
`bag#N` by first appearance). `<journeyStep>` is the script step index (`-1` = the start observation) and
`<k>` the 0-based ordinal of the `Simulation.Step` within that journey step (it advances whenever `seq`
is `0`). The prefix exists because `(step, seq)` is not unique: untimed intents share a step number with
the next timed one and every Step restarts `seq` at 0. It is derived from the lines alone, so every driver
produces it identically; Step/Seq themselves are never renumbered. Streams are diffed across drivers with
`--compare`.

`--replay-check` records the intent count, view-model text and event lines at every step, then replays
`GameSession.IntentLog` from `profile.NewSession()` and requires identical view-models and event streams
at every boundary (and an identical replayed log). Failures are prefixed `replay:`. This is guardrail G6
in its Phase 1 form: replay(intents) ≡ state. The replay goes through the shell (`ApplyTraced`), so it
includes `Undo`. The intent log is the full history, undos included: undo truncates the *event* log, not
the intent log (ratified 2026-10-28). Undo appears in the stream as one `Rewound toStep=N` line. The
demo journeys do not undo, so `target-demo.events` has no `Rewound`; the free-move journey undoes once
(`free-move.events` has one `Rewound toStep=4`). Adding an undo to a journey adds golden lines,
so treat it as a deliberate change and run `record-goldens`.

Notes:
- The event lines come from `DriverObservation.Events`:
  - tui names its `GameView.LastEvents` with its own `EventText`;
  - mock-godot and live Godot read the `"events"` array of every `DebugCommandHandler` response
    (one `EventText` per controller).
- A `state` query adds no lines.
- On every step and driver, `VerifyEvents` requires an event stream, and requires at least one event
  whenever the VM changed. A pre-1.6 Godot build sends no `events`, so it fails this check.
- `godot` has no in-process session, so `--replay-check` is skipped there with a note. The tui leg of
  `parity-godot` runs the check instead.

## Pose golden (Phase 2 Slice 2.4)

`--poses-out` writes one line per **moving entity** per sample of each **commit**, LF-terminated:

```
<journeyStep>:<k> u=<q>/4 t=<ms> entity=<key> bag=bag#N col=<c> row=<r> subX=<x> subY=<y> height=<h|-> facing=<f|->
```

- A **commit** is one mutating command: a key, click, back, `tick` or `advanceTime` (one `Timeline.Commit` of that
  response's events). `<k>` is its 0-based ordinal within the journey step (a `tick: 3` step has commits 0–2). An empty
  commit still takes its ordinal, so k can skip. A `state` or `poses` query is not a commit.
- `u=<q>/4` is the sample point, q = 0…4, at `t = SpanMs·q/4` ms (integer) of the commit's span (P2-D5). After a clock
  commit, kept tracks may have started before 0; t = 0 is "now".
- The moving entities are the keys of the timeline's tracks (departure and landing keys, every channel), sampled with
  `Timeline.Sample`. A commit with no tracks writes no lines. The rest of the scene is the VM, already goldened.
- `entity=…` onward is the canonical `PoseText` (invariant culture; `Body(B)`, `Cursor(B)` or `Stack(bag#N[i])`). Bags
  are named through the run's **one** `EventText`, and a commit's events are named before its poses, so `bag#N`
  matches the event golden.
- Example: `112:0 u=2/4 t=62 entity=Body(B) bag=bag#7 col=3 row=1 subX=-8 subY=0 height=- facing=E` (a walk's midpoint).

**Driven commits (Slice 2.8, D2-B).** A free body has no tracks: its glide *is* its fine position, re-read on each
commit. So the sampled lines alone never show it. A commit is **driven** when a shown location's body is driven
(stored with a held direction or a non-zero velocity) before or after it. After its sampled lines, a driven commit adds:

```
<journeyStep>:<k> fine <PoseText>
```

- first, one line per driven body whose rest pose (cell plus `Px`/`Py`) changed, in `B, H, T` order;
- then one line per stack that stayed in its cell but whose offset changed (the P2-D11 nudge), in scene order.

Each line is the **new rest pose**. A commit with no driven body adds nothing, so a grid-locked journey's pose stream is
unchanged (`target-demo.poses` is byte-identical). With one `advanceTime: 125` per journey step (one fine tick), the
`fine` lines pin every 1/16 step of the walk. The lines come from `PoseStream.Commit`, so tui, mock-godot, live Godot's
debug server and the pose replay all produce them. Example: `20:0 fine entity=Body(B) bag=bag#2 col=0 row=1 subX=0
subY=-4 height=- facing=S` (just after a crossing S).

Where the lines come from:
- mock-godot and live Godot: the `"poses"` array that `Pockets.Realization.PoseDebugCommandHandler` adds to every
  success response (one `PoseStream` per controller; `state` returns `[]`). It wraps Core's `DebugCommandHandler`, which
  cannot hold a `Timeline` itself because Core never references Realization (principle 6). `{"action":"poses"}`
  returns the last commit's lines.
- tui: the driver's own `PoseStream` over the events it records. The TUI renders nothing new (P2-D4).
- The commit's `atMs` (P2-D13) is the clock delta: `advanceTime`'s ms, `GameController.TickLengthMs` for a `tick`, else
  0. It is never wall-clock.
- `--replay-check` replays the intent log through `PoseReplayCheck`, committing at each recorded commit's intent count
  with its `atMs`, and requires identical lines (failures are prefixed `replay poses:`).
- Timings are policy (`RealizationPolicy`, P2-D5). Tuning one is a pose re-record (`make record-goldens`), never an
  L1 change; the VM, event and buffer goldens must not move.

## Fine-state fuzz (Phase 2 Slice 2.8)

`--fuzz-fine <seed>` checks D5's wall behaviourally: **fuzz L2a ⇒ identical L1**. After the start observation and after
every journey step, the runner overwrites the session's fine state (`GameController.FuzzFine` →
`GameSession.WithFuzzedFine` → `Pockets.Core.Fine.FineFuzz.Generate`):

- about half the occupied cells of every location's active bag get an offset in ±6 per axis;
- every location's body gets a resting position in ±7 per axis (no velocity, nothing held);
- the noise is counter-hashed from `(seed, journeyStep)`, so it is the same on both drivers and every machine.

The overwrite is not an intent: it is not logged or replayed, so `--replay-check` is skipped under the fuzz with a note,
and the pose stream legitimately differs (do not compare it). The VM checkpoint and event streams must equal the
unfuzzed run's. `parity-full` checks that with `--compare` on smoke and the target demo, on both drivers (`FUZZ := 1`).

- **Caveat, by design:** the fuzz runs *after* each step's input, so it never changes which `Move`s the integrator derives
  within a step. A full fuzz of a **steered** body does change them, so the free-move journey is fuzzed with
  `--fuzz-fine-offsets` only (bodies kept). The Core test `FreeMoveJourneyTests.FullFuzz_OfASteeredBody_DoesChangeL1_ByDesign`
  pins that caveat. Fuzzing the integrator's inputs is the L1-projection test's job (`FreeMoveJourneyTests`).
- The mutation check: `FineFuzzTests.Mutation_L1ReadsAnOffset_FailsTheFuzz` uses the internal `GameSession.FineLeakForTests`
  hook (L1 reads an offset) and requires the fuzz to diverge.

## The free-move journey (Phase 2 Slice 2.8)

[`journeys/free-move.journey.json`](../../journeys/free-move.journey.json) drives D2-B. It uses the demo profile
(realtime) and enters Quiet 1 in six steps. It then sends `setMovement B Free` and steers, with one `advanceTime: 125`
(one 8 Hz fine tick) per journey step:

1. a tree bump (clamp to 7, bump once, then a blocked tick);
2. a settle, then a crossing S;
3. a diagonal SE (two crossings in one tick, X then Y);
4. a walk E through Dry Grass, which nudges it, and a second tree bump;
5. a harvest, which clears the offset, and an undo, which restores it with the L1 snapshot;
6. a D2-A key `Move` of the undriven body (P2-D14);
7. the outer-edge clamp at column 0, then leaving.

`parity-full` runs it on both drivers with `--replay-check` and diffs it cross-driver and against its goldens.

### Windows check for Aaron (Phase 2 close-out)

One pass covers Slices 2.5 (Godot animates from Realization) and 2.7/2.8 (D2-B free movement), plus Phase 1's
still-pending real-Godot events diff. It is also what the **D2-B verdict** waits on
([drift note](../../design/two-layer-phase2-drift.md#the-d2-b-verdict-pending-aarons-windows-pass)). Everything here
uses plain `dotnet`/`make` lines, so it runs under cmd-make, Git Bash or PowerShell.

1. **Build.** Pull `main`. Open `src/Pockets.Godot/project.godot` in Godot 4.6.1 (.NET) and Build, or run
   `dotnet build src/Pockets.Godot/Pockets.Godot.csproj`. Expect 0 errors. A build from before 2.4 sends no `poses`
   and fails the pose compares; rebuild (it references `Pockets.Realization`).
2. **`make parity-godot`.** Launch the scene **fresh and send it no input** (the `bag#N` names count from the
   controller's first event, so a key pressed in the window first shifts every name and the event diffs fail). Then run
   `make parity-godot`. It runs the target-demo tui leg (with `--replay-check`) and the godot leg over
   `ws://localhost:9080` (with the ledger-row screenshots into `artifacts/godot-screenshots/`), then **five compares**,
   all of which should say OK: checkpoints, events and poses tui vs live-godot, and the events and poses goldens vs
   live-godot. target-demo's poses have no `fine` lines.
3. **Restart Godot fresh.** `parity-godot-free-move` needs the start state and fresh naming, so it can't follow step 2
   on the same instance. Close the game, relaunch, send no input.
4. **`make parity-godot-free-move`.** Five compares, all OK: checkpoints, events and poses tui vs live-godot, and the
   free-move events and poses goldens vs live-godot. While the runner is connected the scene drives no frame-loop fine
   ticks (`HasPeers`), so every tick comes from the journey's `advanceTime` steps. If only `fine` lines diverge while
   the events match, suspect frame ticks leaking past `HasPeers`.
5. **Hand-play, grid-locked (2.5).** Restart and play by hand:
   - the body **walks** between cells in Quiet 1 instead of jumping, and the yellow highlight rides with it;
   - pressing into a tree gives a short **lunge** and back;
   - in an inventory bag the highlight **glides** between cells (60 ms);
   - Sort (4) **slides** the stacks to their new cells; a grab and drop inside one bag slides the stack;
   - Acquire Random (5) or a harvest makes the stack **pop** (lift and settle);
   - holding or mashing the arrow keys gives no lag and no queued animation (each press snaps, D7);
   - Ctrl-Z mid-walk leaves the body at its cell centre (A3);
   - the rest frame looks as it did before 2.5 (stack tiles fill their cells). One known change: the 3 px cursor
     border is always grey now (it used to take a framed cell's colour). Say if that matters.
6. **Hand-play, D2-B (2.7).** Disconnect every socket client (the scene sends fine ticks from its frame loop only
   while none is connected). Play into Quiet 1, then send `{"action":"setMovement","loc":"B","model":"Free"}` from a
   one-shot client (for example a tiny `websocat` call) and close it. Then:
   - an arrow press no longer jumps one cell;
   - a **held arrow glides** at about 2 cells/s, crossing into the next cell about every half second, and the
     highlight follows the sub-cell position;
   - **diagonals** (two arrows, or the stick at 45°) glide diagonally; a diagonal crossing steps X then Y in one tick;
   - gliding into a **tree** stops just short of its cell, the "✕ A tree" cue fires **once**, and holding longer gives
     no repeated bumps or lunge;
   - at the **outer grid edge** the body stops, with no wrap;
   - on **release** it settles to its cell centre within about a quarter second;
   - walking through a **stack nudges** it a little (up to 6/16 of a cell) in the walk direction; harvesting or moving
     it re-centres it.
7. **Feel notes, for the verdict.** Does 8 Hz motion look smooth without an interpolation track, or does it stutter?
   Does the constant 2 cells/s need acceleration? How do the 125 ms walk (grid-locked) and the 2 cells/s glide feel?
   Then the verdict: keep D2-B as an option, make it the realtime default, or delete it. Speed, settle and timing
   values are policy, so tuning them re-records pose goldens only.

## Drivers

| `--driver` | Path to Core | Full invariants? | Notes |
|------------|--------------|------------------|-------|
| `tui` | Real headless `GameView` under Terminal.Gui `FakeDriver`, driven via `ProcessKey` | yes | Provides a render probe (character buffer) for `assertRender`. |
| `mock-godot` | In-process `GameController` via `PoseDebugCommandHandler` (the exact Godot WS dispatch) | yes | Mock transport; no render probe. Used by `make parity` where Godot can't run. |
| `godot` | Live WebSocket to a running Godot build's `DebugWebSocketServer` | census limited* | Needs a Godot runtime. Used by `make parity-godot`. |

\* Over the wire only the active-bag view-model is available, so the global conservation
census is skipped for `godot` (a view-model-scoped check runs instead). The in-process
drivers run the full census.

## Script format (JSON)

```jsonc
{
  "name": "smoke",
  "description": "...",
  "steps": [
    { "key": "Right" },                                   // send a GameKey (by name)
    { "tick": 1 },                                         // advance N facility ticks (wait)
    { "back": true },                                     // leave-bag / back button
    { "click": { "row": 0, "col": 2, "button": "Primary" } },
    { "advanceTime": 125 },                               // advance the clock N ms (deterministic; 125 = one fine tick)
    { "setMovement": { "loc": "B", "model": "Free" } },   // D2-B flag (Slice 2.7; shell intent SetMovement)
    { "steer": "E", "advanceTime": 125 },                 // hold a direction ("none" releases; or {"loc","dir"}), then tick
    { "checkpoint": "pickup",                              // labels this step's checkpoint
      "key": "Primary",
      "assertViewModel": { "handEmpty": false },          // recursive subset match on the VM
      "assertRender": "Inventory",                         // substring in the render probe (tui)
      "conserves": false,                                  // opt out of the conservation check
      "golden": true },                                    // capture render golden here (--goldens/--screenshots)
    { "comment": "notes only, no action" }
  ]
}
```

- Each step usually performs one action (`key` / `tick` / `back` / `click` / `advanceTime`), then evaluates
  its assertions and emits a checkpoint (`{step, label, vm}` — one compact JSON line). The D2-B steps combine: within
  a step the runner applies `setMovement`, then `steer`, then the other actions, so `{ "steer": "E", "advanceTime": 125 }`
  holds E for one fine tick.
- `assertViewModel` is a **subset** match: every field you list must appear with an equal
  value; objects recurse, arrays match by prefix.
- `assertRender` is asserted only on drivers that expose a render probe (tui); elsewhere it
  is skipped with a note (it is **not** part of the cross-driver diff — only the view-model
  stream is).
- `conserves` (default `true`) asserts the whole-store item census is unchanged versus the
  previous step. Set `false` on steps that legitimately transform totals (craft completion,
  harvest, acquire) — the runner then records the delta as a note instead of failing.
- `golden` (default `false`) flags this checkpoint as a render-golden capture point (Slice 8). With
  `--goldens`/`--screenshots` the run saves the driver's render surface here; with neither flag it is
  inert. The target-demo journey flags exactly the 11 progressive-UI ledger rows.

## Invariant pack (after every step)

- **stack validity** — count in `[1, max]`; unique items are exactly 1; cell category /
  input-slot filters honored.
- **progressability** — no corrupt structure: cursors in bounds, bag references resolve,
  root/hand bags present.
- **item conservation** — whole-`BagStore` census diffed step-to-step (see `conserves`).

Implemented in `Pockets.Core.Rendering.InvariantChecker` (Core, so it is itself unit-tested
and runs identically behind every driver).
