# journey-runner

The parity journey runner (Slice 0). Drives **one** journey script against a pluggable
frontend driver and emits a diffable **view-model checkpoint stream**, running the
**invariant pack** after every step. This is the machinery behind the repo's `make parity`
gate — see [design/parity-drift-report.md](../../design/parity-drift-report.md) and
[design/target-demo-build-plan.md](../../design/target-demo-build-plan.md) (principle 2).

## One-command entry point

```bash
make parity          # tui + mock-godot over journeys/smoke.journey.json, diff the streams
make parity-godot    # tui + live Godot (needs a running Godot build; see drift report)
make test            # the shipping suites (Core + App)
```

`make parity` passes when both drivers finish with the invariant pack green **and** their
checkpoint streams diff clean.

## Direct usage

```bash
dotnet run --project tools/journey-runner -- \
  --driver tui|godot|mock-godot \
  --script journeys/smoke.journey.json \
  [--out artifacts/parity/tui.checkpoints] \
  [--data <dir>] [--seed <int>] [--url ws://localhost:9080] [--dump]
```

Exit code `0` = pass, `1` = a failed assertion / invariant / driver error, `2` = bad args.
`--dump` also prints the checkpoint lines to stdout (handy for authoring scripts).

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
      "conserves": false },                                // opt out of the conservation check
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

## Invariant pack (after every step)

- **stack validity** — count in `[1, max]`; unique items are exactly 1; cell category /
  input-slot filters honored.
- **progressability** — no corrupt structure: cursors in bounds, bag references resolve,
  root/hand bags present.
- **item conservation** — whole-`BagStore` census diffed step-to-step (see `conserves`).

Implemented in `Pockets.Core.Rendering.InvariantChecker` (Core, so it is itself unit-tested
and runs identically behind every driver).
