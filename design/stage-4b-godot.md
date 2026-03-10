# Stage 4b: Godot C# App Target

## Concept

Add a Godot 4.x C# scene graph as a second app target alongside the Terminal.Gui TUI. Same `Pockets.Core` library, different presentation layer. First pass is asset-less: all UI is code-driven with colored rectangles, text labels, and programmatic styling matching the TUI's color scheme.

## Goals

- Prove that `Pockets.Core` works cleanly as a shared library across two different UI frameworks
- Identify and introduce abstractions where Terminal.Gui assumptions leak into Core
- Get mouse/touch input working (blocked in WSL TUI)
- Establish the rendering/input architecture that will eventually support sprites and animations

## Non-Goals (First Pass)

- No sprite assets, textures, or imported art
- No animation system
- No audio
- No mobile/touch optimization (just desktop Godot)

## Architecture

### Project Structure

```
pockets/
├── src/
│   ├── Pockets.Core/          (shared, no UI dependencies)
│   ├── Pockets.App/           (Terminal.Gui TUI — existing)
│   └── Pockets.Godot/         (Godot C# project — new)
├── tests/
│   └── Pockets.Core.Tests/    (shared tests)
├── data/                      (shared item/recipe data)
└── Pockets.sln               (includes all projects)
```

### Abstraction Boundary

`Pockets.Core` must have zero references to Terminal.Gui or Godot. The current boundary is mostly clean — Core defines models and logic, App defines views. Potential leaks to audit:

- **Color handling**: `CategoryColors` is in `Pockets.App/Rendering/`. If Core needs color info, define it as data (enum/string) in Core, map to framework-specific colors in each app.
- **Input mapping**: Core defines `Direction`, tool keys, etc. as abstract enums. Each app maps its input system to these.
- **Rendering interfaces**: `IStateRenderer` is in `Pockets.Core/Rendering/`. Text-based renderers are Core (useful for tests/debugging). Framework-specific renderers live in each app project.

### Godot Scene Structure

```
Main (Node2D)
├── GameController.cs          (input handling, state management)
├── UIRoot (Control)
│   ├── TopBar (HBoxContainer)
│   │   └── BreadcrumbLabel
│   ├── MainPanel (HSplitContainer)
│   │   ├── LeftPanel (VBoxContainer)
│   │   │   ├── GridPanel.cs   (draws the grid as colored rects)
│   │   │   └── DescriptionPanel.cs
│   │   └── RightPanel (VBoxContainer)
│   │       ├── HandCell.cs
│   │       └── ActionQueue.cs
│   └── BottomBar (HBoxContainer)
│       └── ToolbarLabel
```

All nodes created in code (no .tscn scene files for now). This keeps the first pass purely code-driven and avoids Godot editor dependency.

### Rendering Approach (Asset-Less)

Each grid cell rendered as:
- `ColorRect` for background (category color)
- `ColorRect` border (frame type color: yellow=input, green=output, white=default)
- `Label` for item abbreviation + count
- Cursor: inverted colors (same as TUI)
- Hand cell: cyan highlight when holding

Color values defined in a shared `ColorPalette` class in the Godot project, mapping from Core's category/frame enums to Godot `Color` values. Mirror the TUI palette exactly.

### Input Handling

```csharp
// GameController.cs
public override void _Input(InputEvent @event)
{
    if (@event is InputEventKey key && key.Pressed)
    {
        var action = MapKeyToAction(key.Keycode);
        if (action is not null)
            ApplyAction(action);
    }
    if (@event is InputEventMouseButton mouse && mouse.Pressed)
    {
        var cellIndex = GridPanel.HitTest(mouse.Position);
        if (cellIndex >= 0)
            HandleCellClick(cellIndex, mouse.ButtonIndex);
    }
}
```

Key mappings mirror the TUI exactly. Mouse adds: left-click = move cursor, right-click = primary action (grab/drop).

### Godot Project Setup

- Godot 4.3+ with .NET 8 C# support
- `Pockets.Godot.csproj` references `Pockets.Core.csproj`
- Build: `dotnet build` for Core + tests, Godot editor or CLI for the Godot project
- The Godot project lives in the same repo but can be opened independently in the Godot editor

## Compatibility Concerns

### Things That Should Just Work

- All Core models, logic, tools — zero UI dependency
- GameState, GameSession, recipes, data loading
- Text-based state renderers (for debugging in Godot console)

### Things That Need Abstraction

1. **App initialization**: Currently `Program.cs` does data loading + Terminal.Gui init. Extract data loading into a shared `GameBootstrap` class that both apps call.

2. **Color definitions**: Move category→color mapping from `Pockets.App/Rendering/CategoryColors.cs` to `Pockets.Core` as abstract color data (e.g., `(byte R, byte G, byte B)` tuples or named color enums). Each app maps to its framework's color type.

3. **Key bindings**: Currently implicit in `GameView.cs` key handler. Extract to a `KeyBindings` data structure in Core that maps abstract actions to key names. Each app maps key names to its framework's key type.

### Things That Stay Separate

- View/scene construction (completely different per framework)
- Input event handling (different event systems)
- Application lifecycle (Terminal.Gui.Application vs Godot SceneTree)
- Platform-specific rendering (text cells vs colored rects)

## Implementation Steps

1. **Audit Core for UI leaks** — Ensure Pockets.Core has no Terminal.Gui references
2. **Extract GameBootstrap** — Shared data loading / init, used by both apps
3. **Extract color/input abstractions** — Core defines data, apps define mappings
4. **Scaffold Godot project** — .csproj, folder structure, reference to Core
5. **Implement GameController** — State management, input dispatch
6. **Implement GridPanel** — Code-driven colored rect grid
7. **Implement remaining panels** — Hand, description, breadcrumbs, toolbar
8. **Mouse input** — Cell hit-testing, click actions
9. **Verify parity** — Same game behavior in both targets

## Open Questions

- Should the Godot project be on a separate branch, or in the main tree from the start?
- Godot 4.3 vs 4.4? (4.4 has better .NET 8 support but may be less stable)
- Do we want a shared `Pockets.UI` abstraction library between the two apps, or keep them fully independent with just Core shared?
- Build pipeline: should `dotnet test` still work from repo root with the Godot project present? (Godot C# projects sometimes need the Godot SDK to build)

## Status

Proposed. Research phase — need to verify Godot C# + .NET 8 project structure works alongside the existing solution.
