# Entropy — Pockets Design Context

> Working design document for the Entropy system in Pockets. Captures committed decisions, candidate mechanics, the brainstorm pool, and open questions. Intended for Claude Code subagents and human designers.

---

## 1. Overview

**Entropy** is the world's conflict and threat — a unifying antagonist that thematically maps onto the game's core motifs: fragmented space, organization, hoarding, mind palaces, memory, time, chaos, order, the unknown.

Entropy takes two forms:

- **Plaque** — buildup of entropy on a cell. Ambient, accumulative, background-computable.
- **Lints** — monsters formed from accumulated entropy. Active, observed-only, drop loot.

Entropy has many **flavors**, each with a distinct aesthetic identity and a distinct mechanical signature. Flavors are not interchangeable — Shadow plays nothing like Dust, both visually and mechanically.

### 1.1 Tonal Contract

Pockets is a **mostly chill** game with **opt-in existential horror**, analogous to Minecraft's relationship to the Nether and the End — those dimensions exist, they're meaningful endgame content, but the surface world is fully viable without ever engaging.

Reference points:

- **EVE Online high-sec** — high-sec is not a tutorial for null-sec; it's a complete game with its own progression and economy. Players who never leave high-sec are not playing "the easy version" — they're playing a different game with comparable depth.
- **Endfield** — player-initiated tower defense waves; failure means missing the reward, not double punishment.
- **Minecraft torches/Nether** — defense primitives are cheap, placeable, with predictable rules; horror dimensions are opt-in.

**The contract:** a player can run a deep, satisfying hoarding/farming/crafting game at low entropy indefinitely. Engagement with entropy mechanics is *always the player's choice*, motivated by reward differentiation rather than punishment for non-engagement.

---

## 2. Core Concepts

### 2.1 Effective Depth (ED)

```
EffectiveDepth = bag.Depth + bag.Entropy
```

- **Depth** — literal depth in the canonical Breadcrumb address; auto-updates if the bag is relocated.
- **Entropy** — a fixed base value, modifiable only via Reforging.

Effective Depth is the master scalar driving:

- Plaque generation rate
- Lint spawn rates and types
- Loot table selection
- Atmospheric / visual treatment

### 2.2 Ranks (Banded Effective Depth)

Effective Depth is mapped to discrete **ranks** (e.g., A: 1-5, B: 6-10, ...). Differences between ranks are **qualitative**, not just quantitative — each rank has its own tables for plaque flavors, spawn types, and loot drops, parameterized by exact ED within the band.

**Rank promotion requires a special event** — promotion is *not* automatic from accumulated ED. This is the load-bearing decision that protects the chill tone: a player can hover indefinitely within a rank without time pressure forcing escalation.

Likely promotion mechanism (deferred — see Q1): player-initiated tower defense wave (Endfield-style). Success grants temporary bonus + rank promotion option. Failure grants no bonus. Whether catastrophic failure has additional penalty is open (Q9).

### 2.3 Bag Metadata

| Field | Mutability | Description |
|---|---|---|
| `Depth` | Auto-updating | Literal depth from canonical Breadcrumb address |
| `Biomes` | Locked (Reforge) | Multi-tag; what tiles to use / what can spawn |
| `Orientation` | Locked (Reforge) | 3-bit enum: transpose, x-invert, y-invert (8 chiralities) |
| `Entropy` | Locked (Reforge) | Base entropy value; added to Depth for ED |
| `Specialization` | Locked (Reforge) | Tag(s) — inventory, automation, crafting, decoration, farming, looting, etc. |

### 2.4 Chirality / Orientation

8 orientations, encoded as a 3-bit enum (transpose, x-invert, y-invert). Each orientation defines, for a given bag:

- Cell-0 position (acquisition starting corner)
- Cell traversal order (chirality)
- Local "down" direction for plaque accumulation
- Outflow corner for cross-bag seepage

**Orientations are local only.** They do *not* compose hierarchically — a bag's display and behavior is computed from its own orientation alone, not from any parent chain. Nesting bags of different orientations creates **cross-boundary refraction**: the flow of plaque, acquired items, and outflow seepage shifts direction as it crosses a nesting boundary. This is intentional disorientation and a feature of deep wilderness networks.

See Q6 for player-facing bag chirality variability.

### 2.5 Coreward / Gravity

**Coreward** is both narrative direction and mechanical vector, defined as the inverse of Effective Depth — shallower bags are more coreward, deeper bags are further from the core.

- Implicit in every non-root bag
- Inverse of Acquire gravity (cells fill *away* from coreward)
- Cosmology: the player is the core; the wilderness is increasingly Other as ED grows

### 2.6 Root Bag

The root bag is bookkeeping rather than a real player-facing space — Unix `/` or `/dev` analog. It contains:

- The player's root inventory bag (the visible top of their network)
- The toolbar bag
- The hand bag
- The game settings bag

The hand bag has **pinned depth** and is therefore safe from entropy. Grabbing into the hand is always a clean operation — the hand is a contamination-free workspace.

---

## 3. Plaque

Plaque is the ambient form of entropy — buildup on individual cells within a bag. Plaque accumulates over time at a rate determined by:

- The bag's Effective Depth (and rank)
- The cell's position within the bag (per the chirality's local "down")
- The bag's biome tags
- Local defenses (see §5)

### 3.1 Simulation Primitives

Three distinct primitive models, each applicable to different entropy flavors:

| Primitive | Applies To | Behavior |
|---|---|---|
| **Corner accumulator** | Dust, Sand, Rust, Rime | Pressure ∝ inverse distance to sector-corner; sand-pile spread on overflow |
| **Edge creeper** | Shadow, Fog, Doubt, Quiet | Flood-fill from far-edges inward toward core; reversible by cleaning from the near-core edge |
| **Pure decay** | Wither, Rot, Decay, Mold | No flow; per-tick degradation on items/cells; positional-agnostic within bag |

A fourth class — **structural mutation** (Hollow, Collapse) — mutates the grid topology itself (cell loss, capacity reduction) rather than filling it. Treated as its own system.

### 3.2 Background Computation

When a bag is **closed**, plaque accumulation is computed in background as time-to-next-threshold-event (Dwarf Fortress-style fast-forward) rather than per-tick simulation. On open, any crossed thresholds resolve in order. This makes deep, sprawling inventory networks computationally cheap.

### 3.3 Cross-Bag Seepage

When a bag's plaque saturation exceeds a threshold, a **discrete seepage event** transfers plaque to the parent cell (i.e., the cell in the parent bag where this bag-as-item sits). Direction is determined by the *child bag's* outflow corner (per its orientation), then re-routed by the parent's accumulation rules.

Discrete-event model rather than continuous flow keeps the simulation cheap even with deep nesting. The "burp" of entropy into a parent cell is legible to the player.

### 3.4 Plaque as Resource

Plaque is **harvestable by cleaning** — removing it yields it as a refinable material. This is the foundational economic move that makes the chill tone work: the player is **gardening** entropy, not fleeing it.

- Cleaning verb = primary harvest (managed weeds with a bounty)
- Lint loot = secondary/implicit harvest (kill yields material + unique drops)
- Different plaque flavors refine into different material classes used in entropy-themed crafting, aesthetic blocks, or entropy-counter recipes

### 3.5 Plaque Effects on Facilities

Plaque is the **primary threat to closed bags and facilities**. It can:

- Halt production ticks
- Reduce maximum capacity
- Reduce visibility (Shadow / Fog flavors specifically)
- Damage stored items (Decay / Rot flavors)
- Affect local time speed (Dust / Sand / Rust)

Because the threat to closed bags is plaque-driven (not lint-driven), facilities in closed bags can be safely background-computed.

---

## 4. Lints

Lints are the **active monster form** of entropy — they exist only when a bag is **observed / open**. This is a Schrödinger-style observation model that protects the background-computation cheapness.

### 4.1 Banded Presence

Lint types and counts are determined by the bag's rank. Higher ranks introduce *new flavors* of lints, not just more of the same. Bigger lints with unique loot only appear at higher ranks — incentivizing high-ED engagement for late-game crafting materials.

### 4.2 Active Threat Model

Lints can attack the player, damage items, escape into other bags (a containment problem), and interrupt active facility operations. Their threat is **immediate and present**, in contrast to plaque's slow ambient pressure.

### 4.3 Spawn Determinism (Open — Q3)

When a bag is opened after long closure, lints spawn from accumulated plaque. The determinism model is TBD:

- **Deterministic** — seeded by bag ID + closed duration. Save-scum-proof but kills "what's in there this time" tension.
- **Fresh roll** — true Schrödinger reroll each open. Tension preserved, save-scummable.
- **Hybrid** — deterministic count/types, fresh placement.

---

## 5. Defense Primitives (Torch Families)

Four defense layers, each addressing a different scope:

| Layer | Family | Examples | Per-flavor signature |
|---|---|---|---|
| **Cell** | Fortification | CellFrame augmentations | Hardens individual cells |
| **Spatial** | Area Denial | Torches, turrets, censers | Radius-based prevention |
| **Boundary** | Barriers | Walls, moats, seals | Perimeter containment |
| **Container** | Bag Augmentation | Reforge bag for containment | Bag-level resistance / lint trap |

### 5.1 Asymmetric Coverage

Each entropy flavor has defenses at **approximately 3 of the 4 layers** — leaving one layer absent forces the player to engage with the canonical counter for that flavor. Example: Shadow may have no barrier-layer defense, requiring engagement with light-source area denial.

**Authoring constraint:** the 4×N matrix (defense layers × entropy flavors) should distribute the missing layers roughly evenly across columns, so no single defense primitive feels globally underpowered. See Q7.

---

## 6. Composition Rules

### 6.1 Biome × Rank Tables

A bag's spawn / plaque / loot tables are composed from `Biome × Rank`. A bag with multiple biome tags composes via:

1. **Union** — roll on each biome's table independently (always available)
2. **Cross-product** — if a `Biome_A × Biome_B` cross table is authored at the given rank, *also* roll on it (hybrid entries like "Crystal-Forest Wisp")

This degrades gracefully: where cross tables aren't authored, the system behaves as plain union. Authoring cross tables is high-effort and reserved for thematically rich combinations.

### 6.2 Specialization (Open — Q4)

Specialization is a tag like Biome. Open question: does Specialization modify spawn/loot tables alongside Biome (richer, doubles authoring), or affect only facility synergies and storage UX (cleaner)?

---

## 7. Discard Mechanics

Three discard paths, gated differently:

| Path | Gate | Properties |
|---|---|---|
| **Void** (Trash facility) | Facility unlock | Permanent destruction, no asset recovery |
| **Reforge** (Bag reforger) | Facility unlock | Asset preserved; reset Entropy or retag |
| **Litter** | Always available | Drop in world; ungated |

### 7.1 Litter Destiny (Open — Q2)

What happens to littered bags in the world is TBD:

- **Despawn** — clean, undercuts consequence
- **Persist forever** — Rimworld-style, on-theme but performance concern
- **Region-decay-when-unvisited** — litter in unvisited cells composts back into raw plaque/biome material (*recommendation*: world breathes, near-base mess accumulates, far-wilderness litter returns to the cycle)

---

## 8. Entropy Flavor Pool

The brainstorm pool. Final flavor selection should aim for distinct aesthetic AND mechanical identity per flavor — avoid two flavors that play the same.

### 8.1 Original

Dust, Doubt, Greed, Glow, Decay, Fray, Quiet, Fog, Shadow

### 8.2 In-Vein Extensions

Rust, Mold, Ash, Clutter, Hunger, Dread, Static, Rot, Mire, Haze, Hollow, Echo

### 8.3 Physics-Flavored (Quantum / Astro / Theoretical)

Flux, Void, Foam (quantum foam), Jitter, Blur, Rift, Eddy, Warp, Hiss (CMB), Tangle (entanglement), Bleed, Collapse, Null

### 8.4 Magical / Archaic / Obscure

Miasma, Wane, Gloam, Umbra, Rime, Hex, Wraith, Lethe (forgetting-river), Glamour (illusion-sense), Blight, Husk, Dreg, Pall, Wither

### 8.5 Candidate Mechanical Sketches

| Flavor | Sketch |
|---|---|
| Shadow / Doubt / Fog | Liquid creep from far edges; hides items until cleared |
| Dust / Sand / Rust | Corner accumulation; affects local time speed |
| Decay / Wither | Weakens structures, kills plants, damages items |
| Hollow / Collapse | Reduces maximum capacity |
| Glamour / Glow | Beneficial-looking but corrupts; players may want it for aesthetics |
| Echo / Lethe | Memory-coded; affects undo stack, item recognition |

---

## 9. Play Patterns

Three primary patterns, intended as **specialization options** with synergistic bag tagging:

1. **Fortify / Defend** — high-sec analog. Cheap defense primitives applied broadly. Stable production. Minimal entropy engagement. *Think: Minecraft torches in every corner.*
2. **Expand Quickly** — fast frontier expansion without spending on defense. Higher risk, neglect anxiety, more plaque accumulation, more production interruptions. *Think: sprawl-and-pay-later.*
3. **Venture / Farm** — deliberate engagement with high-ED bags. Tower defense waves, deep wilderness expeditions, purpose-built entropy farms (semi-automatable via chirality-funnel patterns — see §10.1). Yields entropy-themed loot and crafting materials. *Think: Minecraft mob farms.*

### 9.1 Aesthetic Cross-Cut

The **only** deliberate leak between patterns: a player wanting (e.g.) Shadow blocks for cosmetic purposes engages with the Shadow mechanic at minimum stakes — clean a Rank-A Shadow bag, harvest cosmetic outputs. Combat-flavored / mechanical outputs are reserved for higher engagement.

### 9.2 Parity Goals

- Pattern (1) is **not strictly less rewarding** than pattern (3) — they're peers with different reward profiles.
- Largest bag size is the **same** for all play styles.
- Opt-in has **no ratchet**: a player who experimented with high-ED bags can discard them and return to low-ED play with no permanent consequence. Bringing a high-ED bag coreward introduces risk that can be fortified against.

---

## 10. Emergent Patterns

### 10.1 Chirality-Driven Plaque Farms

The chirality + corner-accumulation system gives "flowing water" funneling for free. A high-Entropy bag with deliberate orientation funnels plaque to a known corner; placing a refining facility there yields a passive plaque-to-resource pipeline. The mob-farm pattern (Minecraft) emerges from already-specified mechanics — no new systems required.

Authoring question: support this with purpose-built farming-tagged bag templates as a craftable late-game goal, or let it emerge organically?

### 10.2 Cross-Boundary Flow Refraction

Because orientations are local and non-composing, plaque flowing coreward through a chain of bags with mismatched orientations traces a zigzag through the breadcrumb stack. Worth surfacing visually — a "flow ghost" overlay would sell the cosmology and provide a diagnostic for "this network is broken/weird."

---

## 11. Loot & Theme Curve

The item economy spans four progression tiers, with each tier holding ~10 representative loot items. The mix of **themes** (terrestrial, proto-elemental, magical/elemental, entropy) and the mix of **source verbs** (forage, mine, hunt, clean, craft) both shift with depth. This section documents the current target tuning so authoring stays coherent as new items are added.

### 11.1 Theme Definitions

- **Terrestrial** — familiar real-world materials (pine, oak, granite, salt, iron). Surface biomes, low ED. Anchors player familiarity in earliest tiers.
- **Proto-elemental** — terrestrial-with-a-glow bridge materials (Ember Coal, Quartz Shard). Read as natural but hint at the elemental layer.
- **Magical / Elemental** — fantastical materials in the Harvestella / Rune Factory / Atelier / FFXIV mode (Sunsteel, Moonpetal, Salamander Scale, Phoenix Cinder).
- **Entropy** — harvested via the cleaning verb or dropped by lints. Multiple flavors (Dust, Rust, Shadow, Mire, Hollow, Lethe, Foam, Echo, Void, Glamour) with breadth scaling by rank.

### 11.2 Tier 0 — Earliest Game (Rank A, surface, near root)

Composition target: **6 terrestrial / 2 proto-elemental / 0 magical / 2 entropy**.

Entropy is present in trace form from minute one — the cleaning verb is taught alongside foraging.

| # | Item | Category | Source | Theme |
|---|------|----------|--------|-------|
| 1 | Pine Cone | Material | forage | terrestrial |
| 2 | Oak Twig | Material | forage | terrestrial |
| 3 | Fern Frond | Material | forage | terrestrial |
| 4 | Granite Chip | Material | mine | terrestrial |
| 5 | River Pebble | Material | forage | terrestrial |
| 6 | Sea Salt | Reagent | scrape | terrestrial |
| 7 | Ember Coal | Material | mine | proto-elemental:fire |
| 8 | Quartz Shard | Material | mine | proto-elemental:crystal |
| 9 | Dust Mote | Material | clean | entropy:Dust (trace) |
| 10 | Rust Flake | Material | clean | entropy:Rust |

### 11.3 Tier 1 — Early Game (Rank B, frontier biomes)

Composition target: **3 terrestrial / 3 proto-elemental / 1 magical / 3 entropy**.

Magic peeks through. Entropy gains a second flavor presence.

| # | Item | Category | Source | Theme |
|---|------|----------|--------|-------|
| 1 | Hawthorn Berry | Reagent | forage | terrestrial |
| 2 | Wolf Pelt | Material | hunt | terrestrial |
| 3 | Wild Honey | Reagent | forage | terrestrial |
| 4 | Iron Nugget | Material | mine | proto-elemental |
| 5 | Birch Bark | Material | forage | proto-elemental |
| 6 | Spider Silk | Material | hunt | proto-elemental |
| 7 | Glowcap Spore | Reagent | forage | magical (first) |
| 8 | Ashfall Cinder | Material | clean | entropy:Ash |
| 9 | Mire Pearl | Material | clean | entropy:Mire |
| 10 | Fog Vial | Reagent | clean | entropy:Fog |

### 11.4 Tier 2 — Mid Game (Rank C–D, wilderness, real entropy farming)

Composition target: **0 terrestrial / 1 proto-elemental / 5 magical / 4 entropy**.

Elemental peak. Hunt verb starts producing named elemental-creature drops. First lint-loot.

| # | Item | Category | Source | Theme |
|---|------|----------|--------|-------|
| 1 | Cinderglass Shard | Material | mine | proto-elemental:fire |
| 2 | Sunsteel Ingot | Material | craft | elemental:fire |
| 3 | Moonpetal Bloom | Reagent | forage | magical |
| 4 | Salamander Scale | Material | hunt | elemental:fire |
| 5 | Sylph Down | Material | hunt | elemental:air |
| 6 | Frostvein Quartz | Material | mine | elemental:ice |
| 7 | Shadowsilk Thread | Material | lint | entropy:Shadow |
| 8 | Witchroot Tuber | Reagent | forage | entropy:Wither |
| 9 | Verdant Sapwood | Material | clean | entropy:Mold |
| 10 | Doubtcap Spore | Reagent | clean | entropy:Doubt |

### 11.5 Tier 3 — Late Game (Rank E+, deep wilderness, cosmic/memory)

Composition target: **0 terrestrial / 0 proto-elemental / 4 magical / 6 entropy**.

Cleaning verb dominates; boss-lint drops appear. Memory/forgetting/illusion themes activate. Entropy *breadth* expands rather than *intensity* — six distinct flavors, each with unique mechanical signature.

| # | Item | Category | Source | Theme |
|---|------|----------|--------|-------|
| 1 | Aether Filament | Material | lint | elemental:aether |
| 2 | Starfall Ingot | Material | mine | cosmic |
| 3 | Phoenix Cinder | Material | hunt (boss lint) | elemental:fire |
| 4 | Leviathan Scale | Material | hunt (boss lint) | elemental:water |
| 5 | Hollowglass Prism | Material | clean | entropy:Hollow |
| 6 | Lethean Tear | Reagent | clean | entropy:Lethe (forgetting) |
| 7 | Quantum Foam Vial | Reagent | clean | entropy:Foam (physics) |
| 8 | Echo Crystal | Material | lint | entropy:Echo (memory) |
| 9 | Voidsilk Skein | Material | lint | entropy:Void |
| 10 | Glamour Petal | Reagent | lint | entropy:Glamour (illusion) |

### 11.6 Composition Visualization

Each █ = 1 item out of 10. Rows sum to 10 across each tier.

```
                 Earliest    Early       Mid         Late
  Terrestrial    ██████      ███         ·           ·
  Proto-elem     ██          ███         █           ·
  Magic/elem     ·           █           █████       ████
  Entropy        ██          ███         ████        ██████
                 ──────      ──────      ──────      ──────
                 6/2/0/2     3/3/1/3     0/1/5/4     0/0/4/6
```

### 11.7 Trajectory of Each Theme (0–6 scale)

```
   6 ┤ T●                                      ●X
     │   ╲                                   ╱
   5 ┤    ╲                       ●M       ╱
     │     ╲                     ╱  ╲    ╱
   4 ┤      ╲                  ╱      ●M
     │       ╲               ╱      ╱
   3 ┤        ●T           ●X     ╱
     │       ╱ ╲         ╱      ╱
   2 ┤  P●─╱─── ╲────●X        
     │       ╲   ╲   ╱  ╲      
   1 ┤        ╲   ●M     ●P    
     │         ╲ ╱         ╲   
   0 ┤          ╳            ●T,P──────●T,P
     └──────────────────────────────────────
        Earliest    Early      Mid      Late

  T (terrestrial):   6 → 3 → 0 → 0    ↘   fades by mid
  P (proto-elem):    2 → 3 → 1 → 0    ∩   bridge tier
  M (magic/elem):    0 → 1 → 5 → 4    ∧   mid-peak, holds
  X (entropy):       2 → 3 → 4 → 6    ↗   monotonic ramp
```

### 11.8 Tuning Notes

- **Entropy is part of the world from minute one.** Earliest 2X is doing real work — the cleaning verb is taught alongside foraging, and cosmetic Dust/Rust outputs are reachable from Rank-A bags. Fits a "you arrived into an already-running entropy ecology" narrative (see Q10).
- **Proto-elemental as a true bridge.** Peaks at Early (3) and fades by Late (0). Ember Coal / Quartz Shard / Iron Nugget read as terrestrial-with-a-glow, then get replaced by Sunsteel / Frostvein once the player has crossed into elemental-proper. They are not retained as late-game crafting inputs.
- **Magic/elem peaks at Mid not Late** (5 → 4). Late-game gives that headroom back to entropy *variety*. Matches the design contract that higher ranks introduce new flavors, not bigger versions of mid-tier materials.
- **Entropy ramp 2→3→4→6.** The jump happens between Mid and Late, which is the right place for the rank-gated flavor explosion (Lethe / Hollow / Foam / Echo / Void / Glamour). Six distinct flavors at Late is the breadth ceiling — going higher would dilute mechanical identity per flavor.
- **Q11's "all creatures are lints" leaning fits this curve.** Salamander Scale and Phoenix Cinder living in the M column at Mid/Late are then just *less-corrupted lints* (or elemental-flavored lints) rather than a parallel wildlife system. One spawn pipeline, multiple corruption levels. Rime is the proof-of-concept flavor — already reads as both elemental (ice) and entropic (creep/decay).

### 11.9 Source-Verb Mix per Tier

| Tier | Forage | Mine | Hunt | Clean / Lint | Craft |
|---|---|---|---|---|---|
| Earliest | 4 | 4 | 0 | 2 | 0 |
| Early | 4 | 1 | 2 | 3 | 0 |
| Mid | 2 | 2 | 2 | 3 | 1 |
| Late | 0 | 1 | 2 | 7 | 0 |

Cleaning becomes the primary economic act at depth; hunt is reserved for boss-lint encounters in the late tier; crafted intermediates surface only at Mid where refining chains begin.

---

## 12. Open Questions

| ID | Question | Notes |
|---|---|---|
| Q1 | Rank promotion event specifics | Deferred until mechanic is partially implemented; likely player-initiated tower defense wave |
| Q2 | Litter destiny | Despawn / persist / region-decay; region-decay leaning |
| Q3 | Lint spawn determinism on bag open | Deterministic / fresh / hybrid |
| Q4 | Specialization tag effect on spawn tables | Synergy + UX only vs. spawn modifier |
| Q5 | Reforger recursion | Constraints on the reforger's containing bag; can reforger reforge its own container? |
| Q6 | Player bag chirality | Always standard vs. variable; standard-by-default with looted-as-found is candidate |
| Q7 | Per-entropy-flavor defense matrix | The 4×N grid; missing layer per flavor; column distribution |
| Q8 | Cultivation balance | Plaque farming intentional design strategy vs. needs damping |
| Q9 | Catastrophic wave-failure penalty | Whether failed promotion has any consequence beyond "no reward" |
| Q10 | Player arrival / opening | How did the player get to the pockets world? Affects tutorial progression, opening narrative, and how familiar/terrestrial the earliest-tier loot should feel |
| Q11 | All creatures as lints | Leaning: **yes**. If all wildlife is a form of lint, then magical/elemental drop-sources (Salamander, Sylph, Phoenix, etc.) are either themselves entropy variants or "less corrupted" cousins playing by similar rules. Fits flavors like Rime that read as elemental as much as entropic. Decision determines whether a separate wildlife system is authored or whether non-corrupting lints cover it |
| Q12 | Other inhabitants | Are there other "people" in the pockets world? If so, how did they arrive? Affects social/economic systems, quest-givers, and whether root-bag analogues exist for NPCs |
| Q13 | World topology | Is the pocket universe entirely self-contained with one entrance/root, or do some bags narratively have other exits / partially envelope other worlds (interdimensional subway system)? Affects cosmology of coreward and whether the breadcrumb chain is truly a tree |

---

## 13. Implementation Notes

- **Bag state model**: maintain immutable snapshot per bag (aligned with existing undo pattern). Plaque level is a per-cell field; lint roster is computed-on-open.
- **Background simulation**: store `last_observed_time` and `next_threshold_event_time` per closed bag. On open, fast-forward by computing how many threshold events occurred and resolving them in order.
- **Plaque seepage**: a threshold-crossing event publishes a `PlaqueSeep` message to the parent bag's accumulator; not a continuous fluid sim.
- **Chirality transforms**: a `Bag → (LocalCellIndex → DisplayCellIndex)` function determined by Orientation; applied at render time only. Internal state always uses canonical cell index. Orientations do NOT compose across parent chain.
- **ED computation**: Depth is computed lazily from breadcrumb chain; cached and invalidated on bag relocation.
- **Determinism (pending Q3)**: PRNG seeded by `(bag_id, world_seed, observation_epoch)` to support save-scum-resistant lint spawns if Q3 lands on deterministic or hybrid.
- **Discard hooks**: void/reforge are facility-gated state transitions; litter is just a drop operation with downstream world-level entropy accounting (pending Q2).

---

## 14. Glossary

- **Plaque** — passive entropy buildup on cells
- **Lint** — active entropy creature spawned from plaque, exists only when bag is observed
- **Effective Depth (ED)** — `Depth + Entropy`; the master scalar
- **Rank** — discrete band of ED with its own qualitative tables
- **Coreward** — direction of decreasing ED; toward the player/sanctuary
- **Chirality / Orientation** — 3-bit enum (transpose, x-invert, y-invert) determining a bag's cell-0, traversal order, and flow direction
- **Sector** — synonym for orientation (8 total)
- **Reforging** — facility-gated bag metadata mutation
- **Torch primitive** — any of four defense layers (Fortification, Area Denial, Barrier, Bag Augmentation)
- **Seepage** — discrete plaque-transfer event from child bag to parent cell
- **Root bag** — internal bookkeeping container holding player root, toolbar, hand, settings; the bag with no parent
- **Refraction** — change in flow direction at a nesting boundary between bags of different orientations
