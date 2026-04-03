# Pockets DSL Quick Start

The Pockets DSL is a concatenative language for inventory operations. Programs are strings of whitespace-separated words that execute left to right. Each word is either a **verb** (does something), a **query** (reads state and pushes a value), or a **value** (pushes data for the next word to use).

> **Note:** Some examples in this guide use opcodes marked with * that are planned but not yet implemented. These are included to show the language's design direction.

## Your first program

```
grab right drop
```

This grabs the item at the cursor, moves the cursor right, and drops the item there. Three words, three actions, left to right. That's the whole language model.

## Verbs

Every verb has a **fixed arity** — it always consumes the same number of values from the stack. Most verbs default to operating on B (your inventory bag), so in the common case they consume nothing and you just write the verb name.

### Movement

| Verb | What it does |
|------|-------------|
| `right` | Move cursor right (wraps around) |
| `left` | Move cursor left |
| `up` | Move cursor up |
| `down` | Move cursor down |

### Inventory

| Verb | Default | What it does |
|------|---------|-------------|
| `grab` | B → H | Pick up item at cursor into hand |
| `drop` | H → B | Place hand item at cursor |
| `swap` | B ↔ H | Exchange hand item with cursor item |
| `grab-half` | B → H | Pick up half the stack into hand |
| `drop-one` | H → B | Place one item from hand at cursor |
| `sort` | B | Sort and merge all items in the bag |
| `split-at` | B | Split stack at cursor in half |
| `harvest` | B | Take item from cursor cell (when nested) |

### Bag navigation

| Verb | What it does |
|------|-------------|
| `enter` | Open the bag at cursor (go inside) |
| `leave` | Go back to parent bag |

### Context-sensitive

| Verb | What it does |
|------|-------------|
| `primary` | Left-click action (see "How primary works" below) |
| `secondary` | Right-click action (see "How secondary works" below) |

### Debug

| Verb | What it does |
|------|-------------|
| `acquire-random` | Add a random item to the bag |

## Queries

Queries read the current state and push a value onto the stack. They don't change anything.

| Query | Pushes | What it checks |
|-------|--------|---------------|
| `hand-empty?` | bool | Is the hand empty? |
| `cell-empty?` | bool | Is the cursor cell empty? |
| `cell-has-bag?` | bool | Does the cursor cell contain a bag? |
| `nested?` | bool | Are we inside a nested bag? |
| `same-type?` | bool | Is the hand item the same type as the cell item? |
| `output-slot?` | bool | Is the cursor cell an output slot? |
| `cell-count` | int | Item count at cursor (0 if empty) |

## Values

Values push data onto the stack.

### Locations

Locations identify which panel a verb operates on:

| Value | Location | What it is |
|-------|----------|-----------|
| `H` | Hand | The items you're carrying |
| `T` | Toolbar | Equipped items (future) |
| `B` | Bag | Your inventory — the default |
| `W` | World | Wilderness or world view (future) |
| `C` | Container | A chest or facility (future) |

Most verbs default to B so you rarely write it. Locations are case-insensitive.

### Numbers

```
16 split-at    -- split the stack, keeping 16 on the left
```

### Booleans

```
true    -- push true
false   -- push false
```

## Stack manipulation

| Word | Effect | What it does |
|------|--------|-------------|
| `dup` | a → a a | Copy top of stack |
| `pop` | a → | Discard top of stack |
| `over` | a b → a b a | Copy second item to top |
| `s-swap` | a b → b a | Swap top two stack values |
| `call` | [q] → (executes q) | Execute a quotation from the stack |

Note: `s-swap` is the stack swap. `swap` is the inventory verb (exchange hand and cell).

## Arithmetic

| Word | Effect |
|------|--------|
| `+` | a b → (a+b) |
| `-` | a b → (a-b) |
| `*` | a b → (a*b) |
| `div` | a b → (a/b), returns 0 for divide-by-zero |

## Logic and comparison

| Word | Effect |
|------|--------|
| `and` | bool bool → bool |
| `or` | bool bool → bool |
| `not` | bool → bool |
| `lte` | int int → bool (a ≤ b) |
| `gte` | int int → bool (a ≥ b) |
| `eq` | int int → bool (a = b) |

## Combinators

All combinators are **postfix** — the quotation comes first, then the combinator.

### Repetition

```
[ right harvest ] 3 times
```

Runs the body 3 times. The count goes between the quotation and `times`.

### Conditionals

```
cell-empty? not [ grab ] when          -- run body if true
cell-empty? [ acquire-random ] unless  -- run body if false

hand-empty?
  [ grab ]           -- true branch
  [ drop ]           -- false branch
  if-else
```

### Dispatch tables

```
[
  [ cell-has-bag? ]    [ enter ]
  [ hand-empty? ]      [ grab ]
  [ true ]             [ drop ]
] cond
```

`cond` takes a quotation of paired `[test] [body]` sub-quotations. It runs each test in order; the first to push `true` has its body executed. Remaining tests are skipped.

### Error handling

```
[ leave ] try                           -- pushes true/false
[ grab ] try [ right drop ] if-ok       -- run body only if try succeeded
```

`try` catches errors: pushes `true` if the body succeeded, `false` if it failed (and rolls back state on failure).

### Loops

```
[ cell-empty? not ] [ harvest right ] while
```

`while` takes two quotations: a test and a body. Runs the test, and if it pushes `true`, runs the body. Repeats until the test returns `false`. Safety limits: breaks automatically if the OpResult accumulates errors, or after 512 iterations (whichever comes first).

### Early exit

```
grab break sort    -- grab runs, sort never runs
```

`break` immediately exits the current quotation, loop, or program. The state is preserved as-is — it's not an error.

## Macros

```
:def scoop  grab right ;

scoop scoop scoop
```

Defines `scoop` as "grab then move right". The definition ends with `;`. Macros are expanded inline at parse time — they're shorthand, not functions.

## How primary works

`primary` is the left-click action. It's defined as a `cond` dispatch table:

```
[
  [ output-slot? cell-empty? not and hand-empty? and ] [ grab ]
  [ cell-has-bag? ]                                    [ enter ]
  [ hand-empty? cell-empty? and ]                      [ ]
  [ hand-empty? nested? and ]                          [ harvest ]
  [ hand-empty? ]                                      [ grab ]
  [ cell-empty? ]                                      [ drop ]
  [ same-type? ]                                       [ drop ]
  [ true ]                                             [ swap ]
] cond
```

Reading top to bottom: if you're on an output slot with an item and your hand is empty, grab. If the cell has a bag, enter it. If your hand is empty and the cell is empty, do nothing. If your hand is empty and you're nested, harvest. And so on. The first matching test wins.

## How secondary works

`secondary` is the right-click action:

```
[
  [ hand-empty? cell-empty? and ]                      [ ]
  [ hand-empty? cell-count 1 lte and ]                 [ ]
  [ hand-empty? ]                                      [ grab-half ]
  [ cell-empty? same-type? or ]                        [ drop-one ]
  [ true ]                                             [ ]
] cond
```

If your hand is empty and the cell is empty or has only 1 item, do nothing. If your hand is empty and the cell has more, grab half. If the cell is empty or the same type as your hand, drop one. Otherwise, nothing.

## How coercion works

Every verb knows what "shape" of data it needs. `sort` needs a **Bag**. `grab` needs a **Cell**. `right` needs an **Index**. The system automatically resolves a location to the right shape:

```
Location → Bag → Cell → Stack
    B    →  inventory bag  →  cell at cursor  →  item in that cell
```

This means you never need to think about dereferencing — just name a location and the verb figures out the rest.

## Examples

### Organize your inventory
```
sort
```

### Move an item two cells to the right
```
grab right right drop
```

### Harvest until the row is empty
```
enter
[ cell-empty? not ] [ harvest right ] while
leave
```

### Enter a facility, grab the output, leave
```
enter right right grab leave drop
```

### Check if a stack is between 3 and 10
```
cell-count dup 3 gte s-swap 10 lte and
```

### Define a "collect all" macro
```
:def collect-row  [ cell-empty? not ] [ harvest right ] while ;
enter collect-row leave
```

### Conditional grab-or-drop in one line
```
hand-empty? [ grab ] [ drop ] if-else
```

### Safe harvest with error recovery
```
[ enter collect-row leave ] try pop
```
