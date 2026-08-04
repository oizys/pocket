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

---

# Slice 8 — Full journey assembly + goldens (CLOSING SECTION)

*Dated 2026-08-04. The final slice: no new mechanics — everything below is assembly, goldens,
and the standing gate. This section closes the drift report for the target-demo build plan.*

## State of parity (as of Slice 8)

The 30-minute journey is assembled, executable, and provable on both in-process drivers with one
command. Two standing gates, both pre-push (build-plan sync policy):

| Gate | Script | Scope | Status |
|------|--------|-------|--------|
| `make parity` | [`journeys/smoke.journey.json`](../journeys/smoke.journey.json) | **Fast** — first ~10 min (through Slice 4 look-in-vs-enter), **76 checkpoints** | tui + mock-godot **diff-clean**, invariants green |
| `make parity-full` | [`journeys/target-demo.journey.json`](../journeys/target-demo.journey.json) | **Full** — the complete 0:00→30:00 thread, **153 checkpoints** | tui + mock-godot **diff-clean**, invariants green, **goldens clean** |

Smoke is a **proper VM-prefix** of the target demo (identical `label`+`vm` for its first 76
checkpoints — verified), so any smoke regression is also a target-demo regression, and `parity-full`
depends on `parity` (a single invocation runs BOTH scripts on BOTH drivers). Suites green: **855 Core
+ 51 App**. `Pockets.Godot` csproj compiles under the Godot .NET SDK (Windows).

### Goldens (committed, regression-checked every `parity-full`)

Under [`journeys/goldens/`](../journeys/goldens/):

- **`target-demo.checkpoints`** — the canonical VM checkpoint stream (driver-independent: byte-
  identical across drivers by construction, so recorded from tui). `parity-full` diffs a fresh tui run
  against it — the cross-build parity diff *and* a standing regression diff, in one file.
- **`buffers/<label>.txt` ×11** — the TUI character-buffer render at each **progressive-UI ledger
  row**, captured the frame the chrome materializes: `frame-0` (dialogue box), `opening-dismissed`
  (grid), `inspect-1` (description pane), `pickup-slot-0` (toolbar), `enter-bag` (look-in overlay),
  `toolbar-depth-1` (breadcrumbs), `s5-enter-shrine` (shrine view), `s5-notice-clock` (clock readout),
  `s7-craft-start` (action queue), `s7-craft-complete` (minimap), `s5-slot-core` (fullness pips). These
  are the "buffer-grab asserts the element is ABSENT before its trigger and PRESENT after" checkpoints
  from the journey doc's ledger, frozen as goldens. Regeneration is deterministic (FakeDriver + fixed
  `DemoSeed`); `make record-goldens` refreshes them after an intentional change.

The runner grew a `"golden": true` step flag + `--goldens <dir>` (TUI buffer dump) + `--screenshots
<dir>` (Godot viewport PNG via a screenshot-capable driver interface). These are pure harness
plumbing — inert unless the corresponding output dir is passed, and they never touch the checkpoint
stream. The bags-in-bags **hint negative** is asserted in both scripts (`hint-negative` checkpoint:
the script nests, so no hint dialogue ever fires — see deferred list). The **axe negative** (gatherer
path out of reach) is asserted positively in the journey's `final-golden` checkpoint (demo-axe known +
still a recipe card, never a Stone Axe) and definitively by the Core test
`CraftingTableTests.DemoTables_CraftCompass_Wilderness_AndPouch_ButNeverTheAxe` (`!HasItem "Stone
Axe"`), since the subset-matcher can only assert presence, not census-level absence.

## Windows portability of the parity gates (follow-up, 2026-08-04)

Aaron ran the standing gates on Windows and hit two real issues; both are now fixed so `make parity`
and `make parity-full` behave identically under **cmd-spawned make**, **Git Bash**, and **Linux sh**.

**1. cmd-make choked on sh-only recipes.** The old recipes used `mkdir -p`, `diff -u`, `if…fi`, and
`rm -rf` — pure sh. Windows `make` (not run from a sh shell) hands recipe lines to `cmd.exe`, which
fails them (`The syntax of the command is incorrect … Makefile:40`). **Fix:** every recipe is now a
flat sequence of `dotnet run` calls, with directory-creation, comparison, and cleanup folded into the
journey-runner:
- `--out <file>` self-creates its parent directory (no `mkdir`).
- `--compare <expected> <actual> [--label t]` and `--compare-dirs <expectedDir> <actualDir>` replace
  `diff`/`diff -r` — EOL-normalized, PASS/FAIL to stdout, exit 0/1 (make gates on the exit code).
- `--clean <dir>…` replaces `rm -rf`.

There is **no sh syntax left in the Makefile** — the recipes are the same tokens on every platform.

**2. `GOLDEN FAIL` on `toolbar-depth-1.txt` under Git Bash — line endings.** The runner writes buffer
goldens with LF (`TuiDriver.DumpBuffer` emits `\n`); the committed goldens, with no `.gitattributes`
pinning EOL, checked out as **CRLF** under Git for Windows' default `core.autocrlf=true`. Every line
then read as drifted. Confirmed by the PM against Aaron's checkout (`/mnt/c/git/pocket`): golden CRLF,
artifact LF, **byte-identical after EOL normalization** — no real render drift. **Fix, belt + suspenders:**
- **Suspenders — [`.gitattributes`](../.gitattributes):** `journeys/goldens/** text eol=lf`, plus
  `*.journey.json` and `*.checkpoints` as `eol=lf`. This forces LF in the working tree on checkout AND
  normalizes to LF in the repo on commit, regardless of `core.autocrlf` — so a golden re-recorded on a
  Windows box still commits LF, and any fresh clone checks out LF.
- **Belt — normalized compare:** `Compare.cs` normalizes CRLF/CR→LF (and ignores a trailing final
  newline) on *every* comparison. So even a working tree carrying stale attributes, or a golden that
  slipped in as CRLF, still passes. Line endings can no longer re-enter the parity signal. The runner
  now also always writes the VM checkpoint stream with LF, so goldens recorded on any OS are byte-stable.

Verified locally by CRLF-ifying every committed golden (reproducing Aaron's exact `toolbar-depth-1.txt`
failure) and confirming `make parity-full` stays green, then restoring.

**3. Culture audit (hardening).** Swept the serializer + render + data-load paths for locale-sensitive
formatting and pinned `InvariantCulture` where output feeds the VM/buffers/goldens or the demo profile:
- `ContentParsers` `double.Parse` for loot **FillRatio / ×Weight** — the real trap: under a
  comma-decimal locale (de-DE) `double.Parse("0.5")` silently yields **5.0**, corrupting the loot
  tables → demo profile → every downstream golden (`data/seedling-pot.md` carries `×0.5`, `×0.3`,
  `FillRatio: 0.6`). Now `InvariantCulture`.
- `ViewModelSerializer.FormatClock` (mm:ss readout), `GlyphRenderer`/`GridDiagram` stack counts —
  invariant-pinned.
- `RenderHelpers.AbbreviateName` `ToUpper` → `ToUpperInvariant` — the Turkish-I trap (`"iron"`→`"İRON"`
  under tr-TR) that would drift toolbar/hand abbreviations.

Proven by `CultureInvarianceTests` (Core): the demo VM serialization, the loot-table decimals,
`FormatClock`, and `AbbreviateName` are all asserted byte-identical under **de-DE** and **tr-TR** vs
the InvariantCulture baseline.

### What Aaron should see / do on Windows

- **cmd-make** (`make parity`, `make parity-full` from a plain Windows shell) and **Git Bash** now run
  the same recipes to the same PASS/FAIL — no more `cmd.exe` syntax error, no `GOLDEN FAIL`.
- **After pulling this change, no manual step is required for the gate**: the normalized compare passes
  whether the goldens are CRLF or LF. The `.gitattributes` is purely to keep the *working tree* tidy
  (LF). If you want the working tree normalized immediately in an **existing** clone (Git does not
  re-checkout unchanged files just because attributes changed), run once after pulling:
  ```
  git add --renormalize journeys/goldens/ && git checkout -- journeys/goldens/
  ```
  A **fresh clone** needs nothing — the goldens check out LF by attribute. Either way, `make parity-full`
  is green. `make parity-godot` still needs a live Godot runtime (below); it now uses `--compare` too.

## What remains Windows-only (needs a Godot runtime)

This WSL env has no `godot`/`godot4` binary and no display, so the real Godot process cannot run
here. Unchanged from Slices 0–7, and the full-journey extension inherits it:

- **`make parity-godot`** — now runs the **full** target-demo journey against a live Godot build
  (ws://localhost:9080) and diffs its stream against the TUI baseline. Because the `mock-godot` driver
  runs the *identical* Core command-dispatch + serializer the real server runs, `make parity-full`
  already exercises everything except the actual Godot process, its input map, and its rendering. The
  live pass is a confirmation, not a leap. **Action for Aaron (Windows):** `dotnet build
  src/Pockets.Godot`, launch, then `make parity-godot`. Expected: diff-clean.
- **Godot screenshot goldens** — `parity-godot` now passes `--screenshots artifacts/godot-screenshots`,
  so on the Windows box the runner saves one viewport PNG per ledger row (same 11 labels as the TUI
  buffers) via the transport-local `screenshot` action. This is the Godot render-golden set for the
  vision-model pass (agent-testing prong 3). Scripted here; it only fires where a Godot viewport
  exists (the in-process drivers note-skip), so it never blocks the standing gates.
- **Godot render gaps** (drift #4, still open) — Godot draws no C/W/T look-in panels and has no focus
  cycling; a successful peek opens the C location without a visible overlay. The chrome-as-state
  `ui`/`openPanels` view-model is fully driven; only the *render* of those panels rides the deferred
  Slice-1-follow-up Godot pass. The screenshots will make this gap visible (and, once closed, provable)
  on the Windows run.

## Deferred (stays deferred — with reason)

- **Conditional weighted loot-table director** (the drop director: prereq-gated inclusion,
  known/present exclusion) — specced in the journey doc's three-path-cliff section, its own post-demo
  slice. The demo uses fixed placements (mechanism 1+2): recipe cards + a seeded-but-reproducible
  wilderness scatter. No director in the 30.
- **Sort** — a later *found* feature (hour 2+, when the hoard is real), keeping its keeper line "Order.
  There you are." (RATIFIED). By :28 there isn't enough mess to need it; fullness was the felt need, so
  the eye core unlocks pips, not Sort.
- **Bags-in-bags soft hint + first-nest memory fragment** — both need a nesting-**event** dialogue
  trigger (fire the hint iff nesting-count==0 by a marker; key the memory fragment to the first nest).
  Core's `DialogueTriggerKind` has no such kind, and adding one is new mechanics — out of scope. The
  script *does* nest, so the negative is the test: `hint-negative` asserts no hint fires. The bag
  **fullness flag** the hint gestures at is itself a banked future Shrine glyph.
- **Godot render gaps** (above) — C/W/T panels + focus cycling in Godot, the deferred Slice-1-follow-up
  frontend pass. Everything is ledger-driven and ready to render.
- **SVG glyph import** — the real `assets/glyphs/basis-quiet-positive.svg` → `Texture2D` import is the
  deferred Godot glyph pass (`glyphs.md` TODO). In-demo both frontends draw an env-header *approximation*
  (TUI ASCII staircase `=== == =`, Godot label placeholder) from the VM `env` fields.
- **Enter-only glyph** (banked) — the eyelid-shut frame + a "see enter-only property" reveal are future
  Shrine feature unlocks, deliberately outside the 30 (RATIFIED; the first failed peek is the only tell).

## As-built decisions (ratified in code across Slices 0–8)

The demo diverged from a literal reading of the journey doc in several places — each a deliberate,
ratified-in-code call from a prior slice's Running notes, pulled here into one list so the journey doc
and the implementation reconcile:

1. **Deterministic demo profile** (Slice 0) — `GameInitializer.CreateDemoProfile(registry, seed?,
   dialogue)` with `DemoSeed = 20260803`; the wilderness generator threads the seeded RNG. Both
   frontends load the identical profile, so start states are byte-identical. The `TickMode` started
   pinned **Rogue** for a deterministic baseline and switched to **Realtime** at Slice 7 (clock-driven
   craft) — scripted `advanceTime` is the ONLY time source in the harness; the wall clock is never read.
2. **Parity via a .NET journey-runner + mock transport** (Slice 0) — Godot can't run in WSL, so the
   gate is one script on two in-process drivers (`tui` = real headless GameView under FakeDriver;
   `mock-godot` = the exact server-side `DebugCommandHandler` dispatch), sharing one
   `ViewModelSerializer` → byte-identical streams by construction. The live `godot` driver is the
   Windows confirmation.
3. **Dialogue is monotonic / does not undo** (Slice 2, RATIFIED) — queue, line index, fired-beats, and
   inspected-items carry forward across `Undo`. Fire-once is only sound if fired-beats can't roll back.
   Beats are data-driven in `/data/dialogue/*.md`, beat-keyed scripted conditions (deterministic, no
   RNG, no cooldown machinery).
4. **Pickup-to-toolbar routing is a demo-only `GameState.ToolbarPickup` flag** (Slice 3) — decided in
   `GameController` where focus is known, gated on `_focus == B`, so grabbing FROM the toolbar/facility
   panels keeps the classic grab-into-hand cut buffer. Non-demo profiles never take the branch.
   **Demo toolbar = 1×4** (3 fillable slots + a seeded non-full Coin Pouch in slot 3) — sized so one
   short journey exercises both slot-fill order AND capacity overflow. **Acquire ordering (RATIFIED):**
   empties + mergeable stacks first (top-left), then descend into non-full *plain* sub-bags;
   facility/wilderness bags are never descended into.
5. **`C` = generic `Peek`, a new key** (Slice 4) — a uniform one-deep look-in over any peekable bag
   (plain Chest included, which `E`/Primary would instead *enter*); the existing Primary-opens-
   facility/wilderness routes are left unchanged (keeps earlier checkpoints green). **`Bag.EnterOnly`**
   refuses the peek — no panel, world untouched — firing a fire-once `FirstFailedPeek` beat + a
   non-serialized `FeedbackPulse` the frontends flash. **No enter-only marker in the view-model**
   (RATIFIED: the failed peek is the only tell; no glyph in-demo).
6. **Shrine = a plain enterable bag** in the home room (Slice 5), not a doorway — simplest reachable
   placement. **Feature slots** = `FeatureSlotFrame(glyph, locked, filter?)` matching a core by its
   `Glyph` property, locking irreversibly on slot. **Clock** = injectable `IGameClock` (VirtualGameClock
   for parity). **Fullness pips** = a Core calc emitted on every bag cell only when the `FullnessPips`
   chrome is on; `CoreSlotted` flips it game-wide — the first unlock that rewires rendering everywhere.
7. **Wilderness = the real Quiet 1** (Slice 6) replacing the Slice-4 "Quiet Pocket" placeholder at the
   same cell; **the eye-core source was re-pointed** from a Slice-5 Chest stopgap to the ruin bag
   (append-only history: the journey is now wilderness → ruin → core → Shrine). **Unnavigable trees** =
   `TreeFrame` + `Cell.IsUnnavigable`; the cursor refuses, bumps count, and `NthTreeBump` fires the
   axe-absence line once at 3. **Recipe-as-item** = `RecipeItemProperty` + `GameState.KnownRecipes`,
   learned when a recipe item reaches hand/toolbar. Fixed placements (loot director deferred).
8. **Crafting Table = generic assembler** (Slice 7) — `GetRecipesForFacility` special-cases it to
   `KnownRecipes ∩ Recipes`. Three pre-loaded tables in the home room so a craft is triggered by
   *learning a card + advancing time*, not brittle material-loading. **Minimap** lights on *entering* an
   EnterOnly wilderness (not crafting one) — so litCount is 1 at compass-complete, 2 after entering the
   crafted Quiet 1. `TickFacilities` iterates in a deterministic GUID-free order (the fix for a 3-craft-
   on-one-tick log divergence). **The three-path cliff:** explorer (`another-quiet-1` → craft+enter a
   2nd Quiet 1), organizer (`belt-pouch` → a new carrying bag), gatherer (`demo-axe` known + readable
   but Iron-Ore-priced → known-but-uncraftable, the axe negative).
9. **Two journeys, one prefix** (Slice 8) — `smoke` was trimmed from the grown full thread back to a
   fast core-mechanics gate (through Slice 4) and `target-demo` promoted to the canonical full script +
   the closing `final-golden`/`hint-negative` beats. A deliberate restructure (append-only history: the
   full thread is preserved verbatim in target-demo; smoke is its proper prefix).

---

# Playtest fixes (2026-08-04) — Aaron's first target-demo run

The first real playthrough of the target demo (Windows) surfaced six items. All are landed; this section
is the as-built record. One item (toolbar quickmove / Primary-macro rework / diminished hand role) is
**PARKED** pending Aaron's design call and was deliberately NOT implemented.

## What changed (by item)

1. **[BUG, was blocking] Recipe-switch destroyed items.** Repro: with a full home room, switching a
   facility's recipe deleted the items sitting in its slots. **Root cause:** `GameSession.ExecuteCycleRecipe`
   dumped the slot items and then discarded `Grid.AcquireItems`' *unplaced remainder* (`var (newGrid, _) =
   rootGrid.AcquireItems(...)`) — a silent item sink whenever the root bag had no room. **Fix:** the dumped
   items are re-homed through `ApplyRecipeSwitch`, which acquires them into the root bag and, if any stack
   cannot be fully placed, **refuses the whole switch** (nothing mutated, items kept) rather than dropping
   them. The Crafting Table no longer dumps at all (see item 4/5: generic slots + set-recipe), so its path
   is deletion-proof by construction. **Coverage:** Core `PlaytestFixesTests` (full-root refusal conserves;
   room-in-root switch conserves; generic-table set-recipe conserves) + the journey now exercises a recipe
   *set* with conservation asserted, closing the invariant hole (there was previously no cycle/switch step
   in either journey, so conservation was never checked on this path).

2. **[BUG] Entering the Coin Pouch from the toolbar wedged the UI.** Primary/E on a bag living in the
   **T** panel used to breadcrumb-*enter* it, pushing a breadcrumb onto the T location; the toolbar panel
   then showed the nested bag with no way out (Q/LeaveBag act on B, not T). **Fix:** in
   `GameController.ExecuteFocusedPrimary`, Primary on a toolbar bag now opens it as a **C look-in overlay**
   (peek), never enters — toggles closed cleanly on Q/C. Regression: `PlaytestFixesTests.ToolbarBag_Primary_
   Peeks_DoesNotEnter_AndClosesCleanly`.

3. **[FIX] Recipe cards are consumed on learn.** Learning a recipe used to leave the card resting in the
   toolbar. Now `GameSession.RegisterKnownRecipes` **learns and removes** the card (poof on learn):
   `KnownRecipes` gains the id and the card's cell empties. This is a *sanctioned* census removal — the
   journey-runner gained an `expectDelta` step field that asserts the census changed by EXACTLY the
   consumed card (`{"Compass Recipe": -1}` etc.), so conservation is accounted for precisely, not silently
   weakened to a blanket `conserves:false`. Coverage: `PlaytestFixesTests.PickingUpARecipeCard_LearnsIt_
   AndConsumesTheCard` + `expectDelta` on every learn step in the journey.

4. **[FIX] One crafting table, starting EMPTY.** The antechamber seeded three pre-loaded, recipe-pinned
   tables (compass/wilderness/loom — a Slice-7 journey convenience) that instantly crafted the moment their
   recipe was learned, because the ingredients already sat in them. Replaced with **one empty table**:
   generic (unfiltered) input slots + `FacilityState.RequiresSelectedRecipe`, so it never auto-scans and
   stays inert until the player selects a recipe and loads it through play. The compass ingredients now sit
   loose in the antechamber (cells 14/15, the two freed by dropping the old tables); the journey grabs them
   to the toolbar and drops them into the table. Kills the instant-craft-on-learn surprise (Belt Pouch
   included). Core: `CraftingTableTests` rewritten to `EmptyTable_StaysInert_UntilARecipeIsSelected` +
   `SelectLoadCraft_*` (compass / wilderness→new EnterOnly Quiet 1 / pouch→bag) + `DemoProfile_CannotCraft
   TheAxe_IronOreTooScarce` (the gatherer negative is now material scarcity — only one Iron Ore exists, the
   axe needs two).

5. **[FEATURE] Modal recipe menu — replaces recipe cycling.** R now opens a real **modal recipe list**
   (`RecipeMenuState` on `GameSession`, projected to the VM as `recipeMenu`): it lists the facility's
   craftable set (KnownRecipes ∩ loaded), ↑/↓ navigate, Enter selects (sets the active recipe), Esc/Q
   closes. The controller owns the keys while it is open (modal-lite, like `SplitMode`). Rendered on both
   frontends — **TUI real** (`RecipeMenuView`, a centered overlay) and **Godot thin** (a centered panel).
   `GameKey.CycleRecipe` was renamed `GameKey.RecipeMenu`. **The old no-modal-dialogs rule is REVERSED**
   (Aaron) — noted here and in [`tui-redesign.md`](tui-redesign.md) #7 (its status header). The underlying
   `FacilityLogic.CycleRecipe` primitive remains (for legacy filtered-slot facilities) but is now routed
   through the conservation-safe placement from item 1.

## Journey / golden changes (deliberate, append-only history)

The target-demo journey's **Slice-7 section (24:00–30:00) was rewritten** and its goldens re-recorded
(`make record-goldens`). Every earlier beat (Slices 0–6) is unchanged; the smoke journey stays a proper
prefix. Documented diffs:

- **Ruin recipe grab (`c6-grab-recipe`)** — grabbing the "another Quiet 1" card now consumes it
  (`expectDelta {"Quiet Recipe": -1}`, hand ends empty). The follow-on `c6-swap-core` became a plain
  `c6-grab-core` (no card rides back into the ruin — it's gone).
- **Learn beats (`s7-learn-compass/pouch/axe`)** — each now carries `expectDelta {"<card>": -1}` and asserts
  the toolbar is unchanged (cards no longer route there).
- **New load-through-play beats** — grab the loose Dry Grass/Bone Chips to the toolbar, open the empty
  table, open the **modal recipe menu** (`s7-recipe-menu`, `recipeMenu` asserted + `assertRender:"Recipe"`),
  select compass (`s7-select-compass`, `recipeMenu:null`), then the established toolbar-sourced load
  (`s7-drop-grass`/`s7-drop-bone`) into the trays.
- **Compass craft** — `s7-craft-start` (ActionQueue materializes, one row, progress 1/3) → `s7-no-drift` →
  `s7-craft-progress` (2/3) → `s7-craft-complete` (Minimap materializes, queue drains, compass produced —
  a declared transform: Dry Grass −3, Bone Chips −2, Quiet Compass +1). These two remain the golden
  ledger-row captures.
- **Three-path cliff (`final-golden`)** — now proven by **availability**: all three non-axe recipes are
  KNOWN (in hand by 30:00), the headline compass is crafted, and the axe is known-but-uncraftable. The
  journey no longer crafts a second wilderness + a pouch at two extra tables (that was the removed
  3-table convenience); each recipe's craftability is instead proven at the Core level
  (`CraftingTableTests.SelectLoadCraft_*`). `minimap.litCount` is therefore **1** at the end (only the
  found Quiet 1 was entered), not 2.

Golden set: `journeys/goldens/target-demo.checkpoints` + the 11 `buffers/*.txt` re-recorded (the home-room
buffers now show the two loose material stacks at cells 14/15; `s7-craft-start`/`s7-craft-complete` show
the single compass craft). `make parity` (76) + `make parity-full` (156) green, cross-driver + goldens
clean; suites 875 Core + 53 App.

## Runner change

`JourneyStep.ExpectDelta` (`expectDelta` in JSON): when present, the runner asserts the census delta versus
the previous step equals the map EXACTLY (nothing more appeared or vanished). It is the principled account
of a sanctioned removal/transform — used for the consumed recipe cards — as opposed to `conserves:false`,
which only says "something changed, allowed."

## Parked (NOT implemented — Aaron's design call pending)

Toolbar quickmove / Primary-macro rework / diminished hand role. PM is tracking this separately.
