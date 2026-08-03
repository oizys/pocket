> **Source of truth: this repo copy.** Mirrored 2026-08-03 by the Slice-0 parity worker
> from `~/obsid/paths/projects/pockets/target-demo-build-plan.md`. The vault copy is **Aaron's reading copy**
> (phone/Obsidian); the repo copy is **canonical for Claude Code sessions** going forward.
> Edits that matter to implementation land here first. See
> [parity-drift-report.md](parity-drift-report.md) and [INDEX.md](INDEX.md).

---

# Pockets — Target Demo Build Plan

*(PM draft 2026-08-03. Turns [[target-demo-journey]] (decision-complete)
into worker-sized slices. Grounded in the repo's real state: `Pockets.Core`
/ `Pockets.App` (TUI) / `Pockets.Godot` all exist and share
`GameController`; the agent-testing harness (#13) already has both prongs
— TUI `FakeDriver`/`TuiTestHarness` and the Godot WebSocket debug server
(port 9080, state snapshots + screenshots) with `agent_play_godot.py`
driving real recipe runs. This plan builds on those, not from scratch.)*

## Goals (Aaron, 2026-08-03)

1. **TUI and Godot builds remain in sync and have the same supported
   harness testability throughout.** *(Further goals slot in here if
   more arrive.)*

## The two load-bearing principles

### 1. Core-first, view-model out

Every demo feature lands as **Core state + logic + a presenter/view-model
projection**, and both frontends render *only* from the view-model:

- **`UiLedger`** (new, Core): which chrome exists right now (toolbar?
  breadcrumbs? action queue? minimap? dialogue box?), mutated only by
  gameplay triggers. Panels aren't frontend decisions anymore — chrome
  presence is *game state*. This is what makes progressive UI disclosure
  testable once, identically, on both frontends.
- **Dialogue queue**, **fullness-pip states**, **feature-slot states**,
  **clock readout** — same treatment: Core computes, frontends draw.

The harness asserts **at the view-model level first** (identical on both
builds by construction), plus a thin per-frontend render check (TUI:
buffer text via `FindText`; Godot: WebSocket state snapshot + screenshot
at checkpoints). Anything only assertable per-frontend is kept
deliberately shallow — "is it drawn" — never game logic.

### 2. The parity gate (the ratchet that keeps builds in sync)

The 30-minute journey becomes a **checkpoint script**: the progressive-UI
ledger's absent→present rows + the beat asserts from
[[target-demo-journey]], expressed as `GameKey` input sequences +
expected view-model states. One script, two drivers:

- **TUI driver** — headless `TuiTestHarness`/`FakeDriver` route.
- **Godot driver** — the WebSocket route (`agent_play_godot.py` pattern,
  generalized).

**A slice may not land unless the journey-so-far passes on BOTH
frontends.** That's the sync mechanism — not discipline, a gate. The
runner grows checkpoint-by-checkpoint with each slice, so drift is caught
the day it's introduced, never at demo time. `make parity` (or
equivalent) runs both drivers and diffs their view-model checkpoint
streams; CI-friendly, worker-runnable.

## Slices

Each slice = one worker session, landing Core + both frontends + its
journey-script checkpoints, gated on parity green. Order matters
(dependencies noted); estimates assume the Stage-3/4 substrate.

### Slice 0 — Parity baseline + journey runner *(the keystone)*
- Audit current TUI↔Godot drift (known: Godot inits Workshop-only;
  hotkey/back-button divergences to enumerate) and reconcile to one
  shared `GameInitializer` demo profile.
- Build **`journey_runner`**: pluggable driver (`--driver tui|godot`),
  script format = ordered steps `{keys | wait | assert-viewmodel |
  assert-render}`, checkpoint diff output. Fold
  `agent_play_godot.py`'s loop into it; add invariant pack (item
  conservation, stack validity, progressability) run after every step.
- Deliverable: a 2-minute smoke journey green on both drivers +
  a written drift report of anything reconciled.
- *Depends on: nothing. Everything depends on it.*

### Slice 1 — `UiLedger` (chrome-as-state)
- Core `UiLedger` + trigger events (first-pickup, first-enter,
  first-timed-action, compass-crafted, core-slotted…). Frontends render
  chrome conditionally from it. Start with today's always-on chrome
  flagged ledger-on, then flip defaults off for the demo profile.
- Journey checkpoints: all ledger rows assert absent→present exactly at
  their trigger (both drivers).

### Slice 2 — Dialogue box w/ portrait *(new UI surface)*
- Core: dialogue queue + **beat-keyed scripted trigger conditions**
  (deterministic counters/events per the ratified decision; no RNG, no
  cooldown machinery). Data-driven lines (markdown/data file, like item
  cards).
- TUI: bottom-third box, glyph portrait. Godot: Control-node box, rect
  portrait placeholder. Advance/dismiss on Primary in both.
- Checkpoints: frame-0 dialogue-only state; 3rd-unique-inspect trigger
  fires once; rapid-scan script cannot double-fire.

### Slice 3 — Fixed inventories: toolbar + hand, pickup routing
- Toolbar as a real bag with special binding (always-rendered, depth-
  invariant); pickup → first available toolbar slot; **bag-as-partial-
  empty acquire** (non-full bag counts as empty for placement, item then
  acquires *into* it).
- Checkpoints: toolbar-born-on-first-pickup (ledger), slot-fill order,
  depth-invariance standing assert, nested-acquire destination asserts,
  conservation across every step.

### Slice 4 — Look-in vs. enter; enter-only bags
- `C` peek overlay (one-deep for demo), cross-container move via
  overlay; **enter-only** bag property — peek fails with shake +
  dialogue hook (first-failed-peek is the only tell; no glyph).
- Checkpoints: overlay open/close leaves world untouched (golden diff);
  C-on-wilderness no-op + dialogue fired; E works where C won't.

### Slice 5 — The Shrine: feature slots, clock, the eye core
- Feature-slot CellFrame variant (glyph filter + locked-once-slotted);
  Menu (peekable bag of Start/Esc actions), Toolbar bag, **Clock**
  pre-slotted; **realtime mode** surfacing the clock readout via ledger.
- **Eye core → fullness pips game-wide** (Core computes pip state per
  bag; both frontends render on every bag cell — the first unlock that
  rewires rendering everywhere).
- Checkpoints: wrong-item rejection, Menu peek contents, clock Δt,
  pips absent at 3 earlier checkpoints → present on ALL bag cells after
  slotting, core non-removable.

### Slice 6 — Quiet 1 wilderness + finds
- Enter-only wilderness bag, **unnavigable tree cells** (cursor bump +
  feedback), seeded harvestables, ruin bag with note / **"another Quiet
  1" recipe** / eye core; **recipe-as-item + known-recipes registry**.
- Drop mechanism for the demo: **fixed placements** (mechanism 1+2 from
  the journey doc). The **conditional weighted loot table** (drop
  director: prereq-gated inclusion, known/present exclusion) is specced
  in the journey doc and deliberately deferred to its own post-demo
  slice.
- Checkpoints: tree cells refuse cursor (position unchanged), bump-N
  dialogue, seeded loot table, glyph header render on both frontends.

### Slice 7 — Crafting table, compass, minimap, the cliff
- Realtime timed craft at the table (action queue via ledger); **Quiet
  Compass → minimap** (zones-reached state → radar render both sides);
  axe recipe **present-but-uncraftable** in demo materials; bag +
  wilderness recipes both in-hand by journey end (three-path cliff).
- Checkpoints: queue absent→present on craft start, realtime progress
  via timed asserts, minimap wedge count, end-state recipe/absence
  asserts.

### Slice 8 — Full journey assembly + goldens
- Assemble the complete 30-minute script (all beats, both drivers);
  record golden view-model checkpoint stream + TUI buffer goldens +
  Godot screenshot set at every ledger row; wire `make parity` as the
  repo's standing demo gate. Visual pass on the screenshot set (vision-
  model prompts per agent-testing.md prong 3).
- Deliverable: the target demo, runnable + provable on both builds with
  one command each.

## Sync policy (standing, beyond this plan)

- **No slice lands touching only one frontend** (pure frontend bugfixes
  exempt, but they still run the parity gate).
- New chrome/panel work goes **through the `UiLedger`** — a panel that
  isn't ledger-driven is a review flag.
- Journey scripts are append-only history: a mechanic change that breaks
  an old checkpoint is a *decision* (update the script deliberately),
  never silent.
- Godot quirks (WSL2 etc.) stay documented in `design/godot-quirks.md`;
  the WebSocket driver runs against the Windows binary when WSL blocks
  (mouse-event limitation precedent).

## Worker notes

- Sequence: 0 → 1 → 2 → 3 → 4 → 5 → 6 → 7 → 8; slices 2–4 are
  parallelizable *in principle* but share `UiLedger` touchpoints —
  recommend serial with the one-worker-per-project convention anyway.
- Each brief inherits: MM-agnostic (this is pockets, no MM rules), TDD
  per repo CLAUDE.md, LINQ method syntax, data files human-editable,
  land on `main` per no-PR policy, parity gate green before push.
- The journey doc + this plan should be **mirrored into the repo's
  `design/`** (per its INDEX convention) by the Slice-0 worker, with
  the vault copies marked as source-of-truth pointers — keeps Claude
  Code sessions in-repo while Aaron reads on his phone.
