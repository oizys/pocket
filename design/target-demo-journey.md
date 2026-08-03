> **Source of truth: this repo copy.** Mirrored 2026-08-03 by the Slice-0 parity worker
> from `~/obsid/paths/projects/pockets/target-demo-journey.md`. The vault copy is **Aaron's reading copy**
> (phone/Obsidian); the repo copy is **canonical for Claude Code sessions** going forward.
> Edits that matter to implementation land here first. See
> [parity-drift-report.md](parity-drift-report.md) and [INDEX.md](INDEX.md).

---

# Pockets — Target Demo Journey (unified draft)

*(PM draft 2026-08-03, synthesized per Aaron's direction from
[[user-journeys-first-30min]]: **J3 base + J1 bits**, more time in the
weirdness, **progressive disclosure of mechanics AND UI**, self-dialogue
box w/ portrait as secondary narrative, and the 8 captured beats —
amnesia, fixed toolbar/hand, look-in vs be-inside, enter-only wilderness,
bag-acquire semantics, verbs-by-absence, the Shrine, compass + axe-recipe
ending. Where a beat left an open question I've made a **[PROPOSAL]** and
flagged it — every one is reversible in synthesis. Harness hooks assume
the Stage-3 TUI substrate + seeded fixtures; every UI materialization is
an assertable absent→present transition.)*

**Spine in one line:** wake with nothing — not even UI — and let curiosity
pull verbs, panels, and memory into existence, one noticed absence at a
time, ending with a compass, an axe recipe, and one Shrine slot filled.

**The four narrative surfaces** (kept distinct on purpose):
- **Dialogue box w/ portrait** *(NEW UI)* — the character's voice: memory
  fragments, noticing absences, self-talk. Modal-lite: bottom-third
  overlay, portrait left, advance/dismiss on Primary. Replaces the
  status-line narrator everywhere.
- **Description pane** — the world's voice: item cards, invitation to
  explore.
- **Notes/scraps** — the dead's voice: found writing, glyphs, recipes.
- **The Shrine** — the *game's* voice: features as physical objects.

---

## Progressive UI ledger (what exists when)

| Time | UI element | Materializes because |
|---|---|---|
| 0:00 | Dialogue box (portrait) | First thing ever seen — before the world |
| 0:45 | Grid + cursor | Vision clears; the room *is* an inventory |
| 1:30 | Description pane | First cursor-rest on an item |
| ~4:00 | Toolbar bar | First pickup flies to toolbar slot 1 |
| ~7:00 | Look-in overlay | First chest peek (C) |
| ~9:00 | Breadcrumb line | First *entering* (crossing the threshold) |
| ~12:00 | Shrine slots view | Entering the Shrine (explains Menu/Toolbar/Clock retroactively) |
| ~15:00 | Clock readout (top bar) | Noticed at the Shrine — was always ticking (realtime mode) |
| ~18:00 | Action queue panel | First timed action (crafting table) |
| ~26:00 | Minimap / radar | Quiet Compass crafted |
| ~28:00 | Bag fullness pips (game-wide) | Eye core slotted at the Shrine |

Harness rule: each row is a checkpoint — buffer-grab asserts the element
is ABSENT in the frame before its trigger action and PRESENT after.
Nothing else may summon it early (regression tests hammer this).

---

## 0:00–1:30 — Black, then a voice

- **UI moment**: black screen. The **dialogue box** fades in alone —
  portrait (groggy, eyes half-shut), text: *"…cold. Was I reaching for
  something?"* Advance on Primary (the input's first job is turning
  pages, before movement exists). Two lines max, then the box drops and
  the world fades in dim: an **8×4 room-grid** — mostly empty, 5–6
  scattered items, palette near-monochrome. No toolbar, no breadcrumbs,
  no panels. Just a grid and a cursor glow.
- **Start-inventory [RATIFIED 08-03]**: the player starts **inside an
  8×4 "home" inventory** — the antechamber of the hole. Rationale: beat 2
  says teach *be-inside* and *look-in* as separate ideas; starting
  *inside* makes "you are in an inventory" the ground truth from frame
  one, and look-in becomes the *learned* trick (via the chest at ~7:00) —
  the reverse of genre convention, which is exactly the weirdness budget
  spent right. (Alternatives: start in the Toolbar → too abstract before
  the toolbar exists as UI; start in a wilderness → burns the enter-only
  reveal.)
- **Input**: Primary to advance dialogue; then WASD/d-pad moves the
  cursor (wrap-around discoverable).
- **Mechanics**: cursor movement, dialogue advance/dismiss.
- **Harness**: frame-0 buffer-grab = dialogue box only (no grid glyphs);
  post-fade grab = grid, no toolbar row; scripted 40-key wander asserts
  wrap + no crash with zero UI chrome.

## 1:30–4:00 — Things have names (description pane)

- **UI moment**: cursor rests on a **Guttering Lantern** → the
  **description pane materializes** below the grid, card in the prior
  owner's voice. Each of the 5–6 items is a small worldbuilding bite
  (Bone Chips, Dry Grass, a **Small Bag**, a **Chest**, a **Crafting
  Table** — the latter two inert-looking "furniture" items).
- **Input**: pure cursor browsing. No verb yet but *look*.
- **Dialogue beat**: after the third card, the box returns, one line:
  *"I know what these are. Why don't I know where I am?"* (amnesia
  scaffold: knowledge of things, loss of self.)
- **Mechanics**: focus-follow description; dialogue triggers are
  **beat-keyed scripted conditions [RATIFIED 08-03]** — deterministic
  event/counter conditions, never random, no cooldown machinery (the
  script conditions themselves prevent spam).
- **Harness**: description text equals data-file card per item; dialogue
  trigger fires exactly on 3rd unique inspect (condition asserted by
  rapid-scan script — no double-fire).

## 4:00–7:00 — Pick something up (the Toolbar is born)

- **Input**: Primary on the Lantern (the description invites: "it wants
  carrying").
- **UI moment**: the item lifts and flies **down** — and the **toolbar
  bar materializes** along the bottom to *catch it* in slot 1. This is
  beat 3 made visible: **pickup goes to the first available toolbar
  slot**, and the toolbar is a **real inventory** (its slots are cells,
  not shortcuts). The player picks up 2 more things and watches slots
  1→2→3 fill left-to-right.
- **Dialogue beat**: *"That… bar. That's mine. That's the one thing that
  feels normal."* (Marks toolbar+hand as the FIXED inventories — always
  present at any depth — without a tutorial sentence.)
- **Mechanics**: pickup-to-toolbar routing, toolbar as real cells, hand
  reserved (not yet taught).
- **Harness**: toolbar absent → present transition on first pickup;
  slot-fill order asserted; toolbar persists across every later
  depth-change frame (standing assert for the rest of the run).

## 7:00–9:30 — Look in, don't fall in (the Chest)

- **Input**: cursor to the Chest, **C** (peek).
- **UI moment**: a **look-in overlay** opens — the chest's 4×2 grid
  floats over the room, room still visible dimmed behind. Player moves
  a Rope Coil from chest → toolbar (cross-container move *without going
  anywhere*). Close overlay (Q or C again).
- **Teach (beat 2's pair, first half)**: *you can look into and arrange
  an inventory without being inside it.*
- **Mechanics**: look-in overlay, cross-container transfer via toolbar,
  overlay stacking rules (one deep for the demo).
- **Harness**: overlay open/close leaves room state untouched (golden
  diff = only chest/toolbar rows); item conservation across the move;
  input map inside overlay asserted (no cursor leak to the room grid).

## 9:30–12:00 — Fall in on purpose (entering, breadcrumbs)

- **Input**: cursor to the **doorway cell** on the room's edge —
  description: *"A low arch. You could fit."* — **E** (enter).
- **UI moment**: the room slides away; a **breadcrumb line materializes**
  above the grid: `Home > Passage`. The Passage is a thin 2×6 connective
  bag (three items, one more doorway) whose real job is teaching **Q**
  (back) against a place too boring to stay in.
- **Teach (beat 2's pair, second half)**: *you can be inside every
  inventory — and inside is how you go deeper.* Dialogue: *"Rooms inside
  rooms. Fine. That's fine."*
- **Mechanics**: enter/back, breadcrumb push/pop, cursor-restore on pop.
- **Harness**: breadcrumb absent→present on first E; E/Q cycle ×4
  restores cursor exactly (stack assert); toolbar unchanged across depth.

## 12:00–15:30 — The Shrine (the game explains its own UI)

- **UI moment**: through the Passage: the **Shrine** — a small stone
  room-grid where 3 cells are **glyph-framed feature slots**, already
  filled: **Menu** (a bag containing the Start/Esc actions — peekable
  with C!), the **Toolbar bag** (portrait of the bar the player just
  watched be born), and a **Clock, ticking** — at which the **top-bar
  clock readout materializes** ("it was always running" — the game
  started in **realtime mode**). One slot is **empty**, its glyph frame
  glowing faintly: a shape the player hasn't seen yet.
- **Dialogue beat** (the thesis line): *"Someone built a room where the
  rules live. …Someone built the rules."* — **curiosity unveils
  structure.**
- **Rules (per beat 8)**: only the matching-glyph item fits a slot; once
  slotted, **cannot be removed**. Peeking the Menu bag is allowed and is
  the demo's sanctioned "what do these meta-bags even be" moment.
- **Mechanics**: feature-slot CellFrames (glyph-filter variant of
  input-slot filters), slot-locked state, Menu-as-bag, realtime clock
  surfacing.
- **Harness**: wrong-item into feature slot rejected (filter language
  reused from Stage-3 frames); Menu peek lists exactly the Start/Esc
  actions; clock readout absent before Shrine entry, present + advancing
  after (two grabs, Δt).

## 15:30–21:00 — The Wilderness (enter-only; verbs by absence)

- **UI moment**: back home (breadcrumbs exercised naturally), the **Small
  Bag** from the floor: its description resists — *"Dark inside. No
  bottom that you can see."* — **C does nothing** (a soft error shake +
  dialogue: *"Can't just peek at this one."*). **Enter-only** is taught
  by the peek FAILING. E → **Quiet 1 wilderness**: pale Dust palette,
  the **Quiet+ glyph** large on the environment header, **trees as
  unnavigable squares** (cursor refuses; bump feedback), sticks, rocks
  scattered; deeper in, a **ruin bag** holding: a **note** (half-
  pictographic scrap), a **recipe — "another Quiet 1"** (make more
  wilderness), and a **glyphed core** whose glyph matches the Shrine's
  empty slot — the **eye core** *(labeled only by shape; the player
  doesn't know what it does yet)*.
- **Verbs by absence**: player bumps a tree repeatedly → dialogue:
  *"Solid. If I had an axe— …did I used to have an axe?"* (plants the
  axe + an amnesia thread in one line). The second felt absence —
  can't-see-fullness — arrives in the next beat's capacity juggling
  (*"Which one has space? I'd have to look inside every single one."*),
  while the eye core rides unexplained in the toolbar.
- **Input**: harvest sticks/rocks (Primary per-item), bump trees, open
  ruin bag (C works on ruin bags — only *wilderness* bags resist peeking;
  **the first failed peek is the only tell [RATIFIED 08-03]** — no
  visible glyph in-demo. The eyelid-shut frame + a "see enter-only
  property" reveal are banked as **future Shrine feature unlocks**,
  outside the 30), carry finds via toolbar.
- **Mechanics**: enter-only bag property + failed-peek affordance,
  unnavigable cells, harvest, found-recipe items, core items.
- **Harness**: C-on-wilderness = no-op + shake asserted; tree cells
  reject cursor entry (position unchanged); ruin loot from seeded table;
  bump-counter dialogue fires at N=3 exactly.

## 21:00–24:00 — Prepping the trip (bags in bags; toolbar capacity)

- **Input**: hauling exceeds toolbar space → friction. Player (or hint)
  puts the **ruin's small bag INTO a toolbar slot**: the toolbar now has
  **more carrying capacity** — further pickups route per beat 5's
  acquire rule: **a non-full bag counts as an empty space for placement,
  then the item acquires INTO the bag**.
- **Hint fallback (beat 5's ask)**: if the player hasn't nested a bag by
  **23:00**, dialogue offers: *"The bag's not full. The bag is… also a
  space?"* — **single soft hint, dialogue-only, no escalation [RATIFIED
  08-03]**. (A bag **"fullness flag"** is banked as a valuable future
  Shrine feature glyph — seeing fullness at a glance is *earned*, later.)
- **Dialogue beat**: first bags-in-bags moment lands the memory fragment
  attached to *nesting*: *"—reaching into a bag that didn't end. That's
  the last thing. That's the LAST thing."* (Beat 1's blackout memory,
  delivered at the mechanically-rhyming moment.)
- **Mechanics**: bag-in-toolbar-slot placement semantics, acquire-into-
  bag routing, hint timer.
- **Harness**: scripted overflow pickup routes into toolbar-bag per rule
  (assert destination cell inside nested bag); hint fires at 23:00 only
  if nesting-count == 0; memory-fragment dialogue keyed to first nest
  event, not time.

## 24:00–28:00 — The Crafting Table (action queue; the Compass)

- **Input**: home again; drop sticks + rocks + Dry Grass at the
  **Crafting Table** (1×3, input/output frames — Stage-3 facility
  pattern, J1's surviving bit). First timed craft: the **action queue
  panel materializes** to show the work happening (progress ticks in
  realtime — the Clock matters now).
- **UI moment**: output: the **Quiet Compass** → on pickup, the
  **minimap/radar materializes**: Core dot, Quiet wedge faintly lit, 11
  dark. The found **axe recipe** card sits readable in the toolbar —
  named price in Quiet-1 materials, **not craftable within the demo's
  remaining minutes on purpose**.
- **Mechanics**: facility recipe (compass), realtime tick progress,
  minimap state from zones-reached, recipe-as-item.
- **Harness**: action queue absent→present on craft start; realtime
  progress asserted via timed grabs; minimap wedge count == 1; axe
  recipe present + axe absent in end-state.

## 28:00–30:00 — One slot filled: **the Eye of Fullness [RATIFIED 08-03]**

- **Setup shift**: the core found in the wilderness ruin (15:30 beat) is
  an **eye-glyphed core**, matching the Shrine's glowing empty slot. The
  absence it answers was *felt* at 21:00–24:00: juggling trip capacity,
  the player couldn't tell which bag had room without opening it —
  dialogue there notes it: *"Which one has space? I can't— I'd have to
  look inside every single one."*
- **Input**: carry the eye core to the Shrine, slot it. **Click** —
  irreversible.
- **UI moment**: the **bag fullness flag** materializes game-wide — every
  bag item now renders a small fullness pip (empty / partial / full) on
  its cell. The player *sees* their whole carrying situation at a
  glance for the first time. Dialogue: *"…Oh. Now I can see."* The demo
  card rises over the radar: home lit, Quiet 1 known, an axe recipe
  waiting, ten dark wedges.
- **Why fullness, not Sort** (Aaron's call): by :28 there isn't enough
  mess to *need* sorting — but capacity was fought minutes ago, so this
  unlock lands on a felt need. **Sort stays a found feature for later**
  (hour 2+, when the hoard is real) and keeps its keeper line: *"Order.
  There you are."*
- **Mechanics**: feature-slot unlock → game-wide render change (first
  proof that slotting rewires the UI everywhere, not just adds a
  button).
- **Harness**: fullness pips absent on every bag render before slotting
  (asserted at 3 earlier checkpoints), present on ALL bag cells after
  (home, toolbar, nested); slotted core non-removable; pip states
  correct against known contents (empty/partial/full fixtures); final
  golden snapshot + progressive-UI ledger fully green.

---

## Decisions (Aaron, 2026-08-03)

1. **Start-inventory: RATIFIED** — the 8×4 home antechamber.
2. **Dialogue triggers: RATIFIED** — beat-keyed scripted conditions
   (deterministic; no cooldown machinery — the conditions themselves
   prevent spam). Memory fragments stay keyed to mechanically-rhyming
   events (nesting → blackout), not timestamps.
3. **Enter-only: RATIFIED** — first failed peek is the only tell in-demo.
   Eyelid-shut frame glyph + property-visibility banked as future Shrine
   feature unlocks.
4. **Closing unlock: RATIFIED (follow-up, same day)** — the **fullness
   flag** slots in-demo (the eye core; capacity was the felt need). Sort
   stays a later found feature and keeps *"Order. There you are."* as
   keeper text. Shrine long-term vision captured in
   [[shrine-of-features]].
5. **Hint: RATIFIED** — single soft dialogue hint, no escalation. Bag
   **fullness flag** banked as a later feature glyph.
6. **Axe cliff: RATIFIED + expanded** — see below.

## The 30-minute cliff: three-path self-determination (Aaron, 08-03)

At minute 30 the player should feel **choice / self-determination**, with
a viable next move for each temperament — all three legible from what the
demo already showed:

- **Gatherer-achiever** — make the axe, chop the trees.
- **Explorer** — make + enter more wilderness (the "another Quiet 1"
  recipe from the ruin).
- **Organizer-achiever** — make more bags and stack them (capacity as
  its own reward).

**Recipe availability has to support all three.** Mechanism options
(Aaron, verbatim-distilled — can combine):

1. **Assembler starts with some known recipes** — the player can try to
   build what is already known.
2. **A few fixed extra recipe drops** placed during the 30.
3. **Weighted-with-conditions loot tables** on wilderness gen: loot-table
   entries carry **inclusion conditions** — an early recipe's entry can
   *require* previous "tech-tree" entries (only drops once its
   prerequisites are known) and be **excluded once the recipe is known
   or the item already exists in the generated space**. Makes specific
   early recipes *extremely likely* exactly when they're relevant, and
   never redundant.

Option 3 is a real system spec (conditional weighted tables ≈ a drop
director) — likely the durable one; 1+2 can fake it for the demo build's
first slice. The bag recipe (organizer path) and the wilderness recipe
(explorer path) must both be in hand by 30:00 under whichever mechanism.
