# journey-runner

The parity journey runner (Slice 0). Drives **one** journey script against a pluggable
frontend driver and emits a diffable **view-model checkpoint stream**, running the
**invariant pack** after every step. This is the machinery behind the repo's `make parity`
gate — see [design/parity-drift-report.md](../../design/parity-drift-report.md) and
[design/target-demo-build-plan.md](../../design/target-demo-build-plan.md) (principle 2).

## One-command entry points

```bash
make parity          # FAST gate: tui + mock-godot over journeys/smoke.journey.json, diff the streams
make parity-full     # FULL gate: BOTH journeys on both drivers + target-demo golden regression
make record-goldens  # regenerate journeys/goldens/ after an INTENTIONAL journey/state change
make parity-godot    # the full journey vs a live Godot build + screenshot set (needs a Godot runtime)
make test            # the shipping suites (Core + App)
```

Two standing gates (build plan sync policy — both pre-push):

- **`make parity`** — the **fast** gate: [`journeys/smoke.journey.json`](../../journeys/smoke.journey.json),
  the first ~10 minutes (through Slice 4's look-in-vs-enter). Run it constantly. Passes when both
  drivers finish invariant-green and their checkpoint streams diff clean.
- **`make parity-full`** — the **full** demo gate:
  [`journeys/target-demo.journey.json`](../../journeys/target-demo.journey.json), the complete
  30-minute thread. Runs both journeys on both drivers (it depends on `parity`), then
  regression-checks the committed **goldens** (below). Smoke is a proper VM-prefix of the target
  demo, so any smoke regression is also a target-demo regression.

### Goldens (Slice 8)

Committed under [`journeys/goldens/`](../../journeys/goldens/):

- `target-demo.checkpoints` — the canonical VM checkpoint stream (driver-independent — byte-identical
  across drivers by construction, so recorded from tui). `parity-full` diffs a fresh run against it.
- `buffers/<label>.txt` — the TUI character-buffer render at each of the 11 progressive-UI **ledger
  rows** (every chrome materialization moment: dialogue box, grid, description pane, toolbar, look-in
  overlay, breadcrumbs, shrine view, clock readout, action queue, minimap, fullness pips). Captured at
  the checkpoints flagged `"golden": true`.

`make record-goldens` regenerates both after an intentional change (then commit the diff). On Aaron's
Windows box, `make parity-godot` additionally saves a Godot **screenshot** per ledger row into
`artifacts/godot-screenshots/` — the render-golden set for a vision-model pass (see drift report).

## Direct usage

```bash
dotnet run --project tools/journey-runner -- \
  --driver tui|godot|mock-godot \
  --script journeys/target-demo.journey.json \
  [--out artifacts/parity/tui.checkpoints] \
  [--goldens <dir>] [--screenshots <dir>] \
  [--events-out <file>] [--replay-check] \
  [--data <dir>] [--seed <int>] [--url ws://localhost:9080] [--dump]
```

Exit code `0` = pass, `1` = a failed assertion / invariant / driver error, `2` = bad args.
`--dump` also prints the checkpoint lines to stdout (handy for authoring scripts).

- `--goldens <dir>` — at every `"golden": true` checkpoint, write the driver's render probe to
  `<dir>/<label>.txt`. Only drivers with a render surface (tui) populate it; others emit a note.
- `--screenshots <dir>` — at every `"golden": true` checkpoint, ask a screenshot-capable driver
  (real **godot** only) to save `<dir>/<label>.png` (the transport-local `screenshot` action). Other
  drivers note-skip; it never blocks the run.

- `--events-out <file>` — write the run's event golden (below).
- `--replay-check` — replay the run's intent log on a fresh session (tui, mock-godot); fails if any
  step's view-model or event stream differs. Skipped with a note for `godot`.

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
at every boundary (and an identical replayed log). Failures are prefixed `replay:`.

## Drivers

| `--driver` | Path to Core | Full invariants? | Notes |
|------------|--------------|------------------|-------|
| `tui` | Real headless `GameView` under Terminal.Gui `FakeDriver`, driven via `ProcessKey` | yes | Provides a render probe (character buffer) for `assertRender`. |
| `mock-godot` | In-process `GameController` via `DebugCommandHandler` (the exact Godot WS dispatch) | yes | Mock transport; no render probe. Used by `make parity` where Godot can't run. |
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

- Each step performs at most one action (`key` / `tick` / `back` / `click`) then evaluates
  its assertions and emits a checkpoint (`{step, label, vm}` — one compact JSON line).
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
