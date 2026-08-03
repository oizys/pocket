# TUI ↔ Godot Parity Drift Report

*Slice 0 of the target-demo build plan (see [target-demo-build-plan.md](target-demo-build-plan.md)).
Dated 2026-08-03. Audits the divergences between the TUI (`Pockets.App`) and Godot
(`Pockets.Godot`) frontends, records what was reconciled this slice into one shared
`GameInitializer` demo profile, and lists what is deliberately deferred with a written
reason — including everything that needs a machine with a Godot runtime.*

Standing goal (Aaron): **the TUI and Godot builds remain in sync with the same supported
harness testability throughout.** This report is the baseline snapshot the parity gate
(`make parity`) now defends.

---

## How parity is enforced now

`make parity` runs one journey script ([`journeys/smoke.journey.json`](../journeys/smoke.journey.json))
on two drivers and diffs their **view-model checkpoint streams**:

- **TUI driver** — the real shipping `GameView` running headless under Terminal.Gui's
  `FakeDriver`, driven through its actual `ProcessKey` path (the TUI keymap included).
- **mock-godot driver** — an in-process `GameController` driven through
  `Pockets.Core.Rendering.DebugCommandHandler`, the exact server-side dispatch the real
  Godot WebSocket server runs per packet. Only the socket is mocked.

Both serialize through the single `Pockets.Core.Rendering.ViewModelSerializer`, so identical
Core state yields byte-identical checkpoints **by construction**. The invariant pack
(`InvariantChecker`: stack validity, progressability, item-conservation census) runs after
every step on both drivers. Current status: **35 checkpoints, streams diff-clean, invariants
green on both.**

The real-Godot pass (`make parity-godot`, live WebSocket to a running Godot build) is the
same script against `--driver godot`; see *Deferred → needs a Godot runtime* below.

---

## Divergences found

| # | Divergence | Severity | Status |
|---|------------|----------|--------|
| 1 | **Tick mode** — TUI built its session at the default `TickMode.Realtime` (+ a wall-clock tick timer); Godot pinned `TickMode.Rogue`. The same key sequence produced *different* state (Realtime defers facility/plant ticks to an external timer; Rogue ticks per action). | **High** — silent state divergence | **Reconciled** |
| 2 | **Unseeded start state** — both frontends called `CreateFromRegistry(registry, new Random())`, and the wilderness generator (`GeneratorBuiltins.Wilderness`/`Shuffle`) *ignored even that* and span up its own `new Random()`. Start states were nondeterministic and never matched between builds. | **High** — no reproducible baseline | **Reconciled** |
| 3 | **Keyboard keymap** — Godot's `MapKey` lacks WASD, `Tab`/`Shift-Tab` (focus), `#`/Shift-3 (begin-split), `Enter`/`Esc` (confirm/cancel) that the TUI's `MapKey` has. | Low for the harness | **Deferred (documented)** |
| 4 | **Rendered chrome** — Godot renders only the B grid, hand, breadcrumb, description, a toolbar label, and the action log. It draws **no C/W/T panels and has no focus cycling**; the TUI renders all of them. | Medium (render parity) | **Deferred → Slice 1 (`UiLedger`)** |
| 5 | **Duplicated view-model/serializer** — Godot's `DebugWebSocketServer` carried its own `SerializeState` copy, a latent drift source against any TUI-side view-model. | Medium (maintenance) | **Reconciled** |
| 6 | **Init note in the brief** ("Godot inits Workshop-only vs the TUI's Stage-3 init") | — | **Already converged** — both frontends already called `CreateFromRegistry`; the residual gap was the *seed/tick* (see #1, #2), now closed. The Workshop-only shape is a property of `CreateFromRegistry` (only Workshop is placed; other facilities are player-crafted) and is now **shared** by both. |

---

## What was reconciled this slice

### One shared demo profile
`GameInitializer.CreateDemoProfile(registry, seed?)` → `DemoProfile { State, Recipes,
FacilityRecipeMap, TickMode }`, seeded with `DemoSeed = 20260803` and pinning
`TickMode.Rogue`. Both frontends now load it:

- **TUI** — `Program.cs` builds the profile; `GameView` gained a `tickMode` parameter and a
  public `Controller` accessor (the latter lets the headless runner read the session without
  reflection).
- **Godot** — `GameSceneController.InitializeGame` calls `CreateDemoProfile(...).NewSession()`.

This closes #1 and #2: both builds start in **byte-identical** state and tick identically.

### Determinism fix in the generator
`GeneratorBuiltins.Wilderness` and `Shuffle` now accept an optional `Random` and use the
caller's seeded rng (threaded from `CreateFromRegistry`). Without this the demo profile was
*not* reproducible even at a fixed seed — the direct cause of #2's cross-build mismatch.

### Shared, transport-agnostic serializer + command handler (closes #5)
`SerializeState` and the WS command dispatch moved out of `DebugWebSocketServer` into Core
(`ViewModelSerializer`, `DebugCommandHandler`). The Godot server now delegates to them
(screenshot stays transport-local). One serializer, three consumers (TUI driver, real Godot
WS, mock transport) — the drift source is gone. The view-model also gained an `openPanels`
field so opening/closing a bag as a C/W/T panel is a first-class, cross-driver checkpoint
signal (a small, deterministic step toward Slice 1's chrome-as-state).

---

## Deferred (with reason)

### Keymap parity (#3) — deferred, low harness impact
The Godot **keyboard** map is missing several keys, but the Godot **WebSocket** route parses
any `GameKey` by name, so every action is harness-drivable regardless. The journey script is
authored at the `GameKey` level, so the runner is unaffected. Bringing Godot's keyboard map to
TUI parity (WASD, Tab focus, split, Enter/Esc) is a small UX task best done alongside the
Slice 1 chrome work (Tab focus only matters once panels render). **Not a parity-gate blocker.**

### C/W/T panel rendering + focus (#4) — deferred to Slice 1
Godot draws no container/world/toolbar panels and has no focus cycling. This is exactly what
`UiLedger` (Slice 1) is for: chrome presence becomes Core state both frontends render from.
The view-model already exposes `openPanels`, so the moment Godot renders those panels the
existing checkpoints cover them. Until then, the smoke's "enter/exit a bag" beat is asserted
at the **state** level (`openPanels`) on both drivers — which is diff-clean — and the
per-frontend render is a TUI-only `assertRender` (skipped-with-note on mock-godot).

### View-model scope: active bag only
The checkpoint view-model reports the **active (B) bag** + hand + `openPanels`, not the
interior of open C/W panels. Conservation still covers everything (the census walks the whole
`BagStore`, including facility/panel bags), and the smoke's craft is asserted via the action
log + census delta. Exposing panel interiors in the view-model is a Slice-1+ item (it pairs
with rendering them). Noted so a later slice that needs to assert *inside* a facility panel
knows to extend the serializer.

### Needs a Godot runtime (this WSL env can't) — Aaron's Windows machine
This environment has **no `godot`/`godot4` binary** and **no Python `websockets` module**, so
neither the Godot editor/runtime nor the original `tests/agent_play_godot.py` transport can run
here (consistent with [godot-quirks.md](godot-quirks.md): Godot needs its .NET assembly built
and a display/runtime WSL doesn't provide). Consequences:

- The Godot-side edits (`GameSceneController` → demo profile; `DebugWebSocketServer` →
  delegate to Core) are **mechanical and reviewed but not compiled here** — `Pockets.Godot`
  is excluded from `Pockets.sln` and needs the Godot .NET SDK to build. **Action for Aaron:**
  on the Windows box, `dotnet build src/Pockets.Godot/Pockets.Godot.csproj`, launch the app,
  then `make parity-godot` — it drives the live WebSocket server with the same smoke script and
  diffs the Godot checkpoint stream against the TUI baseline. Expected: diff-clean.
- Because the mock-godot driver runs the **identical** Core command-dispatch + serializer the
  real server runs, `make parity` (TUI + mock-godot) already exercises everything except the
  actual Godot process, its input mapping, and its rendering. The live pass is a confirmation,
  not a leap.
- Over the WebSocket transport, the runner sees only the active-bag view-model (not the whole
  store), so the **global conservation census is unavailable to the `--driver godot` path**; it
  falls back to a view-model-scoped invariant check (per-cell count/max, cursor bounds) and
  flags conservation as transport-limited. The in-process drivers (`tui`, `mock-godot`) run the
  full census, so the parity gate here loses no coverage.

---

## Follow-ups for later slices
- **Slice 1 (`UiLedger`)**: render C/W/T panels in Godot from ledger state; then the existing
  `openPanels` checkpoints assert render parity on both drivers.
- **Keymap**: bring Godot's keyboard `MapKey` to TUI parity (bundle with Slice 1).
- **Serializer**: expose open-panel interiors when a slice needs to assert inside them.
- **Realtime**: the demo profile pins Rogue for a deterministic baseline; the journey's
  realtime-clock beat (Slice 5) will introduce a driver-level clock/`wait` protocol — the
  runner already models `tick`/wait steps for it.
