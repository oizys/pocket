# Progressive Unveiling of Metastructure

## Concept

The game's self-similar metastructure (everything is bags) is revealed in layers, mirroring the journey of learning LISP: at first just being operational, but over time unveiling the beauty and power of the self-similar metastructure.

### Unveiling Phases

1. **Opaque phase:** New tools are forcibly added to the toolbar. The toolbar bag isn't visible in the root bag. Players just see "press 1-5 for tools" — standard game UI.
2. **Awareness phase:** The toolbar becomes visible as a bag in the root bag, but is read-only or pinned. Players realize "oh, my hotbar is literally a bag."
3. **Manipulation phase:** Players can rearrange tools within the toolbar bag using normal grab/drop.
4. **Full access phase:** Whole-bag swapping unlocked. Rules for nested bags within toolbar slots. Hotkey press "trickles through" bag logic (e.g. a bag in toolbar slot 3 could open a sub-menu of tools).

### Design Goal

Each phase teaches the player a deeper truth about the system while giving them earned power over it. Mastery of one layer's friction earns circumvention through the next layer's tools.

## Cohesion: Very High

This is a pacing/progression design layered on top of Meta-Bags. No new mechanics — just controlled exposure of existing ones. The progression system itself could be driven by game events (find a special item that "unlocks" toolbar editing).

## Intuition: High

Players never face the full complexity at once. Each phase matches their current mental model. By the time they reach full access, they've internalized the bag metaphor deeply enough that it feels natural rather than overwhelming.

## Architecture

- Progression flags on GameState (enum or bitfield): `MetaAccess.Opaque`, `.Visible`, `.Editable`, `.FullAccess`
- UI renderers check flags to show/hide system bags in root view
- Input handlers check flags to allow/deny grab/drop on system bag cells
- Progression triggers: event-driven (story items, achievements, or explicit unlocks)
- No new model types. Flags + conditional rendering/input

## Methodology Fit

- **Builds on:** Meta-Bags concept entirely
- **New friction:** Limited access creates curiosity and anticipation
- **Reduces friction:** Each unlock rewards mastery with new power
- **Emergent potential:** Players who reach full access discover combinations the designer didn't plan for — the LISP moment

## Status

Proposed. Depends on Meta-Bags. Unveiling phases map naturally to development stages.
