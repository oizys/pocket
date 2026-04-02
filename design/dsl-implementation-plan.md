# DSL Implementation Plan — Inventory Operations Language

## Status: Planned

## Overview

Rearchitect Pockets.Core around a concatenative DSL for inventory operations. The DSL is the substrate for hotkey bindings, scripted sequences, test authoring, modding, and future AI behaviors. Design philosophy: **build the DSL first, then build the app around it.**

See `dsl-design-context.md` for the full language design (registers, opcodes, access levels). This document covers the concrete code changes to get there.

---

## Design Decisions (locked in)

These were resolved during design discussion and are not open questions:

1. **Flat bag store** — bags stored in `ImmutableDictionary<Guid, Bag>`, not nested in cells. Cells hold `BagRef(Guid)` instead of `Bag` instances. Eliminates the zipper (`WithActiveBag`) and makes BagRegistry redundant.

2. **RegisterFile** — five named panel registers (`H`, `T`, `B`, `W`, `C`), each a `BreadcrumbEntry?`. Replaces `Cursor` + `HandBag` + `Breadcrumbs` on GameState.

3. **Fixed-arity opcodes, no optional args** — zero-arg tacit verbs for defaults (`grab` = grab from B to H), suffixed explicit verbs for overrides (`grab-from` pops a RegisterId). No optional stack peeking.

4. **Automatic coercion** — each opcode parameter declares an expected `AccessLevel` (Register, Breadcrumb, Bag, Cell, Stack). Runtime walks the coercion chain `RegisterId → BreadcrumbEntry → Bag → Cell → Stack` to resolve. Coercion only goes *down* the chain; going up is a static error.

5. **Result stack, not flags** — ops push `Result<T>` onto a data stack. Combinators (`try`, `if-ok`, `each`, `times`) compose results. No mutable flags register.

6. **Focus and Selection are UI-only** — the UI resolves focus to a RegisterId and selection to an `each` loop before emitting DSL expressions. The DSL itself has no Focus opcode and no selection state.

7. **Two tiers** — high-level tacit verbs (zero args, hardcoded defaults) and low-level explicit verbs (pop args from stack). High-level is the default; low-level exists for macros and tooling internals.

8. **`move` is Stack → Cell** — source coerces to Stack (extract value), destination coerces to Cell (resolve container). The cell handles merge/reject/remainder.

---

## Phase 1: Flat Bag Store

The keystone change. Everything else depends on this.

### 1a. Introduce BagStore

```
New: BagStore — ImmutableDictionary<Guid, Bag> wrapper
  - GetById(Guid) → Bag
  - Update(Guid, Bag) → BagStore
  - Add(Bag) → BagStore
  - Remove(Guid) → BagStore
  - All, Count, Facilities (bags with FacilityState)
  - ParentIndex: ImmutableDictionary<Guid, BagOwnerInfo>
    rebuilt on mutation or lazily
```

### 1b. Change Cell to hold BagRef instead of Bag

```
Current:  ItemStack.ContainedBag : Bag?
Proposed: ItemStack.ContainedBag : Guid?   (or BagRef record)

Cell.HasBag checks: Stack?.ContainedBag != null
  && store.Contains(ContainedBag)
```

All bag data lives in the store. The ItemStack just holds a pointer. This means grab/drop/sort that move an ItemStack containing a bag automatically move the reference — the bag data stays in the store untouched.

### 1c. Migrate GameState

```
Current:
  GameState(RootBag, Cursor, ItemTypes, HandBag, Breadcrumbs)

Proposed:
  GameState(BagStore, RootBagId, Cursor, ItemTypes, HandBagId, Breadcrumbs)
```

Intermediate step — keep Cursor/HandBag/Breadcrumbs fields for now but swap the storage. `ActiveBag` becomes `Store.GetById(activeBagId)` instead of zipper traversal. `WithActiveBag(Bag)` becomes `this with { BagStore = BagStore.Update(activeBagId, newBag) }`.

### 1d. Delete WithActiveBag zipper

The zipper (`WithActiveBag`) currently rebuilds parent chains from leaf to root. With flat storage, updating a nested bag is just `Store.Update(id, newBag)` — O(1) structural sharing via ImmutableDictionary. Delete the zipper and all the parent-chain rebuild logic.

### 1e. Delete BagRegistry

`BagStore` *is* the registry. `BagRegistry.Build()` BFS traversal is gone. `GameState.Registry` computed property is gone. All callsites that used `Registry.GetById()` use `Store.GetById()`.

### Tests

- All existing tests must pass (bag store is an internal change)
- New tests for BagStore: add, update, remove, parent index consistency
- Verify bag-carrying items (grab/drop a bag item) correctly preserve the Guid reference
- Verify nested bag navigation still works with ID-based lookup

---

## Phase 2: RegisterFile

### 2a. Introduce RegisterFile and RegisterId

```csharp
enum RegisterId { H, T, B, W, C }

record RegisterEntry(Guid BagId, Cursor Cursor, ImmutableStack<BreadcrumbEntry> Breadcrumbs);

record RegisterFile(
    RegisterEntry Hand,
    RegisterEntry Toolbar,
    RegisterEntry Bag,
    RegisterEntry? World,
    RegisterEntry? Container)
{
    RegisterEntry? Get(RegisterId id) => id switch { ... };
    RegisterFile Set(RegisterId id, RegisterEntry entry) => ...;
}
```

### 2b. Migrate GameState to RegisterFile

```
Current:
  GameState(BagStore, RootBagId, Cursor, ItemTypes, HandBagId, Breadcrumbs)

Final:
  GameState(BagStore, RegisterFile, ItemTypes)
```

- `Cursor` → `Registers.Bag.Cursor`
- `HandBag` → `Registers.Hand` (just another register pointing into the store)
- `Breadcrumbs` → `Registers.Bag.Breadcrumbs`
- `RootBagId` → `Registers.Bag.BagId` (at the bottom of the breadcrumb stack)
- `ActiveBag` → resolved from focused register's breadcrumb chain + store

### 2c. Update GameSession

GameSession wraps GameState. Its `ExecutePrimary`, `ExecuteSecondary` etc. currently delegate to GameState tool methods. These will soon be replaced by DSL dispatch (Phase 4), but for now update them to work with RegisterFile.

### Tests

- Existing tool tests adapted to RegisterFile-based GameState
- RegisterFile: get/set/null handling for optional registers
- Multi-cursor independence: moving cursor in B doesn't affect W

---

## Phase 3: Coercion and Access Levels

### 3a. AccessLevel enum and attribute types

```csharp
enum AccessLevel { Register, Breadcrumb, Bag, Cell, Stack, Index }

/// Marks a static method as a DSL opcode. The interpreter reflects over these
/// at startup to build the dispatch table.
[AttributeUsage(AttributeTargets.Method)]
class OpcodeAttribute(string name) : Attribute
{
    public string Name => name;
    public RegisterId? DefaultRegister { get; init; }  // for tacit zero-arg form
}

/// Marks a method parameter with its expected access level and role.
/// The interpreter uses this to drive automatic coercion from the data stack.
[AttributeUsage(AttributeTargets.Parameter)]
class ParamAttribute(AccessLevel level) : Attribute
{
    public AccessLevel Level => level;
    public bool Source { get; init; }           // cleared/decremented after op
    public bool Target { get; init; }           // written to after op
    public RegisterId? DefaultRegister { get; init; }  // per-param default
}
```

The interpreter reflects over `[Opcode]` methods, reads `[Param]` attributes from each parameter, and handles coercion + write-back generically. Opcode method bodies receive strongly-typed, already-resolved arguments and never touch the data stack directly.

### 3b. Coercion resolver

Pure function, ~20 lines. Driven by `[Param]` attribute metadata:

```
Resolve(object value, AccessLevel expected, GameState state) → object

RegisterId    → look up RegisterFile → RegisterEntry
RegisterEntry → follow breadcrumbs in Store → Bag
Bag           → index by entry's Cursor → Cell
Cell          → read .Stack → ItemStack (error if empty)
Position      → just the cursor coordinates (for Index level)
```

Coercion walks down the chain from the value's actual level to the expected level. Going up (e.g., Stack where Bag is expected) is a static error at parse time.

### 3c. Write-back resolution

After an opcode executes, the interpreter checks `[Param(Target = true)]` and `[Param(Source = true)]` to know which cells to update in the game state. This keeps write-back logic out of individual opcode implementations — the method returns the new values, the harness writes them back to the correct register's bag/cell.

### 3d. Result type

```csharp
record DslResult<T>(T? Value, ItemStack? Remainder, string? Error)
{
    bool IsOk => Error is null;
    static DslResult<T> Ok(T value, ItemStack? remainder = null) => ...;
    static DslResult<T> Fail(string error) => ...;
}
```

Pushed onto the data stack by each opcode. Combinators consume results.

### Tests

- Coercion chain: RegisterId → each level, verify correct resolution
- Coercion errors: Stack where Bag expected → static error
- Empty cell coercion to Stack → runtime error with clear message
- Result stack: push, pop, combinator composition

---

## Phase 4: Opcode Extraction and DSL Core

### 4a. Extract tool methods from GameState

Current GameState has: `ToolGrab`, `ToolDrop`, `ToolSwap`, `ToolSort`, `ToolPrimary`, `ToolSecondary`, `ToolQuickSplit`, `ToolModalSplit`, `ToolHarvest`, `ToolHarvestPlant`, `MoveCursor`, `EnterBag`, `LeaveBag`, `Interact`.

Each becomes an attribute-decorated static method. The interpreter discovers these via reflection at startup and builds the dispatch table automatically — no manual `switch` over opcode records.

```csharp
static class Opcodes
{
    // Primitives — explicit args, popped from data stack

    [Opcode("move")]
    static DslResult Move(DslState state,
        [Param(AccessLevel.Stack, Source = true)] ItemStack source,
        [Param(AccessLevel.Cell, Target = true)] Cell dest)
        => ...;

    [Opcode("move-half")]
    static DslResult MoveHalf(DslState state,
        [Param(AccessLevel.Stack, Source = true)] ItemStack source,
        [Param(AccessLevel.Cell, Target = true)] Cell dest)
        => ...;

    [Opcode("swap")]
    static DslResult Swap(DslState state,
        [Param(AccessLevel.Cell, Source = true, Target = true)] Cell a,
        [Param(AccessLevel.Cell, Source = true, Target = true)] Cell b)
        => ...;

    [Opcode("sort", DefaultRegister = RegisterId.B)]
    static DslResult Sort(DslState state,
        [Param(AccessLevel.Bag)] Bag target)
        => ...;

    [Opcode("enter", DefaultRegister = RegisterId.B)]
    static DslResult Enter(DslState state,
        [Param(AccessLevel.Stack)] ItemStack target)
        => ...;

    [Opcode("leave", DefaultRegister = RegisterId.B)]
    static DslResult Leave(DslState state,
        [Param(AccessLevel.Bag)] Bag target)
        => ...;

    // Tacit sugar — zero-arg, defaults baked in via attributes

    [Opcode("grab", DefaultRegister = RegisterId.B)]
    static DslResult Grab(DslState state,
        [Param(AccessLevel.Stack, Source = true, DefaultRegister = RegisterId.B)] ItemStack source,
        [Param(AccessLevel.Cell, Target = true, DefaultRegister = RegisterId.H)] Cell dest)
        => Move(state, source, dest);

    [Opcode("drop", DefaultRegister = RegisterId.H)]
    static DslResult Drop(DslState state,
        [Param(AccessLevel.Stack, Source = true, DefaultRegister = RegisterId.H)] ItemStack source,
        [Param(AccessLevel.Cell, Target = true, DefaultRegister = RegisterId.B)] Cell dest)
        => Move(state, source, dest);

    [Opcode("grab-from")]
    static DslResult GrabFrom(DslState state,
        [Param(AccessLevel.Stack, Source = true)] ItemStack source,
        [Param(AccessLevel.Cell, Target = true, DefaultRegister = RegisterId.H)] Cell dest)
        => Move(state, source, dest);

    [Opcode("drop-to")]
    static DslResult DropTo(DslState state,
        [Param(AccessLevel.Stack, Source = true, DefaultRegister = RegisterId.H)] ItemStack source,
        [Param(AccessLevel.Cell, Target = true)] Cell dest)
        => Move(state, source, dest);

    // Navigation — coerced to Index level

    [Opcode("right", DefaultRegister = RegisterId.B)]
    static DslResult Right(DslState state,
        [Param(AccessLevel.Index)] Position pos)
        => ...;

    [Opcode("left", DefaultRegister = RegisterId.B)]
    static DslResult Left(DslState state,
        [Param(AccessLevel.Index)] Position pos)
        => ...;

    // Tool actions — coerced to Cell level

    [Opcode("harvest", DefaultRegister = RegisterId.W)]
    static DslResult Harvest(DslState state,
        [Param(AccessLevel.Cell, Source = true)] Cell target)
        => ...;

    [Opcode("mine", DefaultRegister = RegisterId.W)]
    static DslResult Mine(DslState state,
        [Param(AccessLevel.Cell, Source = true)] Cell target)
        => ...;

    // Facility ops — coerced to Bag level

    [Opcode("fulfill")]
    static DslResult Fulfill(DslState state,
        [Param(AccessLevel.Bag)] Bag facility,
        [Param(AccessLevel.Bag, Source = true, DefaultRegister = RegisterId.B)] Bag source)
        => ...;

    [Opcode("extract")]
    static DslResult Extract(DslState state,
        [Param(AccessLevel.Bag, Source = true)] Bag facility,
        [Param(AccessLevel.Bag, Target = true, DefaultRegister = RegisterId.B)] Bag dest)
        => ...;
}
```

The attribute pattern means adding a new opcode is just writing a method with the right decorations — no dispatch table to update, no opcode record to define.

### 4b. DslState record

```csharp
record DslState(
    GameState Game,
    ImmutableStack<object> DataStack   // result values, register IDs, quotations
);
```

Minimal. No flags register (results are on the stack). No focus (UI concern).

### 4c. Interpreter (reflection-driven)

At startup, the interpreter scans for `[Opcode]` methods and builds a dispatch dictionary:

```csharp
static class DslInterpreter
{
    // Built once at startup via reflection over [Opcode] methods
    static readonly ImmutableDictionary<string, OpcodeBinding> Dispatch = BuildDispatch();

    static DslState Execute(DslState state, ParsedOp op)
    {
        var binding = Dispatch[op.Name];
        // For each [Param]: pop from stack or use DefaultRegister, then coerce
        var args = binding.ResolveArgs(state, op);
        // Invoke the method with resolved args
        var result = binding.Invoke(state, args);
        // Write-back: update Source/Target cells in game state
        return binding.ApplyWriteBack(state, result, args);
    }

    static DslState Run(DslState state, IEnumerable<ParsedOp> program)
        => program.Aggregate(state, Execute);
}
```

`OpcodeBinding` is a cached reflection wrapper: method info, param attributes, coercion specs. Built once, called many times. No per-opcode `switch` — the dispatch table handles everything.

`ParsedOp` is just a name + any inline immediate values (ints, strings). Much simpler than a hierarchy of opcode records — the attribute metadata on the target method carries all the type information.

```csharp
record ParsedOp(string Name, ImmutableArray<object> Immediates);
```

### 4d. Combinators (the few non-attribute ops)

Combinators are structural — they control execution flow, not domain logic. These remain as explicit parsed forms since they don't map to single method calls:

```csharp
// Parsed program elements
abstract record ProgramNode;
record OpNode(string Name, ImmutableArray<object> Immediates) : ProgramNode;
record QuotationNode(ImmutableArray<ProgramNode> Body) : ProgramNode;
record TimesNode(ImmutableArray<ProgramNode> Body) : ProgramNode;   // pops int
record TryNode(ImmutableArray<ProgramNode> Body) : ProgramNode;
record IfOkNode(ImmutableArray<ProgramNode> Body) : ProgramNode;
record EachNode(ImmutableArray<ProgramNode> Body) : ProgramNode;
record DefNode(string Name, ImmutableArray<ProgramNode> Body) : ProgramNode;
```

`Run` is still a fold. Macros expand inline at parse time.

### 4e. Parser

Simple tokenizer + recursive descent. Tokens are whitespace-separated words.

```
"right harvest right harvest grab drop"
→ [RightOp, HarvestOp, RightOp, HarvestOp, GrabOp, DropOp]

"C grab-from W drop-to"
→ [PushRegister(C), GrabFromOp, PushRegister(W), DropToOp]

"[ right harvest ] 3 times"
→ [PushQuotation([RightOp, HarvestOp]), PushInt(3), TimesOp]
```

### 4f. Macro definitions

```
:def mine-and-grab  W harvest  grab ;
→ stored as named opcode list, expanded inline at parse time
```

### Tests

- Each opcode in isolation: construct DslState, run one op, assert result
- Coercion integration: `GrabOp` resolves B to Stack, H to Cell
- Composition: multi-opcode programs produce correct final state
- Result stack: `try` catches errors, `if-ok` branches, `times` repeats
- Parser: round-trip tokenize → parse → execute for representative programs
- Macro expansion: `:def` creates reusable sequences
- **Regression**: rewrite existing ToolTests as DSL programs, verify identical outcomes

---

## Phase 5: Rewire UI and GameSession

### 5a. GameSession dispatches DSL

Replace `ExecutePrimary`, `ExecuteSecondary`, etc. with a single:

```csharp
GameSession Execute(string dslExpression)
{
    var program = DslParser.Parse(expression);
    var dslState = new DslState(Current, ImmutableStack<object>.Empty);
    var result = DslInterpreter.Run(dslState, program);
    return ApplyResult(result);
}
```

### 5b. Input mapping becomes DSL expressions

```csharp
// Current: switch on key → call specific method
// Proposed: key → DSL string → Execute

var keyBindings = new Dictionary<Key, string>
{
    [Key.Left]   = "left",
    [Key.Right]  = "right",
    [Key.E]      = "enter",
    [Key.Q]      = "leave",
    [Key.D1]     = "grab",       // or "drop" if hand has items (UI resolves)
    [Key.D4]     = "sort",
    [Key.D5]     = "acquire-random",
};

// Mouse click on panel W, cell (2,3):
// UI emits: "W goto 2 3 harvest" or similar
```

### 5c. UI emits Focus/Selection as DSL

The UI layer translates focus and selection into explicit DSL before dispatch:

- Keyboard in focused panel → prepend register ID if non-default
- Selection → `each { ... }` wrapping the verb
- Modal split → `16 split-at` (pushes int, pops it)

### Tests

- Integration: key press → DSL emission → state change → UI update
- Undo: DSL expressions are atomic units for undo snapshots
- Action log: DSL expression string is the log entry

---

## Phase 6: Cleanup (completed)

Done during Phases 1-2:
- Deleted `BagRegistry` class (replaced by `BagStore`)
- Deleted `WithActiveBag` zipper logic (flat store makes it a one-liner)
- Deleted `Bag.FindBagById` (use `Store.GetById`)
- Deleted `GetBagAtDepth`, `ReplaceBagInTree` (zipper infrastructure)

Kept intentionally:
- Tool methods on `GameState` — these are the implementation layer that opcodes delegate to. Keeping both the direct API and DSL paths allows gradual migration.
- `ToolResult` — still used by the direct API path (GameSession.ExecutePrimary etc.)
- GameState has both `(BagStore, LocationMap, ItemTypes)` constructor fields and backward-compatible computed properties (`Cursor`, `RootBagId`, `HandBagId`, etc.)

Future cleanup (when direct API callers are fully migrated to DSL):
- Delete `ExecutePrimary`, `ExecuteSecondary`, etc. from `GameSession`
- Delete `ToolResult` (opcodes use `DslResult`)
- Remove backward-compatible computed properties from `GameState`
- Consider renaming `GameState` → `World` or `State`

---

## Implementation Status

All phases complete. Architecture:

```
GameState(BagStore, LocationMap, ItemTypes)
  ├── BagStore: flat ImmutableDictionary<Guid, Bag>
  ├── LocationMap: variadic ImmutableDictionary<LocationId, Location>
  │     └── Location(BagId, Cursor, Breadcrumbs)
  └── DSL layer (src/Pockets.Core/Dsl/):
        ├── Coercion: LocationId → Location → Bag → Cell → Stack
        ├── [Opcode] attributes + reflection-driven dispatch
        ├── Parser: whitespace-separated tokens, [ ] quotations, :def macros
        ├── Interpreter: fold over ProgramNodes
        └── GameSession.Execute(string) + GameController.HandleDsl(string)
```

Both paths coexist:
- **Direct API**: GameSession.ExecutePrimary() → GameState.ToolPrimary() → ToolResult
- **DSL**: GameSession.Execute("grab right drop") → DslInterpreter → Opcodes → DslResult

---

## Risk Notes

- **Undo granularity** — one undo snapshot per `GameSession.Execute()` call (the DSL expression string the UI emits), not per opcode. This matches user intent.
- **split-at** opcode currently falls back to half-split. Full modal split (arbitrary left count from data stack) needs interpreter support for mixed int/LocationId stack popping.
- **Opcode write-back** — currently opcodes handle their own state updates internally (delegating to GameState tool methods). The `[Param(Source/Target)]` attributes are metadata for future generic write-back when opcodes are decoupled from GameState methods.
