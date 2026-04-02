# Pockets DSL — Inventory Operations Language Design

## Purpose

An interpreted DSL for expressing inventory operations in Pockets. Serves as the substrate for:
- UI hotkey bindings (each hotkey maps to a DSL expression)
- Scripted sequences and tutorials
- User-defined macros / automation
- Test authoring (assert state after running a program)
- Future: facility recipes, AI agent behaviors

Design philosophy: **build the DSL/API first, then build the app around it.**

---

## Core Idea

Pockets has multiple simultaneous panels, each with an active cursor pointing into a bag. These cursors behave like CPU registers. Operations are named verbs with **implicit register bindings** — the opcode determines which registers it reads/writes and at what dereference level, so the caller never writes pointer syntax.

This is ASM-flavored (x86-style implicit register operands) but the goal is a notation that could also work in a more functional/compositional style. Not truly tacit or concatenative — but tacit-*looking* because most operations need zero explicit arguments.

---

## Register File

Fixed-size, nullable. Each register is a `Maybe<BreadcrumbEntry>`.

```
BreadcrumbEntry = { CellIndex: int, SavedCursor: Cursor }
  -- CellIndex: active cell in the panel's current bag
  -- SavedCursor: breadcrumb chain for nested bag navigation
```

### Panel Registers

| Register | Name      | Nullable | Notes |
|----------|-----------|----------|-------|
| `H`      | Hand      | No       | Hidden bag, usually 1 slot. Accumulator for transfers. Multi-slot supported for cut/paste of selections |
| `T`      | Toolbar   | No       | Persistent. Active slot = equipped tool. Read/write like any bag slot |
| `B`      | Bag       | No       | Main inventory. Always present |
| `W`      | World     | Yes      | Contextual. Avatar position in the wilderness |
| `C`      | Container | Yes      | Contextual. Chest, facility, NPC shop, etc. Rebound each time a container is opened |

### Special Registers

| Register | Name  | Type           | Notes |
|----------|-------|----------------|-------|
| `F`      | Focus | RegisterId     | Which panel register is "active" for panel-agnostic ops (movement, selection). Changed by `Focus` opcode or by UI panel-switch events |

### Flags Register

Set as side-effects of operations. Read-only in the DSL (no direct assignment).

| Flag    | Type        | Set By | Meaning |
|---------|-------------|--------|---------|
| `OK`    | bool        | All ops | Last operation succeeded |
| `REM`   | ItemStack?  | Transfer ops | Leftover stack that didn't fit. **Can be used as a source operand** — semantics TBD |
| `COUNT` | int         | Transfer/bulk ops | Number of items affected |
| `FULL`  | bool        | Transfer ops | Destination was full, transfer incomplete |
| `EMPTY` | bool        | Transfer ops | Source was empty, nothing to do |

---

## Access Levels

The key design insight: **the opcode determines the dereference level per parameter, not the syntax.** A register name like `T` can mean different things depending on the verb:

| Level   | What it resolves to               | Example use |
|---------|-----------------------------------|-------------|
| Bag     | The bag the register points into  | `Sort T`, `Fulfill C B` |
| Cell    | `Bag.Grid[CellIndex]`            | `Hoe W`, `Water W` |
| Stack   | `Cell.Contents` (the ItemStack)   | `Grab B`, `Move B C` |
| Index   | Just the cursor position          | Movement ops |

Each opcode parameter declares its access level at definition time. This should be expressed in C# as metadata (attribute, enum, or part of the opcode's type signature) so it can drive validation, UI generation, and documentation.

```csharp
// Conceptual — adapt to actual codebase patterns
enum AccessLevel { Bag, Cell, Stack, Index }
```

---

## Opcode Catalog

### Notation

- `r:X` = reads register X, `w:X` = writes register X
- Access level in parens after register: `r:T(Stack)` = reads T at Stack level
- `=> primitive` means the op is sugar over a lower-level op

### Primitives

These are the irreducible operations. Everything else is sugar or macros.

```
Move      src:Stack  dst:Stack     -- move itemstack. Sets OK, REM, COUNT, FULL
MoveHalf  src:Stack  dst:Stack     -- ceil(count/2). Remainder stays in src
MoveOne   src:Stack  dst:Stack     -- transfer 1 item
Swap      a:Stack    b:Stack       -- exchange two stacks. Sets OK
Sort      target:Bag               -- sort bag contents, merge stacks. Sets COUNT
Enter     target:Stack             -- target must be a bag. Push breadcrumb, open it. Sets OK
Leave     target:Bag               -- pop breadcrumb on that register. Sets OK
Focus     target:RegisterId        -- set F = target. No flags
```

### Navigation (operate on F implicitly)

```
Forward / Back        -- ±1 in row-major order, wrapping
Up / Down / Left / Right  -- grid-aware movement
Top / Bottom          -- first / last row
First / Last          -- cell 0 / cell N-1
```

Named variants for direct register targeting are macros:
```
:def ForwardT Focus T Forward ;
:def ForwardB Focus B Forward ;
-- etc.
```

### Hand Sugar (partial application of Move with H)

```
Grab      src:Stack   => Move src H       -- cut to hand
GrabHalf  src:Stack   => MoveHalf src H
Drop      dst:Stack   => Move H dst       -- paste from hand
DropOne   dst:Stack   => MoveOne H dst
```

### Transfer Sugar (named source/destination pairs)

```
QuickSwap  a:Stack b:Stack  => Swap a b   -- generic, 2 explicit args
AcquireTo  src:Stack dst:Bag              -- remove from src, acquire into dst starting at cell 0. Sets OK, REM, COUNT
```

### Tool Actions (bypass tool dispatch)

Each implicitly searches `T` bag for the first matching tool type. If not found, `OK = false`.

```
Mine     target:Cell    -- find pickaxe in T, extract resource at target. Sets OK, REM
Harvest  target:Cell    -- find scythe/hands in T, collect. Sets OK, REM
Plant    target:Cell    -- find seed at T cursor, consume, place at target. Sets OK, EMPTY
Hoe      target:Cell    -- find hoe in T, modify terrain. Sets OK
Water    target:Cell    -- find watering can in T, apply. Sets OK
```

Tool search strategy: **first matching tool found in toolbar, or fail.** No "best tool" heuristic.

### Generic Tool Use (indirect dispatch)

```
Use      target:Cell    -- T[TI].Action(target). The tool item defines behavior. Sets OK, varies
```

`Use` is effectively `CALL [T]` — a function pointer through the active toolbar slot.

### Facility Ops (bag-level access)

```
Fulfill   facility:Bag  source:Bag   -- auto-fill recipe inputs from source. Looks up active recipe from facility state. Sets OK, COUNT
Extract   facility:Bag  dest:Bag     -- pull outputs into dest via acquire. Sets OK, COUNT, REM
```

### Utility

```
Nop                     -- no-op
Wait    n:Immediate     -- consume n ticks
```

### Compound / Macro Definition

Forth-style named macro definitions:

```
:def name op1 op2 ... ;
```

Macros are sequential composition — just concatenated opcode execution. They expand inline.

Examples:
```
:def MineAcquire    Mine W  AcquireTo W B ;
:def PlantNext      Plant W  Right ;
:def GrabFromChest  Grab C  Drop B ;
```

### Conditionals (minimal)

Skip-next-instruction based on flags, x86-style:

```
SkipOK        -- skip next opcode if OK = true
SkipNotOK     -- skip next opcode if OK = false
SkipFull      -- skip next if FULL = true
SkipEmpty     -- skip next if EMPTY = true
```

Example — plant and advance, but stop advancing if plant failed:
```
:def PlantIfCan   Plant W  SkipNotOK Right ;
```

---

## Open Design Questions

### 1. REM as operand
`REM` holds the leftover ItemStack from a transfer that couldn't fully complete. Should it be usable as a *source* operand in subsequent ops? e.g., `Move REM B` to acquire the remainder into the bag. If so, it acts as a volatile pseudo-register. Needs clear lifetime semantics — when does REM get cleared?

### 2. Immediates syntax
`Wait 5`, `Repeat 3`, and debug ops like `Spawn "wood" 10` need immediate values. Current thinking: one optional trailing token per opcode. String literals for item IDs, integers for counts/ticks. No expressions or variables — this is a command language, not a programming language.

### 3. Selection model
Selection (multi-cell targeting) is currently stateful in the UI. For the DSL, a `ForEach` opcode that iterates the focused panel's selection may be cleaner than stateful select/deselect ops. Needs more design work. Placeholder:

```
ForEach op1 op2 ... End    -- run body once per selected cell, with F cursor set to each
```

Or selection could remain purely a UI concept, with the DSL always operating on the single cursor cell.

### 4. Multi-tool resolution
Tool actions search T for the "first matching tool." What constitutes a match? By tool *category* (any pickaxe) or specific ItemType? Category seems right for gameplay. If multiple matches exist, first-slot-wins is simple and predictable.

---

## Codebase Integration Notes

### Existing Patterns (Stage 3)

The current codebase uses immutable snapshots with undo via a snapshot stack (max 1000). Key types:

- `BreadcrumbEntry(int CellIndex, Cursor SavedCursor)` — exactly the register model foundation
- `Grid`, `Cell`, `ItemStack`, `ItemType`, `Bag` — the domain model the DSL operates on
- `BagRegistry` — bag lookup, needed for resolving register → bag
- `FacilityState` — recipe state, needed by `Fulfill`/`Extract`
- `ActionQueue` — existing tick-based action system

### Integration Approach

The DSL interpreter should:
1. Take the current game state (immutable) + a program (opcode list)
2. Execute opcodes sequentially, producing a new game state per step (or per program)
3. Return the final state + flag register + any error/halt info
4. **Not** directly mutate anything — produce new state values (align with existing snapshot/undo model)

This makes it testable in isolation: construct a state, run a program, assert on the result.

### Interpreter Shape (sketch)

```csharp
public record DslState(
    GameState Game,
    RegisterFile Registers,
    FlagRegister Flags
);

public record RegisterFile(
    BreadcrumbEntry Hand,
    BreadcrumbEntry Toolbar,
    BreadcrumbEntry Bag,
    BreadcrumbEntry? World,
    BreadcrumbEntry? Container,
    RegisterId Focus
);

public record FlagRegister(
    bool OK,
    ItemStack? Remainder,
    int Count,
    bool Full,
    bool Empty
);

// Execution: DslState -> Opcode -> DslState
public static DslState Execute(DslState state, Opcode op) => op switch {
    Move(var src, var dst) => ExecuteMove(state, src, dst),
    Grab(var src) => ExecuteMove(state, src, RegisterId.Hand),
    // ... etc
};
```

The `GameState -> Opcode -> GameState` shape means each opcode is a pure function on state, fitting the existing immutable/snapshot pattern. Macros just fold over the opcode list.
