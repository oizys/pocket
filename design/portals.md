# Portals: Linked Cell-to-Cell Navigation

## Concept

Portals are paired links between two cells anywhere in the bag hierarchy. Each portal points to the other via an absolute address (bag path + cursor position). Moving into or placing an item into a portal with a valid pair transports the player or item to the exit portal's cell. Portals are reciprocal — entering from either side sends you to the other.

An unpaired portal (no link) is inert — it exists but does nothing. This is necessary for player-placed portals since placing both halves is two separate operations.

Portals solve navigation burden as bag hierarchies deepen (e.g. regularly traversing up 2, down 4 bags between a factory and a wilderness). They also enable **tiled worlds**: wilderness bags stitched together via N/S/E/W portal exits, creating screen-at-a-time navigation like the original Zelda. Buildings inside a wilderness are naturally modeled as bags; portals add lateral movement that bags alone can't provide.

Key insight: bags provide "in" and "out" navigation. Portals provide "across" — linking arbitrary points in the hierarchy without nesting.

## Cohesion: High

Portals extend the existing navigation model (cursor + breadcrumbs) with a new traversal type. They reuse the same grid/cell/cursor primitives. If implemented as Cell Frames (see `cell-frames.md`), they slot cleanly into the extensibility point already designed for cell-level behaviors.

## Intuition: High

"Step on the portal, appear at the other end." Universal game concept. The paired/unpaired distinction is natural — one half is useless alone, like a single walkie-talkie. Tiled worlds via portal exits map directly to classic game navigation (Zelda screens, roguelike stairs).

## Architecture

### Portal Address

An absolute location in the bag hierarchy:

```
PortalAddress
├── BagPath: ImmutableArray<int>    (cell indices from root to target bag, empty = root)
└── CellIndex: int                   (cell within the target bag)
```

Alternatively, use a stable bag ID rather than a path (paths break if bags are moved/sorted):

```
PortalAddress
├── BagId: Guid                      (stable identifier on each Bag)
└── CellIndex: int
```

The BagId approach is more robust but requires adding IDs to Bag records.

### Portal as Cell Frame

```
sealed record PortalFrame(
    PortalAddress? PairedAddress,
    bool IsLocked = false) : CellFrame(IsLocked);
```

- `PairedAddress` is null for an unpaired (inert) portal half
- When paired, both sides point to each other
- Placing/removing a portal must update both sides atomically

### Navigation Behavior

- **Player enters portal cell**: if paired, player is transported to exit cell (cursor jumps, breadcrumbs adjust to target bag path)
- **Item placed on portal cell**: if paired, item is acquired at exit cell instead (or acquired into exit bag if exit cell is occupied)
- **Exit cell occupied**: item stacks/merges per normal rules, or fails if incompatible

### Tiled Wilderness

Portals at grid edges create seamless lateral navigation:

```
┌─────────┐    ┌─────────┐
│ Forest   │───→│ Forest   │
│ (West)  │←───│ (East)  │
│         │    │         │
│    ↓    │    │         │
└─────────┘    └─────────┘
     │
┌─────────┐
│ Forest   │
│ (South) │
└─────────┘
```

Each edge portal links to the corresponding edge cell of the adjacent tile. Generation creates the bags and portal pairs together.

### Pairing Operations

- **Place first half**: creates a PortalFrame with `PairedAddress = null`
- **Place second half**: creates a PortalFrame and updates the first half's PairedAddress to point here (and vice versa)
- **Remove one half**: sets the other half's PairedAddress to null (becomes inert)
- **Move a bag containing a portal**: if using path-based addressing, all portal addresses pointing into/out of the moved bag must update. BagId-based addressing avoids this.

## Implementation Decisions

- **Cell Frame, not item**: a portal modifies cell behavior, not cell contents. An item sitting on a portal should be transported; the portal itself stays. This aligns with the Frame model.
- **BagId preferred over path addressing**: paths are fragile (sort, grab/drop bags, restructuring all invalidate paths). A stable GUID on each Bag is more robust, though it adds a field to the Bag record and requires a lookup mechanism (bag registry or tree walk).
- **Reciprocal invariant**: the system must maintain the invariant that if A points to B, B points to A. All mutations go through a helper that updates both sides.

## Methodology Fit

- **Builds on:** Cell Frames, cursor/breadcrumb navigation, bag hierarchy
- **Enables:** fast cross-hierarchy travel, tiled worlds, player-built transport networks
- **Reduces friction:** eliminates repetitive up/down navigation in deep hierarchies
- **Emergent potential:** Very high. Portal networks are player-designed topology. Combined with conveyors (another Frame type), portals enable item transport pipelines across bags. Tiled wilderness portals create explorable overworlds. Portal placement as a craftable/findable resource adds strategic depth.

## Open Questions

- Should portal traversal be instant or use the action queue (speed-based)?
- Can items be pushed through portals by conveyors, or only by player action?
- How are tiled wilderness portals generated — all at once, or lazily as the player approaches an edge?
- Should there be a visual distinction between paired and unpaired portals?
- Cost/rarity of player-craftable portals — too cheap trivializes navigation, too expensive and they're never used

## Status

Proposed. Depends on Cell Frames. Natural fit for mid-stage development once bag navigation is well-established and hierarchy depth starts creating navigation friction.
