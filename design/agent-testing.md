# Agent-Driven Testing

## Concept

Use AI agents as automated game testers across two complementary modes: **agent-as-player** (functional correctness) and **visuals-in-the-loop** (UI/layout validation). Both modes work against the shared `GameController` abstraction, which decouples game logic from any specific UI framework.

## Architecture

```
                    GameController (Core)
                   /        |        \
              TUI View   Godot View   WebSocket API
              (FakeDriver)            (port 9080)
                 |                       |
           TuiTestHarness          Agent Scripts
           (headless xUnit)        (Python/Claude)
```

Both the TUI and Godot clients map their native input events to `GameKey`/`ClickType` and delegate to the same `GameController`. The WebSocket server exposes this API for external agents driving the running Godot app.

## Prong 1: Unit Tests (existing)

Standard xUnit tests against `GameController`, `GameSession`, and `GameState`. These cover all model/state changes and control mechanisms. 443 tests currently. Not agent-driven — mentioned here for completeness.

## Prong 2: Agent-as-Player

**Goal:** Script longer gameplay sequences to find state bugs that unit tests miss. The pattern is: play the game toward a goal, verify the game remains in a valid and progressable state throughout.

**Examples of what this catches:**
- Crafting a facility, placing items, harvesting — then discovering state is broken and you can't continue
- Edge cases in grab/drop/swap sequences that leave orphaned items or corrupted stacks
- Recipe cycling or bag navigation paths that produce unreachable states

**Two routes:**

### TUI Route (headless, fast)
- `TuiTestHarness` + `FakeDriver` runs the full TUI app without a terminal
- Tests send `KeyEvent` sequences, inspect the character buffer
- Already working: `GamePlaythroughTests` demonstrates grab/move/drop flows
- Extend with scenario-based tests: "craft a workbench, enter it, process a recipe, harvest output"

### Godot Route (WebSocket, visual)
- `DebugWebSocketServer` runs on port 9080 inside the Godot app
- External scripts send JSON commands: `{"action": "key", "key": "Primary"}`
- Responses include full state snapshot (grid, cursor, hand, breadcrumbs, action log)
- Agent can be a Python script, a Claude Code session, or a custom test runner
- Supports screenshot capture: `{"action": "screenshot", "path": "/tmp/frame.png"}`

### Agent Script Pattern

Whether TUI or WebSocket, the agent follows the same loop:

```
1. Observe: read current state (grid contents, hand, position)
2. Plan: decide next action toward goal (e.g., "grab the rock, move to workbench, drop it")
3. Act: send the input (GameKey or click)
4. Verify: check post-action state for invariants
   - No items created or destroyed (conservation)
   - No cells with invalid stacks (count > max, wrong category in filtered slot)
   - Game is still progressable (not stuck)
5. Repeat until goal is reached or failure detected
```

**Fuzz mode:** Instead of goal-directed play, send random valid actions and check invariants after each. Good for finding crashes and state corruption.

**Scripted scenarios:** Define a sequence of high-level goals ("acquire 5 rocks, craft a workbench, enter it, set recipe to Tanner, fill input slots, wait for output"). The agent figures out the specific key sequences. Failures become new unit test cases.

## Prong 3: Visuals-in-the-Loop

**Goal:** Validate that the UI presents the right information in a usable layout. Less about pixel-perfection, more about "can the player see what they need to see?"

**What this catches:**
- Log panel too narrow to read action messages
- Description panel truncated or overlapping the grid
- Draw order issues (elements hidden behind others)
- Anchor/stretch behavior broken at different window sizes
- Missing or misplaced UI elements after state changes (e.g., back button not appearing when nested)

### TUI Route
- `FakeDriver` buffer provides character-level inspection
- `FindText()`, `GetChar()`, `GetAttribute()` verify text presence and positioning
- Can assert: "the action log panel contains the text 'Grab'" or "the grid occupies columns 0-40"

### Godot Route
- Screenshot capture via WebSocket: `{"action": "screenshot", "path": "..."}`
- Feed screenshot to a vision model (Claude, Gemini) with a prompt describing what should be visible
- Example prompt: "This is a grid inventory game. The player just grabbed an item. Verify: (1) the hand panel shows one item, (2) the grid cell at cursor is now empty, (3) the action log shows a grab message, (4) all text is readable and not clipped"
- Can also resize the window and re-screenshot to test responsive layout

### Incremental Approach
1. Start with manual screenshot review during development
2. Add WebSocket `/screenshot` to CI — capture frames at key moments, archive for review
3. Add vision-model assertions for critical layouts (post-craft, nested bag, full grid)
4. Eventually: automated resize testing (script window size changes, screenshot, validate)

## Status

- [x] `GameController` abstraction (Core) — shared by TUI and Godot
- [x] TUI wired to `GameController` with `MapKey`
- [x] Godot wired to `GameController` with `MapKey`
- [x] `DebugWebSocketServer` running on port 9080
- [x] WebSocket protocol: key, click, back, tick, state, screenshot
- [x] `TuiTestHarness` + `FakeDriver` for headless TUI testing
- [ ] Agent-as-player Python script (goal-directed)
- [ ] Agent-as-player fuzz mode
- [ ] Scripted scenario library
- [ ] Vision-model screenshot assertions
- [ ] CI integration for screenshot capture
- [ ] Window resize testing
