---
name: agent-play
description: Agent-as-Player scripting for Pockets gameplay — generate tests or live WebSocket sequences
user_invocable: true
---

# /agent-play — Agent-as-Player Gameplay Scripting

Generate gameplay sequences where an automated agent plays Pockets: navigating grids, grabbing items, crafting recipes, exploring bags.

## Usage

```
/agent-play [target] <goal>
```

**Target parameter** (optional, default: `tui`):
- `tui` — Generate for the TUI client (Terminal.Gui). Default.
- `godot` — Generate for the Godot client.
- `both` — Generate for both clients.

**Examples:**
- `/agent-play craft a Belt Pouch` — TUI test (default)
- `/agent-play godot navigate to forest and harvest`
- `/agent-play both verify all recipes craft successfully`

## Testing Layers

All three layers share `Pockets.Core` (GameState, GameSession, GameController) — gameplay logic is identical across clients. What differs is how input reaches the core and how output is verified.

### Layer 1: Core Domain Tests (no UI)
- **Project:** `tests/Pockets.Core.Tests/`
- **How:** Direct `GameSession` method calls — `MoveCursor`, `ExecutePrimary`, `ExecuteGrab`, etc.
- **Verify:** Assert on `GameState` properties (cell contents, hand, breadcrumbs, facility state)
- **When to use:** Testing game logic, recipes, crafting, navigation. Fast, deterministic, no UI dependencies.
- **Canonical example:** `AgentPlayValidationTests.cs`

### Layer 2: TUI Integration Tests (FakeDriver)
- **Project:** `tests/Pockets.App.Tests/`
- **How:** `TuiTestHarness` wraps Terminal.Gui's `FakeDriver` for headless rendering. Inject keys via `Application.Driver`, read the 80×25 character buffer.
- **Verify:** Buffer captures — assert rendered characters/attributes match expected output.
- **When to use:** Testing that UI renders correctly, key bindings work end-to-end, visual regressions.
- **Note:** `FakeDriver` is global/static — tests use `[Collection("TUI")]` to prevent concurrency.

### Layer 3: Godot Live Tests (WebSocket)
- **Project:** External agent or in-Godot test script
- **How:** Connect to `DebugWebSocketServer` on port 9080. Send JSON commands, receive JSON state.
- **Commands:**
  ```json
  {"action": "key", "key": "Primary"}
  {"action": "click", "row": 0, "col": 2, "button": "Primary"}
  {"action": "back"}
  {"action": "tick"}
  {"action": "state"}
  {"action": "screenshot", "path": "/tmp/ss.png"}
  ```
- **Verify:** Parse JSON response `{"handled": bool, "status": "...", "state": {...}}`, or compare screenshots.
- **When to use:** Testing Godot-specific rendering, input handling, visual verification with screenshots.
- **Requires:** Godot running (`./run-godot.sh`)

### Choosing the Right Layer

| Goal | Layer |
|------|-------|
| Verify game logic (crafting, navigation, items) | Core |
| Verify TUI rendering or key bindings | TUI |
| Verify Godot rendering or mouse/touch input | Godot |
| Full E2E: logic + visual | TUI and/or Godot |

When target is `both`, generate a Core domain test (covers logic) plus a WebSocket command sequence (covers Godot live).

## Modes

### 1. Test Generation
**Trigger:** `/agent-play craft a Belt Pouch`

Generates an xUnit test that:
- Loads real content from `/data/` via `ContentLoader.LoadFromDirectory`
- Sets up a `GameSession` with `TickMode.Rogue`
- Uses agent helper methods (NavigateTo, GrabAndDeliver, etc.)
- Asserts the goal was achieved

For TUI target, can additionally generate a `TuiTestHarness`-based test with buffer assertions.

### 2. Live Play (WebSocket — Godot only)
**Trigger:** `/agent-play godot live: grab rock and drop at (2,3)`

Generates a sequence of WebSocket JSON commands for the running Godot game.

## Agent Loop: Observe → Plan → Act → Verify

1. **Observe** — Read `GameSession.Current` state: cursor position, active bag contents, hand contents, breadcrumb depth
2. **Plan** — Decompose the goal into steps: find item → navigate → grab → navigate → drop
3. **Act** — Execute via `GameSession` methods (tests) or WebSocket JSON (Godot live)
4. **Verify** — Assert expected state after each action

## Reference Files

- `references/game-mechanics.md` — GameKey enum, grid coords, facility lifecycle, tick modes
- `references/agent-patterns.md` — NavigateTo algorithm, grab/deliver pipeline, recipe cycling, code snippets

## Pattern: AgentPlayValidationTests

See `tests/Pockets.Core.Tests/Models/AgentPlayValidationTests.cs` for the canonical example:
- Data-driven: discovers all facilities/recipes from `/data/`
- Agent helpers: `NavigateTo`, `FindItemInGrid`, `GrabFromGrid`, `DeliverToSlot`, `GrabAndDeliver`, `SetFacilityRecipe`, `TickUntilComplete`
- Full coverage: crafts every recipe in every facility

## Test Setup Pattern

```csharp
var dataPath = GetDataPath(); // walks up to find Pockets.sln
var registry = ContentLoader.LoadFromDirectory(dataPath);
var recipes = registry.Recipes.Values.ToImmutableArray();
var facilityRecipeMap = registry.BuildFacilityRecipeMap();
// ... build state with facilities and materials ...
var session = GameSession.New(state, recipes, facilityRecipeMap, TickMode.Rogue);
```
