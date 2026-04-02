# Pockets DSL Quick Start

The Pockets DSL is a concatenative language for inventory operations. Programs are strings of whitespace-separated words that execute left to right. Each word is either a **verb** (does something) or a **value** (pushes data for the next verb to use).

## Your first program

```
grab right drop
```

This grabs the item at the cursor, moves the cursor right, and drops the item there. Three words, three actions, left to right. That's the whole language model.

## Verbs

Verbs are the actions you can perform. Every verb has a **default location** it operates on (usually B, your inventory bag), so you don't need to specify one unless you want to override it.

### Movement

| Verb | What it does |
|------|-------------|
| `right` | Move cursor right (wraps around) |
| `left` | Move cursor left |
| `up` | Move cursor up |
| `down` | Move cursor down |

### Inventory

| Verb | What it does |
|------|-------------|
| `grab` | Pick up item at cursor into hand |
| `drop` | Place hand item at cursor |
| `swap` | Exchange hand item with cursor item |
| `grab-half` | Pick up half the stack into hand |
| `drop-one` | Place one item from hand at cursor |
| `sort` | Sort and merge all items in the bag |
| `split-at` | Split stack at cursor in half |

### Bag navigation

| Verb | What it does |
|------|-------------|
| `enter` | Open the bag at cursor (go inside) |
| `leave` | Go back to parent bag |

### World actions

| Verb | What it does |
|------|-------------|
| `harvest` | Take item from current cell (when inside a nested bag) |

### Context-sensitive

| Verb | What it does |
|------|-------------|
| `primary` | Left-click action: enter bags, grab, drop, merge, swap, or harvest depending on context |
| `secondary` | Right-click action: grab half or place one depending on context |

### Debug

| Verb | What it does |
|------|-------------|
| `acquire-random` | Add a random item to the bag |

## Values

Values push data onto a stack for the next verb to consume.

### Locations

Locations tell verbs which panel to operate on. There are five:

| Value | Location | What it is |
|-------|----------|-----------|
| `H` | Hand | The items you're carrying |
| `T` | Toolbar | Equipped items (future) |
| `B` | Bag | Your inventory — the default for most verbs |
| `W` | World | The wilderness or world view (future) |
| `C` | Container | A chest or facility you're interacting with (future) |

Most verbs default to B, so you rarely need to write it. You only use a location when you want to override:

```
C grab      -- grab from Container instead of Bag
W harvest   -- harvest from World
```

Locations are case-insensitive (`b` and `B` both work).

### Numbers

Push an integer for verbs that need one:

```
16 split-at    -- split the stack, keeping 16 on the left
```

## Combining verbs

Since programs execute left to right, you can chain any sequence:

```
-- Move an item two cells to the right
grab right right drop

-- Grab three items from different cells
grab right grab right grab

-- Enter a bag, harvest something, come back
enter harvest leave
```

## Quotations and repetition

Wrap verbs in `[ ]` to create a **quotation** — a block of code you can reuse:

```
[ right harvest ] 3 times
```

This moves right and harvests, three times in a row. The number goes between the quotation and `times`.

## Error handling

Wrap verbs in `try { }` to catch errors instead of stopping the program:

```
try { leave }
```

If you're already at the root bag, `leave` would fail. Inside `try`, the error is caught and pushed as a result. You can then check it:

```
try { grab } if-ok { right drop }
```

This tries to grab. If it succeeds, moves right and drops. If the cell was empty (grab fails), the `if-ok` block is skipped.

## Macros

Define reusable sequences with `:def`:

```
:def scoop  grab right ;

scoop scoop scoop
```

This defines `scoop` as "grab then move right", then calls it three times. The definition ends with `;`. Macros are expanded inline at parse time — they're just shorthand, not functions.

## How it works under the hood

Every verb knows what "shape" of data it needs:

- `sort` needs a **Bag** — it sorts all cells
- `grab` needs a **Cell** — it takes the item from that cell
- `harvest` needs a **Cell** — it removes the item
- `right` needs an **Index** — it moves the cursor position

When you write a location like `B` before a verb, the system automatically **coerces** it to the right shape. `B` is a location. If the verb needs a Bag, the system resolves B's location to find the active bag. If it needs a Cell, it goes one step further and looks up the cell at the cursor. If it needs a Stack, it reads the item from that cell.

This coercion chain means you never have to think about "dereferencing" — you just name a location and the verb figures out what it needs.

```
Location → Bag → Cell → Stack
    B    →  inventory bag  →  cell at cursor  →  item in that cell
```

## Examples

### Organize your inventory
```
sort
```

### Move an item to a specific spot
```
grab right right right down drop
```

### Harvest a row of items from a wilderness bag
```
enter
[ harvest right ] 6 times
leave
```

### Enter a facility, grab the output, leave
```
enter right right grab leave drop
```

### Define a "collect all" macro
```
:def collect-row  [ harvest right ] 8 times ;
enter collect-row leave
```
