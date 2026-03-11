# Research: Godot 4.x 2D + 3D Mixing

## Context

Future feature: dual representation of the same inventory grid — a traditional 2D inventory UI alongside a 3D view where cells are positions on a 3D grid, items are colored cubes/spheres, and the cursor is a player character walking between positions.

## 1. SubViewport Approach (The Standard Pattern)

Embed a 3D scene in a 2D UI using **SubViewportContainer** (a Control node that participates in UI layout) containing a **SubViewport** with your 3D scene inside. In C#:

```csharp
var container = new SubViewportContainer();
container.Stretch = true;  // resize with layout
container.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;

var subViewport = new SubViewport();
subViewport.OwnWorld3D = true;  // isolated 3D world
subViewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Always;
subViewport.HandleInputLocally = true;

container.AddChild(subViewport);
// Add Camera3D, lights, and 3D nodes as children of subViewport
```

## 2. Split Screen Layout

Already supported by Stage 4b's HSplitContainer design. Replace the right panel with a SubViewportContainer. Both sides respond to the same keyboard input via the controller's `_Input()` method — the controller updates the shared state, then tells both renderers to refresh.

## 3. Code-Driven 3D Primitives (No Assets Needed)

`MeshInstance3D` with `BoxMesh`/`SphereMesh` and `StandardMaterial3D.AlbedoColor` for coloring:

```csharp
var mesh = new MeshInstance3D();
var box = new BoxMesh();
box.Size = new Vector3(0.9f, 0.9f, 0.9f);
mesh.Mesh = box;

var mat = new StandardMaterial3D();
mat.AlbedoColor = new Color(0.8f, 0.2f, 0.2f);  // category color
mat.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;  // flat prototype colors
mesh.MaterialOverride = mat;

mesh.Position = new Vector3(col, 0, row);  // grid position
```

## 4. Camera — Orthographic Recommended

`Camera3D` with `ProjectionType.Orthogonal` for a clean grid view:

```csharp
var camera = new Camera3D();
camera.Projection = Camera3D.ProjectionType.Orthogonal;
camera.Size = 14f;  // frames an 8x4 grid nicely
// Position above and behind at isometric angle
camera.Position = new Vector3(4f, 8f, 6f);
camera.LookAt(new Vector3(4f, 0f, 2f));  // grid center
```

Perspective is an option later for a more immersive feel, but orthographic matches the grid aesthetic.

## 5. Sync Pattern — Direct Render Calls (Simplest)

The controller already orchestrates state changes. Just add a second `Render(state)` call:

```csharp
void UpdateView(GameState state)
{
    gridPanel2D.Render(state);    // existing 2D grid
    gridWorld3D.Render(state);    // new 3D view
}
```

No signals or reactive patterns needed at this stage. C# events are the upgrade path if more decoupling is needed later.

## 6. Performance — Non-Concern

32 cubes in a SubViewport is trivially light. Even with materials and a camera, this is well under any performance threshold. No optimization needed.

## 7. Architecture Implications

**The Stage 4b design does not block this.** The current plan's architecture supports adding a 3D view later with zero changes:

- Data-only Core (no rendering assumptions)
- HSplitContainer layout (swap/add panels freely)
- Controller-calls-renderer pattern (add another renderer)

When the time comes: create a `GridWorld3D : Node3D`, wrap it in `SubViewport` + `SubViewportContainer`, add one render call in the controller.

## Open Questions

1. **Input routing**: When the 3D view is focused, should clicks on cubes move the cursor? SubViewport handles input forwarding, but need to convert 3D raycasts to grid coordinates.
2. **Transition**: Should 2D and 3D always be shown together, or toggle between them? Layout flexibility needed.
3. **Cursor representation in 3D**: Simple highlighted cube? A small character model? Animated movement between cells? Decide when we get there.
