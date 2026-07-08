# Entropy Glyph Generator (8+4 Symbol Matrix)

**Status:** Implemented (v1) — data + geometry + SVG in `Pockets.Core`, PNG tool in `tools/glyph-gen`.
**Phase:** Early (asset foundation). **Consumers:** asset generation, narrative, mechanics.

## Concept

The entropy cosmology (see `~/obsid` cosmology capture and
[entropy-design-context.md](entropy-design-context.md)) has a **Golden-Dawn-style
elemental matrix**: 4 quadrants around a Core, each splitting into a positive and
negative sub-zone (8 total), with a distinct chirality per sub-zone. This slice
turns that matrix into **deterministic, Godot-ready vector glyphs**:

- **8 basis icons** — the 8 flips of the "sort by ascending" staircase (long top
  line, medium middle, short bottom, all left-aligned). A zone's chirality is the
  *reading direction* of its flip.
- **4 parent icons** — the 4 flips of a "wifi rainbow" (concentric quarter-arcs)
  that hugs each quadrant's corner, motivated as connecting the aligned edges of
  the quadrant's two children.

## Cohesion

The glyph orientation **is** the bag `Orientation` from entropy-design §2.4 — the
same 3-bit enum `(Transpose, XInvert, YInvert)` that drives cell-0 corner,
traversal chirality, plaque "down", and outflow. One geometric source of truth,
three consumers. The zone↔flip table lives in `EntropyMatrix` as data, so
narrative (flavor names) and mechanics (orientation) read from the same rows the
assets do.

## The Matrix (single source of truth: `EntropyMatrix`)

Chirality = `(Primary, Secondary)` reading pair = (direction strokes point,
direction of length progression). The `+` aspect's primary points into its
quadrant's screen corner; the `−` aspect is the **axis-transpose** of its `+`
sibling (reading pair reversed). Orientation code = `Transpose·4 + XInvert·2 + YInvert·1`.

| Zone | Quadrant (corner) | Aspect | Flavor | Chirality | (T,X,Y) | Code |
|---|---|---|---|---|---|---|
| Quiet + | Quiet (bottom-right) | + | Dust/Death | Right-Down | (0,0,0) | 0 |
| Quiet − | Quiet | − | Blight/Bloom | Down-Right | (1,0,0) | 4 |
| Gloam + | Gloam (bottom-left) | + | Shadow | Left-Down | (0,1,0) | 2 |
| Gloam − | Gloam | − | Glow | Down-Left | (1,0,1) | 5 |
| Flux + | Flux (top-left) | + | Ash | Left-Up | (0,1,1) | 3 |
| Flux − | Flux | − | Rime | Up-Left | (1,1,1) | 7 |
| Jitter + | Jitter (top-right) | + | Static | Right-Up | (0,0,1) | 1 |
| Jitter − | Jitter | − | Void | Up-Right | (1,1,0) | 6 |

The 4 **parent** glyphs share their quadrant's `+` orientation (Quiet=0, Gloam=2,
Flux=3, Jitter=1), so each rainbow opens from its quadrant corner toward the Core.

The math (8 distinct orientations, transpose = D4 axis-swap, `+` points into
corner, culture-invariant determinism) is verified in
`tests/Pockets.Core.Tests/Cosmology/`.

## Architecture

```
src/Pockets.Core/Cosmology/
  Direction.cs        Direction enum + unit-vector mapping (x-right / y-down)
  Orientation.cs      3-bit D4 chirality; Apply(), Reading(), Transposed(), Determinant
  Zone.cs             Quadrant / Aspect / Zone enums
  EntropyMatrix.cs    the DATA table (ZoneInfo rows) + quadrant lookups     <-- source of truth
  Glyphs/
    GlyphParams.cs    the parameter knobs (record with defaults)
    Primitives.cs     Pt / Segment / Arc + orientation transform
    GlyphGeometry.cs  canonical basis + parent shapes, flipped by orientation
    SvgEmitter.cs     deterministic, fill-none, stroke-based SVG strings
    GlyphCatalog.cs   All(params) -> the ordered 12 GlyphSpecs

tools/glyph-gen/       console app (SkiaSharp) — NOT in Pockets.sln
  Program.cs           writes 12 SVGs + contact sheet
  ContactSheet.cs      light+dark labeled PNG via Skia (draws the same primitives)
  RepoLocator.cs       finds repo root
  generate.sh          entry point
```

**Why the split:** the game owns the data/geometry/SVG with **zero external
dependencies** (pure string building, fully unit-tested, Godot-consumable). Only
the PNG contact sheet needs a rasterizer (SkiaSharp native), so that lives in an
out-of-solution tool. `dotnet build Pockets.sln` / `dotnet test` never pull
SkiaSharp; the tool is run explicitly to regenerate assets.

## Generator usage

```bash
tools/glyph-gen/generate.sh                 # 12 SVGs -> assets/glyphs/, sheet -> vault
tools/glyph-gen/generate.sh --no-sheet      # SVGs only (no SkiaSharp needed to view)
tools/glyph-gen/generate.sh --svg-out DIR --sheet PATH.png
```

Outputs:
- `assets/glyphs/{basis-<quadrant>-<aspect>,parent-<quadrant>}.svg` — 12 files,
  `viewBox="0 0 100 100"`, single black stroke, `fill="none"` (modulate color in Godot).
- `~/obsid/paths/projects/pockets/assets/glyphs-contact-sheet-v1.png` — all 12,
  labeled, light+dark, for phone review.

## Parameter knobs (`GlyphParams`)

Everywhere the sketches are ambiguous, the shape is a knob, not a guess:

| Knob | Default | Drives |
|---|---|---|
| `ViewBox` | 100 | canonical square side |
| `StrokeWidth` | 7 | all line/arc weight |
| `StrokeLineCap` | round | cap style |
| `BasisLongLength` | 60 | longest staircase line |
| `BasisMidRatio` / `BasisShortRatio` | 0.62 / 0.30 | mid/short line lengths |
| `BasisRowGap` | 20 | vertical spacing of the 3 lines |
| `ArcCount` | 3 | wifi-rainbow arc count |
| `ArcInnerRadius` / `ArcRadiusStep` | 15 / 13 | rainbow radii |
| `ParentAnchorOffset` | 34 | how far the rainbow sits into its corner |

## Sketch-ambiguity notes (flagged for Aaron's contact-sheet review)

- **Basis line ratios & gap** parameterized (0.62 / 0.30 / gap 20). The sketch
  shows the staircase but not exact proportions — tune via the knobs above.
- **Wifi-rainbow interpretation:** rendered as concentric quarter-arcs anchored at
  the quadrant corner opening toward the Core. Aaron's sheet explores triangle+arc
  and dot-marked variants (right column); v1 commits to the clean nested-arc read.
  `ArcCount`/radii/offset are knobs if the count or curvature should change.
- **Stroke weight & caps** are single knobs so the whole set re-weights at once.

## Methodology fit

TDD: the chirality/transpose/determinism math and SVG validity were specified as
tests first (`Cosmology/` test folder), then the geometry. Data-as-source-of-truth
matches the repo's markdown-data convention and the immutable-record house style.

## Future slices (built on this base)

The cosmology plans **three writing-system eras** as archaeological layers:

1. **Old pictographic** — these glyphs as full pictograms (this slice).
2. **Half-pictographic** — lazier shorthand character forms.
3. **Recent phonetic** — evolved into a single/double-stroke alphabet.

Eras 2–3 are a *script-evolution* problem: deriving simplified, stroke-economical
successors from a pictographic base while preserving identity. The relevant
literature is **VecGlypher** (arXiv:2602.21461), which learns vector-glyph
evolution / stroke-reduction across script generations — a natural fit for
generating the shorthand and phonetic tiers from this v1 basis set. Those tiers
are deliberately **out of scope** here; this slice establishes the deterministic
pictographic base and the shared orientation data they will extend.
```

## Status / next

- [x] 8+4 data table, chirality math, geometry, deterministic SVG, contact sheet, tests.
- [ ] Godot import pass (SVG → `Texture2D`, modulate per zone palette).
- [ ] Script-evolution tiers (half-pictographic, phonetic) per VecGlypher.
- [ ] Aaron review of v1 sheet → confirm/adjust knobs and rainbow interpretation.
