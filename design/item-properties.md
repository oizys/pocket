# Item Properties: Per-Instance Data on Unique Items

## Concept

Unique (non-stackable) ItemStacks gain the ability to carry a set of named properties with variant values. This enables per-instance state like sword durability, custom player-given names, plant growth progress, enchantment levels, or any other data that varies between two items of the same ItemType.

Properties are **unique items only**. Stackable items cannot hold per-instance properties — if you need quality tiers on stackable items (like The Sims quality stars), model them as separate ItemType sub-definitions (e.g. "Tomato (Silver)", "Tomato (Gold)") rather than properties on a shared type.

## Cohesion: Very High

ItemStack already distinguishes unique vs stackable via ItemType.IsStackable. Properties are a natural extension of uniqueness — unique items are already "one per cell" because they have individual identity. Properties give that identity concrete data.

Unlocks multiple planned features:
- **Farming**: Plant growth progress stored as a property rather than requiring new infrastructure
- **Tool durability**: Swords, axes, etc. degrade with use
- **Custom naming**: Player renames items
- **Enchantments/modifiers**: Per-item bonuses

## Intuition: High

"This particular sword has 47/100 durability" is a universally understood game concept. Properties are the data model behind it.

## Architecture

### PropertyValue (Value-Only DU)

A property value is a variant type. The name lives in the dictionary key, not on the value — this avoids redundancy and desync risk. Starts with Int and String, guaranteed to be extended.

```csharp
public abstract record PropertyValue;
public record IntValue(int Value) : PropertyValue;
public record StringValue(string Value) : PropertyValue;
// Future: FloatValue, BoolValue, etc.
```

Pattern matching is clean and type-safe:
```csharp
if (stack.Properties?["Durability"] is IntValue(var d)) { /* use d */ }
```

### ItemStack Extension

```csharp
public record ItemStack(
    ItemType ItemType,
    int Count,
    Bag? ContainedBag = null,
    ImmutableDictionary<string, PropertyValue>? Properties = null);
```

- `Properties` is nullable — null means no properties (most items, all stackable items)
- Dictionary keyed by property name for O(1) lookup
- Immutable — modifications return new ItemStack via `with`
- Convenience methods: `GetInt(name)`, `GetString(name)`, `WithProperty(name, value)`

### Enforcement

- Stackable items **cannot** have properties. Setting properties on a stackable ItemStack is a no-op or error.
- Stacking logic (merge) should assert both stacks have null properties (stackable items shouldn't reach merge with properties).
- Properties do not affect item identity for matching purposes — two unique items of the same ItemType are still the "same type" regardless of property values.

### Data Definition

Properties can have defaults defined in item data:

```markdown
# Item: Iron Sword
Category: Weapon
Stackable: No
Properties: Durability 100, Quality "Normal"

A sturdy blade forged from iron.
```

Default properties are copied onto each new instance created by the output pipeline.

### Use Cases

| Feature | Property Name | Type | Example |
|---------|--------------|------|---------|
| Durability | `Durability` | Int | 47 |
| Max Durability | `MaxDurability` | Int | 100 |
| Custom Name | `CustomName` | String | "Ol' Reliable" |
| Plant Progress | `Progress` | Int | 3 |
| Plant Duration | `Duration` | Int | 6 |
| Enchantment | `Enchantment` | String | "Fire" |

## Facility Progress Unification

Currently facility crafting progress lives on `FacilityState` (a field on `Bag`). With item properties, this could migrate to the owning `ItemStack` — the Workbench *item* carries `Progress: 3` rather than the Workbench *bag* carrying `FacilityState.Progress = 3`. This would unify the tick mechanism for facilities and plants: both are unique items with progress properties, both advance on ticks, both complete when progress hits duration.

This migration is optional and non-blocking — facilities work fine as-is. But the unification is appealing because it means tick logic has one place to look (item properties on unique items) rather than two (FacilityState on bags + properties on plant items).

### Bag-to-Owner Resolution

If facility progress moves to item properties, facility bags need a way to read their owner's state. Currently a bag has no upward reference — it doesn't know which ItemStack contains it. This matters when rendering (showing progress inside the facility) or when facility logic needs to check the owning item's properties.

Three approaches, with tradeoffs:

#### Approach 1: Walk Breadcrumbs Upward

The current navigation model already encodes the path from root to active bag. To find the owner of the active bag, peek the top breadcrumb entry — it gives the cell index in the parent bag, and that cell's ItemStack is the owner.

```
ActiveBag owner = ParentBag.Grid.GetCell(Breadcrumbs.Peek().CellIndex).Stack
```

**Pros:**
- Zero new infrastructure. Works today.
- No state to keep in sync. Breadcrumbs are the source of truth for "where am I."
- Predictable cost: O(1) to peek, O(depth) to resolve the parent bag from root.
- Naturally correct — the breadcrumb trail *is* the path you took to get here.

**Cons:**
- Only works for the *currently navigated* bag. If tick logic needs to update a facility the player isn't inside, breadcrumbs don't help — you'd need `ReplaceBagById` (DFS) anyway.
- O(depth) to resolve the parent bag from root via `GetBagAtDepth`. In practice depth is small (rarely > 5), but it's a full root-to-leaf walk each time.
- **Portal complication:** If portals allow multiple breadcrumb paths to the same bag (like symlinks), peeking breadcrumbs gives you "the path I took" not "the canonical owner." Two players (or two portal entries) reaching the same bag would see different parent cells. This is actually *correct* for navigation (you go back the way you came), but means the owner relationship is path-dependent, not intrinsic.

#### Approach 2: Bag.OwnerId (Guid back-reference)

Add `Guid? OwnerId` to `Bag`, pointing at the ItemStack that contains it (or more precisely, at the *bag's own Id* as stored on the parent — but since bags have Ids and items don't currently, this might point to some stable item identity).

Actually, the reference would need to point at something *findable*. Options:
- Point at the **parent Bag's Id** + cell index (where in that bag the owner item lives)
- Point at a new **ItemStack Id** (would require adding Guid to ItemStack for unique items)

```csharp
public record Bag(..., Guid? OwnerBagId = null, int? OwnerCellIndex = null);
```

**Pros:**
- O(1) to find the owner given BagRegistry (look up OwnerBagId, read cell at OwnerCellIndex).
- Works for any bag, not just the currently navigated one. Tick logic can resolve owners without breadcrumbs.
- Intrinsic relationship — not path-dependent.

**Cons:**
- **Sync burden.** Every operation that moves a bag (grab, drop, sort) must update the OwnerId on the contained bag. Sort reorders cells, so every bag in the sorted grid needs its OwnerId/CellIndex updated. This is error-prone and a source of subtle bugs.
- **Immutability friction.** Updating a bag's OwnerId means creating a new Bag, which means updating the ItemStack's ContainedBag, which means updating the Cell, which means updating the Grid, which means updating the parent Bag. It's `with` all the way up — but you're already doing that anyway via the zipper.
- **Cell index fragility.** If the parent bag is sorted or items are rearranged, the OwnerCellIndex goes stale. You'd need to update it on every grid mutation of the parent, not just when the owned bag moves.
- **Portal complication:** Same bag reachable via multiple paths is fine — OwnerId is canonical. But if portals allow an item to *appear* in multiple cells (true symlink semantics), OwnerId can only point to one. This is probably fine since items aren't actually duplicated, just reachable via shortcuts.

#### Approach 3: Owner Registry (Computed Index)

Similar to BagRegistry, build a computed `OwnerRegistry` mapping `Bag.Id → (ParentBag.Id, CellIndex)` via BFS. Query it when needed, rebuild after mutations.

```csharp
public record OwnerEntry(Guid ParentBagId, int CellIndex);
// OwnerRegistry: ImmutableDictionary<Guid, OwnerEntry>
```

**Pros:**
- No stored back-references. No sync burden. Computed fresh like BagRegistry.
- Works for any bag, not just navigated ones.
- Intrinsic — derived from the actual tree structure.
- Can be combined with BagRegistry (same BFS pass builds both indices).

**Cons:**
- Same rebuild cost as BagRegistry — O(total cells across all bags) on every access. Currently BagRegistry isn't cached because `with` copies break identity. If this becomes a hot path (tick logic iterating all facilities), the cost multiplies.
- Still O(1) lookup after rebuild, but the rebuild itself is non-trivial for large bag trees.
- Two registries to keep conceptually aligned (or merge into one richer index).

#### Recommendation

**For v1: Walk breadcrumbs** for the active bag (rendering progress when the player is inside a facility/plant). Use **ReplaceBagById** for tick updates to non-navigated bags (already the pattern for facility ticks). This requires zero new infrastructure.

**Longer term:** If/when the portal system introduces multiple paths to the same bag, or if tick iteration over many facilities becomes a performance concern, introduce the **Owner Registry** (Approach 3) as a computed index alongside BagRegistry. Avoid stored back-references (Approach 2) — the sync burden is high and the immutable model fights it at every turn.

The key insight is that **owner resolution is rarely needed in a hot loop.** Tick logic currently does DFS via `ReplaceBagById` which already walks the tree. Adding an owner lookup to that same walk is marginal cost. The expensive case (iterating *all* facilities to tick them) already uses BagRegistry.Build() which could be extended to capture owner info in the same pass.

## Methodology Fit

- **Testable**: Property CRUD on ItemStack is pure record operations — trivial to unit test.
- **Incremental**: Add the property infrastructure first with no consumers. Then adopt it in farming, then durability, etc.
- **Data-driven**: Default properties in item definitions fit the existing typed-header markdown system.
- **Non-breaking**: Properties field is nullable with default null — all existing code continues to work unchanged.

## Status

Proposed. This is foundational infrastructure that unblocks farming (design #15) and future features like tool durability. Should be implemented before or alongside farming.

Bag-to-owner resolution: use breadcrumb walking for v1 (zero new infrastructure). Defer Owner Registry to when portals or performance demand it. See analysis above.
