# OpResult Threading — DSL Execution Model

## Status: Approved

## Concept

Every DSL opcode is a pure function `(OpResult, ...args) → OpResult`. State flows through the stack as a token, not managed by the interpreter. The interpreter becomes a dumb loop; all semantics (error accumulation, rollback, step-debug) emerge from the OpResult chain.

## Design

### OpResult

```csharp
record OpResult(
    GameState State,       // current state after this op
    GameState Before,      // snapshot from before the expression started (for undo/diff)
    ImmutableList<string> Errors)
{
    bool IsOk => Errors.Count == 0;

    // Thread state forward, preserve Before and accumulated errors
    OpResult Chain(GameState newState) => new(newState, Before, Errors);
    OpResult ChainError(string error) => new(State, Before, Errors.Add(error));
}
```

- `State` is the latest game state — each opcode reads from and writes to this
- `Before` is set once at expression start, never changes — used for undo and diff
- `Errors` accumulates across the chain — not just the last failure

### Initial stack

Before any script runs, the interpreter pushes one OpResult:

```
[ OpResult(currentGameState, currentGameState, []) ]
```

This is the "seed" that every opcode chain grows from. Opcodes are fully static — all context comes from the stack.

### Opcode signature

```csharp
// Old: opcode reads state from DslState, interpreter manages state
static DslResult Grab(DslState state, [Param(...)] Cell source)

// New: opcode pops OpResult, reads state from it, pushes new OpResult
static OpResult Grab(OpResult input, [Param(...)] Cell source)
```

Coercion resolves `[Param]` arguments against `input.State`. The opcode computes a new GameState and returns `input.Chain(newState)`.

### Stack convention

OpResult always sits under surface values:

```
After action op:    [ ..., OpResult ]
After query op:     [ ..., OpResult, bool/int/value ]
```

Combinators consume surface values and thread the OpResult through:

```
[ body ] when:    pop bool, if true run body (which threads OpResult), else pass OpResult through
[ body ] try:     save OpResult ref, run body, if errors clear them and push false, else push true
value else:       pop fallback, pop result-or-value, return value if ok or fallback if error
```

### Why not a list of states

Appending each intermediate GameState to a list gives step-debug capability but costs allocation on the common path. Since GameState is immutable, the Before reference is sufficient for undo/diff. For step-debug, an external observer can hook into the interpreter loop and collect OpResults — opt-in overhead, not default overhead.

### Nested operations

In nested quotations (`[ [ inner ] try outer ] try`), each scope adds its own OpResult to the stack. Inner operations thread their own chain; the outer scope sees the final OpResult from the inner scope. This naturally supports future call-by-name or call-by-cc conventions — each "frame" is an OpResult on the stack, and unwinding is just stack manipulation.

### Error recovery: `else` pattern

```
[ risky-op ] try 0 else
```

`try` catches errors and pushes a success/failure indicator. `else` pops two values: if the first indicates failure, returns the second (fallback); otherwise returns the first. Chainable:

```
[ parse-int ] try [ parse-float ] else [ default ] else
```

Each `else` gets a shot at converting an error to a value.

## What changes from current implementation

| Current | New |
|---------|-----|
| `DslState` holds `GameState` + data stack | `OpResult` on the stack holds state; data stack is just the stack |
| Interpreter extracts `DslResult.State` after each op | Interpreter just runs ops; state flows through stack |
| `DslResult` discards error/remainder after each op | `OpResult` accumulates errors across the chain |
| Coercion reads from `DslState.Game` | Coercion reads from `OpResult.State` (popped from stack) |
| Opcodes return `DslResult` | Opcodes return `OpResult` |
| `DslState` is a separate type from game state | `OpResult` is the only threading type |
| `try` wraps exceptions | `try` checks `OpResult.Errors` |

## Interpreter simplification

```csharp
static ImmutableStack<object> Run(ImmutableStack<object> stack, IEnumerable<ProgramNode> program)
    => program.Aggregate(stack, Execute);

static ImmutableStack<object> Execute(ImmutableStack<object> stack, ProgramNode node)
    => node switch
    {
        OpNode op => ExecuteOp(stack, op),
        QuotationNode q => stack.Push(q),
        TimesNode t => ExecuteTimes(stack, t),
        // ... combinators pop from stack, run body, push result
    };
```

`DslState` goes away entirely. The interpreter is just stack operations. Opcodes are just functions. The stack is the only state.
