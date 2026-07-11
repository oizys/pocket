# Depth-Recipe Progression (Navigation-is-Crafting Data Spine)

**Status:** Implemented (v1) — data + types + reachability + validation in
`Pockets.Core`, CLI + PNG tool in `tools/depth-recipes`.
**Phase:** Early-Mid (progression foundation). **Consumers:** mechanics
(progression/minimap), narrative (hero beats), design analysis.

## Concept

The cosmology capture (`~/obsid` §"Navigation = recursive crafting") says there is
**no connected overworld**: where the player *is* and where they *can get to* is not
traversal, it's **recursive crafting**. "Out" (deeper entropy) means **more nested**.
This slice turns that into the game's **data spine**:

- Reaching **zone×depth _n+1_** requires a recipe whose ingredients come from
  **depth _n_** of that zone (Quiet 2 ← Quiet 1 materials).
- The **negative sub-zone extends its + sibling's chain**: Quiet+ runs depths 1..10,
  then **Bloom (Quiet−) continues at 11**, whose recipe draws from **Quiet 10** — the
  cosmology's cited boundary, encoded exactly.
- Moving **around the circle** is **semi-linearized** by cross-zone edges: e.g. a
  **Quiet 5 material is required to craft Gloam 1**.
- **Hero-piece gates** are special recipes that unlock **deterministic** wilderness
  maps (vs. the random wildernesses at ordinary nodes): **Quiet 3 parts → Barrenhold**,
  an old city that hosts a story beat.

## Cohesion

Everything derives from the **`EntropyMatrix` SSOT** — the 8+4 zone/chirality/flavor
table that assets, narrative, and mechanics already share. This slice adds **no new
zone data**; a `ZoneDepth` is `(Zone, Depth)` and its material's flavor noun comes
from the matrix's `Flavor`. The same `Orientation`/chirality that flips the glyphs is
what "negatives extend + siblings" leans on. One source of truth, now with a depth
axis and a recipe graph over it.

## Intuition

A radial **nested-circle** progression map (see the generated PNG): Core at center,
four quadrant arms fanning into their screen corners, **depth = radius** (deeper =
more nested). Players read it as "climb your ladder, and mid-ladder you unlock the
next quadrant's door." The reachability frontier is exactly the **radar minimap's**
future data source.

## Architecture

```
src/Pockets.Core/Cosmology/Recipes/
  ZoneDepth.cs         (Zone, Depth) node — depth numbered continuously across +/−   <-- the atom
  Material.cs          Material (Id, Name, ZoneDepth Source) + Ingredient (Material, Qty)
  DepthRecipe.cs       recipe unlocking one node; RecipeKind = Root | WithinZone | Entry
  CrossZoneEdge.cs     TUNABLE semi-linearization dependency (Source node → Target node)
  HeroGate.cs          first-class gate (Requires + StoryBeat) — BeatType = Character|Boss|Relic
  QuadrantChain.cs     (Quadrant, +depths, −depths) → the linear ladder; MaterialAt(node)
  RecipeBook.cs        Assemble(chains, edges, gates) → the immutable graph            <-- queries
  DepthRecipeData.cs   the STARTER dataset (tunable knobs) → RecipeBook                <-- the data
  ProgressionState.cs  reached-node set (materials held = signatures of reached nodes)
  Reachability.cs      pure Frontier / Project (closure) / IsCraftable / IsUnlockable
  RecipeValidation.cs  no-orphans / no-cycles / all-reachable / chirality-extension checks

tools/depth-recipes/    console app (SkiaSharp) — NOT in Pockets.sln
  Program.cs            `frontier` | `diagram` | `validate` subcommands
  FrontierPrinter.cs    the progression-frontier printout (design-analysis face of Reachability)
  ProgressionDiagram.cs the labeled radial cosmology PNG (nodes, edges, gates, 12 glyphs)
  RepoLocator.cs        finds repo root      generate.sh   entry point
```

**Why the split (same posture as `tools/glyph-gen`):** the game owns the data,
reachability, and validation with **zero external dependencies** (pure, fully
unit-tested, Godot-consumable). Only the PNG needs a rasterizer, so that lives in an
out-of-solution tool. `dotnet build Pockets.sln` / `dotnet test` never pull SkiaSharp.

### Recipe-chain schema (within-zone)

A `QuadrantChain(Quadrant, PositiveDepths, NegativeDepths)` is one **linear ladder**
of depths `1..(P+N)`. The first `P` are the "+" zone; the rest are the "−" zone,
**continuing the numbering**. `RecipeBook.Assemble` derives each node's recipe
**structurally**, which makes the graph orphan-free and acyclic *by construction*:

| Node | Recipe |
|---|---|
| depth 1, no incoming edge | **Root** — free world-start, no recipe |
| depth 1, gated | **Entry** — ingredients are the cross-zone edge materials only |
| depth _d ≥ 2_ | **WithinZone** — spine ingredient from depth _d-1_ (same chain), plus any edges targeting it |

The boundary node (depth `P+1`, the first "−" node) automatically draws its spine
from depth `P` (the last "+" node) — **Bloom 11 ← Quiet 10** falls out for free.

### Cross-zone semi-linearization edges (TUNABLE)

`CrossZoneEdge(Source, Target, Quantity, Note)` adds a dependency: crafting `Target`
also needs a material from `Source` in another quadrant. Starter set sequences the
circle **clockwise** (Quiet → Gloam → Flux → Jitter):

| Edge | Meaning |
|---|---|
| Gloam 1 ← Quiet 5 | cosmology's cited example |
| Flux 1 ← Gloam 3 | Flux opens once Gloam is mid-explored |
| Jitter 1 ← Flux 3 | Jitter (chaos) opens last |

The **source depth on each edge is a pacing knob** — raise it to demand deeper
progress in a predecessor before the next quadrant opens.

### Hero-piece gates

`HeroGate(Id, Name, Requires, Beat)` hangs off the graph as its own unlockable, not a
`ZoneDepth` node. `Beat` is the narrative metadata **slot** (title, one-line summary,
`BeatType`). Starter gates: **Barrenhold** (3× Quiet-3 parts → a ruined city holding
a living survivor) and **The Sunken Beacon** (2× deep-Gloam Glow → a boss).

### Reachability semantics

`Reachability` is a **pure crafting closure** over a `ProgressionState` (the set of
reached wildernesses; materials held = the signatures of reached nodes):

- `IsCraftable(node)` — not yet reached and every ingredient **source** reached.
- `Frontier(state)` — the **immediate** craftable next nodes (one step away); this is
  what the CLI prints and the minimap will highlight.
- `Project(state)` — the monotone **fixpoint**: everything eventually reachable, plus
  the hero gates that closure unlocks.

Gating shows up in the *frontier*, not the closure: from the world start the closure
still climbs the whole Quiet chain and thus reaches everything, but Gloam 1 only
enters the *frontier* once a Quiet-5 material is actually in hand.

## Tunable-data posture

`DepthRecipeData` is **design data, not final balance.** Depth counts, edge
source-depths, and gate quantities are pacing knobs, flagged inline and on the
diagram. The **only pinned value is Quiet+ = 10**, which realizes the cosmology's
cited Bloom-11 ← Quiet-10 boundary. Balance passes retune the numbers; the schema and
the validation invariants stay fixed.

## Tooling

```bash
tools/depth-recipes/generate.sh frontier                         # frontier from world start
tools/depth-recipes/generate.sh frontier --reached quiet+5,gloam+1
tools/depth-recipes/generate.sh diagram                          # PNG → vault
tools/depth-recipes/generate.sh validate                         # integrity checks
```

Node syntax: `quiet+5` / `gloam-7` shorthand, or the canonical key `quiet-positive:5`.
The diagram writes `~/obsid/paths/projects/pockets/assets/depth-recipes-v1.png`
(labeled, phone-readable; the 8+4 entropy glyphs are placed on the plate).

## Methodology fit

TDD: the schema shape, reachability behavior, and validation invariants were specified
as tests (`tests/…/Cosmology/Recipes/`) — including a **property-style** monotonicity
check (random `A ⊆ B` states over the real graph, seeded) and the **chirality-extension
property** asserted per quadrant. Data-as-source-of-truth and immutable records match
the repo house style; nothing duplicates the `EntropyMatrix` SSOT.

## Reference — constraint vocabulary for future generation

The wilderness *contents* behind each node (and the deterministic hero maps) are a
**constraint-based level-generation** problem. The reference is **G-PCGRL**
(*Graph-based Procedural Content Generation via Reinforcement Learning*,
arXiv:2407.10483), which frames PCG as editing a **graph of typed nodes and
relational constraints** — a direct fit for this recipe/reachability graph as the
constraint scaffold that future generation fills in. Generation itself is **out of
scope** here; this slice establishes the deterministic progression graph and the
reachability projection those generators will target.

## Status / next

- [x] Recipe-chain schema + starter data (EntropyMatrix-derived, no duplication).
- [x] Cross-zone semi-linearization edges (tunable) + hero-piece gates.
- [x] Pure reachability projection (frontier + closure) with CLI frontier printout.
- [x] Validation as tests: no orphans/cycles, all reachable, chirality-extension,
  reachability monotonicity (property-style).
- [x] Labeled radial progression-graph PNG to vault (glyphs placed = bonus).
- [ ] Aaron review of the v1 diagram → confirm depth counts / edge pacing feel right.
- [ ] Wire `ProgressionState` to the radar minimap (this is its data source).
- [ ] G-PCGRL-style content generation behind each node (future slice).
