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

### ItemProperty

A property is a named value. The value type is a variant that starts with Int and String, but is guaranteed to grow.

**Option A — Variant record with subtypes:**
```csharp
public abstract record ItemProperty(string Name);
public record IntProperty(string Name, int Value) : ItemProperty(Name);
public record StringProperty(string Name, string Value) : ItemProperty(Name);
// Future: FloatProperty, BoolProperty, etc.
```

**Option B — Single record with object value:**
```csharp
public record ItemProperty(string Name, object Value);
```

Option A is preferred — pattern matching on subtypes is idiomatic C# and catches type errors at compile time. Option B is simpler but loses type safety.

### ItemStack Extension

```csharp
public record ItemStack(
    ItemType ItemType,
    int Count,
    Bag? ContainedBag = null,
    ImmutableDictionary<string, ItemProperty>? Properties = null);
```

- `Properties` is nullable — null means no properties (most items, all stackable items)
- Dictionary keyed by property name for O(1) lookup
- Immutable — modifications return new ItemStack via `with`
- Convenience methods: `GetProperty<T>(name)`, `WithProperty(prop)`, `WithoutProperty(name)`

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

## Methodology Fit

- **Testable**: Property CRUD on ItemStack is pure record operations — trivial to unit test.
- **Incremental**: Add the property infrastructure first with no consumers. Then adopt it in farming, then durability, etc.
- **Data-driven**: Default properties in item definitions fit the existing typed-header markdown system.
- **Non-breaking**: Properties field is nullable with default null — all existing code continues to work unchanged.

## Status

Proposed. This is foundational infrastructure that unblocks farming (design #15) and future features like tool durability. Should be implemented before or alongside farming.
