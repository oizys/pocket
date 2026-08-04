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

## Slice 2 (dialogue box) — Godot node needs a Windows Godot-runtime pass
Same posture as Slices 0–1: the Godot-side dialogue edits (`GameSceneController` — load the
`DialogueBook`, gate the room grid on `ChromeElement.Grid`, and a bottom-third `PanelContainer`
overlay with a `ColorRect` portrait placeholder + line label driven by `state.Dialogue`) are
**mechanical and reviewed but not compiled here** (no Godot .NET SDK in this WSL). The Core dialogue
system, the view-model `dialogue` field, and the demo-opening journey section are fully exercised by
`make parity` (TUI + mock-godot, 51 checkpoints, diff-clean) — the mock-godot driver runs the exact
serializer + command dispatch the real Godot server uses, so only the actual Godot process/render
is unverified. **Action for Aaron:** on the Windows box, rebuild `src/Pockets.Godot`, launch, and run
`make parity-godot` to confirm the live pass + eyeball the dialogue overlay (colored-rect portrait,
grid hidden at frame 0, box drops on Primary → grid appears). Placeholder art only — no asset pipeline.

## Slice 3 (fixed inventories) — Godot node needs a Windows Godot-runtime pass
Same posture as Slices 0–2: the Godot-side edit (`GameSceneController` — the bottom-bar toolbar label
now renders `RenderHelpers.FormatToolbarSummary(state)`, the depth-invariant T-bag contents, instead
of a static hotkey hint; still ledger-gated on `ChromeElement.Toolbar`) is **mechanical and reviewed
but not compiled here** (no Godot .NET SDK in this WSL). All Slice-3 mechanics are Core and fully
exercised by `make parity` (TUI + mock-godot, **60 checkpoints, diff-clean**) plus Core/App unit tests:
the toolbar as a real depth-invariant bag, pickup-to-first-toolbar-slot routing (demo-profile only —
non-demo keeps grab-into-hand, suites prove it), and the bag-as-partial-empty recursive acquire
(overflow into a non-full toolbar bag). The new `toolbar` view-model field (occupied slots + one level
of nested contents) is emitted by the shared serializer, so both drivers diff it identically. **Action
for Aaron:** on the Windows box, rebuild `src/Pockets.Godot`, launch, `make parity-godot` to confirm
the live pass + eyeball the bottom bar (empty until first pickup, then Rock/Wood/Leather appear, with
the Coin Pouch showing a nested `[n/2]` capacity readout).

Design notes ratified this slice (documented in code):
- **Demo toolbar size = 1×4** (a single row): three fillable slots + a seeded non-full **Coin Pouch**
  carrying bag in slot 3. Sized to the demo's item variety so one short journey exercises both
  slot-fill order AND capacity overflow. Non-demo profiles keep `CreateStage1`'s 10×1 toolbar.
- **Pickup vs grab-for-move**: a *bare pickup* (Primary on a resting inventory item, empty hand, at
  root depth) routes to the toolbar; the *hand* stays the in-flight cut buffer, still reached by
  Primary on the toolbar/facility panels (routing is B-focus-only). The demo's craft delivers *from*
  the toolbar, proving grab-for-move remains available.
- **Acquire ordering**: within each bag, true-empty cells + mergeable same-type stacks fill first
  (top-left), then any remainder descends into non-full **plain** sub-bags (top-left). Facility and
  wilderness bags are never descended into (you don't dump loose items into a crafting station or an
  enter-only world).

## Slice 4 — look-in vs. enter; enter-only bags

Landed the journey's `C`-peek / `E`-enter split and the enter-only property. Exercised by
`make parity` (TUI + mock-godot, **75 checkpoints, diff-clean**) plus Core/App unit tests.

Design notes ratified this slice (documented in code):
- **`C` = generic peek (new `GameKey.Peek`)**: opens a **one-deep** look-in overlay (the C container
  panel) over ANY peekable bag at the cursor — a plain **Chest** included, which `Primary`/`E` would
  instead *enter*. Toggles closed on `C`; `Q` also closes. Reuses `OpenAsContainer` → `ApplyResult`,
  so `FirstPeek → LookInOverlay` fires identically on both drivers. One-deep for the demo: peeking is
  a top-of-cursor action; nested peeking is out of scope until a later slice needs it.
  - *Worker's call (per brief latitude):* the existing `Primary`-opens-facility-as-C /
    wilderness-as-W routes are left UNCHANGED (keeps the Slice-3 smoke green). `Peek` is an
    additional, uniform route that always opens the cursor bag as a C look-in — the only path that
    peeks a plain chest without entering.
- **`Bag.EnterOnly`** (bool, default false, travels with the bag through the store): a peek is
  **refused** — no panel, cursor/world untouched — surfacing a failed-peek affordance and firing a
  fire-once `DialogueTriggerKind.FirstFailedPeek` beat ("Can't just peek at this one."). `E` still
  enters via breadcrumbs. **No visible glyph/frame marks an enter-only bag** (RATIFIED 2026-08-03;
  the failed peek is the only tell — the eyelid-shut glyph is a banked future Shrine unlock). The
  property is deliberately **absent from the view-model** so no frontend can render a marker.
- **Failed-peek affordance** = a non-serialized, consume-on-read `GameController.FeedbackPulse`
  (`FailedPeek`) the frontends play each refusal: **TUI** flashes the command strip
  ("✕ Enter-only — …"); **Godot thin equivalent** surfaces the cue on the status line + the shared
  dialogue box renders the once-only beat, and `DebugCommandHandler` returns the same refusal in its
  `status`. It never touches game state, so checkpoints/goldens are unperturbed.
- **Demo content**: a peekable **Chest** (4×2, Smooth Pebble + Spring Water) at free cell 9 (1,1)
  and an enter-only **Quiet Pocket** placeholder (a cheap Slice-6 wilderness stand-in) at cell 10
  (1,2). Both at pinned free cells so earlier smoke coordinates don't shift; non-demo profiles are
  untouched (suites prove).

Godot render note (extends #4): the real Godot frontend still draws no C/W look-in panel, so a
successful peek there opens the C location without a visible overlay — the same deferred render gap
as the existing facility/wilderness panels (not new to this slice). The `C` key, the enter-only
status cue, and the dialogue beat are wired; the overlay *render* rides the same Slice-1+ follow-up.

## Follow-ups for later slices
- **Slice 1 (`UiLedger`)**: render C/W/T panels in Godot from ledger state; then the existing
  `openPanels` checkpoints assert render parity on both drivers.
- **Enter-only glyph** (banked): the eyelid-shut frame + a "see enter-only property" reveal are
  future Shrine feature unlocks, deliberately outside the 30-minute demo (RATIFIED).
- **Keymap**: bring Godot's keyboard `MapKey` to TUI parity (bundle with Slice 1).
- **Serializer**: expose open-panel interiors when a slice needs to assert inside them.
- **Realtime**: the demo profile pins Rogue for a deterministic baseline; the journey's
  realtime-clock beat (Slice 5) will introduce a driver-level clock/`wait` protocol — the
  runner already models `tick`/wait steps for it.
