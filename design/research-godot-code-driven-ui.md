# Research: Code-Driven UI in Godot 4.x C#

Research for porting the Pockets TUI to Godot 4.6.1 with C#/.NET. All findings based on the Godot 4.x stable API surface. Web fetching was unavailable so could not verify against live 4.6.1 docs — core Control/Container API has been stable since 4.0 so risk is low.

## 1. Control Nodes vs Custom _Draw()

### Option A: Composed Control Nodes

Build each grid cell as a tree of Control nodes (Panel/ColorRect for background, Label for text, another Panel for the border frame).

**Pros:**
- Built-in input events per node (`_GuiInput`), so each cell automatically handles mouse enter/exit/click
- Layout containers (GridContainer) handle positioning math automatically
- Theme override system gives per-node styling without subclassing
- Godot's UI layout system handles resizing/anchoring

**Cons:**
- 32 cells × ~3–4 nodes each = 96–128 Control nodes in the grid alone
- Each Control node carries overhead (transform, notification pipeline, etc.)
- Updating many nodes per frame (e.g., full grid refresh after Sort) means many property sets
- More complex scene tree to debug

**Code sketch:**
```csharp
var cellPanel = new PanelContainer();
cellPanel.CustomMinimumSize = new Vector2(80, 60);

var styleBox = new StyleBoxFlat();
styleBox.BgColor = categoryColor;
styleBox.BorderWidthBottom = 2;
styleBox.BorderWidthTop = 2;
styleBox.BorderWidthLeft = 2;
styleBox.BorderWidthRight = 2;
styleBox.BorderColor = frameColor; // yellow/green/white
cellPanel.AddThemeStyleboxOverride("panel", styleBox);

var label = new Label();
label.Text = "Wd Sw\n x3";
label.HorizontalAlignment = HorizontalAlignment.Center;
label.AddThemeColorOverride("font_color", Colors.White);
label.AddThemeFontSizeOverride("font_size", 14);
cellPanel.AddChild(label);

gridContainer.AddChild(cellPanel);
```

### Option B: Single Custom _Draw() Control

One Control node for the entire grid. Override `_Draw()` to paint all 32 cells using `DrawRect()`, `DrawString()`, etc.

**Pros:**
- Minimal node count (1 node for the entire grid)
- Full control over pixel-level rendering
- Single `QueueRedraw()` call refreshes everything
- Easy to implement cell-to-index math (grid is uniform)

**Cons:**
- Must implement hit-testing manually (coordinate math)
- Must manage fonts, sizing, and text measurement manually
- No built-in hover/focus states
- Drawing text requires obtaining a Font resource and calling `DrawString(font, pos, text, ...)`

**Code sketch:**
```csharp
public override void _Draw()
{
    var font = ThemeDB.FallbackFont;
    int fontSize = 14;

    for (int i = 0; i < 32; i++)
    {
        int col = i % Columns;
        int row = i / Columns;
        var rect = new Rect2(col * CellWidth, row * CellHeight, CellWidth, CellHeight);

        DrawRect(rect, GetCategoryColor(i));                           // Background
        DrawRect(rect, GetFrameColor(i), filled: false, width: 2.0f); // Border
        if (i == cursorIndex)
            DrawRect(rect, CursorColor);                               // Cursor

        var textPos = new Vector2(rect.Position.X + 4, rect.Position.Y + fontSize + 4);
        DrawString(font, textPos, GetCellText(i),
            HorizontalAlignment.Left, CellWidth - 8, fontSize, GetTextColor(i));
    }
}
```

### Option C: Hybrid (Recommended)

Use **composed Control nodes** for the overall layout (panels, containers, labels for description/breadcrumbs) but a **single custom `_Draw()` Control** for the 8×4 grid itself. The grid is the only component that benefits from custom drawing — it has uniform cells, needs fast batch updates, and has simple hit-test geometry. Everything else (text panels, toolbar, hand display) is straightforward Label/Panel composition.

**Why hybrid wins for Pockets:**
- The grid is a dense, uniform structure where custom drawing is simpler and more efficient
- Side panels are text-heavy and benefit from Label's built-in text wrapping, sizing, theme support
- Hit testing on a uniform grid is trivial (integer division)
- Layout containers handle the 4-panel split without pixel math

## 2. Hit Testing

### With composed Control nodes

Each cell node receives `_GuiInput(InputEvent)` automatically:

```csharp
cellPanel.GuiInput += (InputEvent @event) =>
{
    if (@event is InputEventMouseButton mb && mb.Pressed
        && mb.ButtonIndex == MouseButton.Left)
        OnCellClicked(cellIndex);
};
```

Set `MouseFilter = Control.MouseFilterEnum.Stop` on the cell panel so it captures clicks.

### With custom _Draw() (recommended for grid)

Manual coordinate math — trivial on a uniform grid:

```csharp
public int HitTest(Vector2 localPos)
{
    int col = (int)(localPos.X / CellWidth);
    int row = (int)(localPos.Y / CellHeight);
    if (col < 0 || col >= Columns || row < 0 || row >= Rows)
        return -1;
    return row * Columns + col;
}

public override void _GuiInput(InputEvent @event)
{
    if (@event is InputEventMouseButton mb && mb.Pressed)
    {
        int index = HitTest(mb.Position);
        if (index >= 0)
            EmitSignal(SignalName.CellClicked, index, (int)mb.ButtonIndex);
    }
}
```

`_GuiInput` receives positions in the Control's local coordinate space — no global-to-local conversion needed. The Control must have `MouseFilter = MouseFilterEnum.Stop`.

### Mouse hover

```csharp
if (@event is InputEventMouseMotion motion)
{
    int newHover = HitTest(motion.Position);
    if (newHover != _hoveredCell) { _hoveredCell = newHover; QueueRedraw(); }
}
```

## 3. Layout Containers (Code-Driven, No .tscn)

All container nodes can be created purely in C#:

```csharp
public override void _Ready()
{
    var uiRoot = new VBoxContainer();
    uiRoot.SetAnchorsPreset(Control.LayoutPreset.FullRect);
    AddChild(uiRoot);

    // Top Bar
    var topBar = new HBoxContainer();
    topBar.CustomMinimumSize = new Vector2(0, 30);
    uiRoot.AddChild(topBar);
    topBar.AddChild(new Label { Text = "Root > Backpack" });

    // Main Area (left/right)
    var mainContent = new HBoxContainer();
    mainContent.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
    uiRoot.AddChild(mainContent);

    var leftPanel = new VBoxContainer();
    leftPanel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
    leftPanel.SizeFlagsStretchRatio = 3.0f;  // 75%
    mainContent.AddChild(leftPanel);

    var gridPanel = new GridDrawControl();
    gridPanel.CustomMinimumSize = new Vector2(640, 240);
    gridPanel.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
    leftPanel.AddChild(gridPanel);

    var descLabel = new RichTextLabel();
    descLabel.CustomMinimumSize = new Vector2(0, 120);
    descLabel.BbcodeEnabled = true;
    leftPanel.AddChild(descLabel);

    var rightPanel = new VBoxContainer();
    rightPanel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
    rightPanel.SizeFlagsStretchRatio = 1.0f;  // 25%
    mainContent.AddChild(rightPanel);

    // Bottom Bar
    var bottomBar = new HBoxContainer();
    bottomBar.CustomMinimumSize = new Vector2(0, 30);
    uiRoot.AddChild(bottomBar);
    bottomBar.AddChild(new Label { Text = "[1]Grab [2]Drop [3]Split [4]Sort [5]Acquire" });
}
```

**Key concepts:**
- `SizeFlags.ExpandFill` — node expands to fill available space
- `SizeFlagsStretchRatio` — proportional sizing among siblings (3:1 = 75%/25%)
- `CustomMinimumSize` — minimum pixel size; containers will not shrink below this
- `SetAnchorsPreset(LayoutPreset.FullRect)` — fills parent; essential for root UI node
- `HSplitContainer` adds a draggable divider (unnecessary); use `HBoxContainer` with stretch ratios instead
- `MarginContainer` with `AddThemeConstantOverride("margin_left", 8)` etc. for padding

## 4. Theming and Styling

### Per-node overrides (recommended for first pass)

```csharp
// Colors
label.AddThemeColorOverride("font_color", new Color(0.8f, 1.0f, 0.8f));

// Font size
label.AddThemeFontSizeOverride("font_size", 16);

// Custom font
var font = ResourceLoader.Load<Font>("res://fonts/JetBrainsMono-Regular.ttf");
label.AddThemeFontOverride("font", font);

// StyleBox on PanelContainer
var style = new StyleBoxFlat();
style.BgColor = new Color(0.1f, 0.1f, 0.15f);
style.BorderColor = Colors.White;
style.BorderWidthBottom = style.BorderWidthTop = style.BorderWidthLeft = style.BorderWidthRight = 1;
style.ContentMarginLeft = style.ContentMarginTop = 4;
panel.AddThemeStyleboxOverride("panel", style);
```

Override name strings are type-specific: Label uses `"font_color"`, `"font_size"`, `"font"`; PanelContainer uses `"panel"`.

### Theme resource (for game-wide consistency, later)

```csharp
var theme = new Theme();
theme.SetDefaultFont(font);
theme.SetDefaultFontSize(14);
theme.SetStylebox("panel", "PanelContainer", panelStyle);
theme.SetColor("font_color", "Label", Colors.White);
uiRoot.Theme = theme;  // propagates to all children
```

Per-node overrides take priority over Theme.

### Monospace font

Godot 4 has no built-in monospace font. Bundle a .ttf (JetBrains Mono, Fira Code, etc.) at `res://fonts/`. `ThemeDB.FallbackFont` is proportional. `SystemFont` can request system fonts by name but reliability varies by platform.

## 5. Performance

**32 nodes is fine.** Godot handles hundreds of Control nodes routinely. Even 128 nodes (32 cells × 4 sub-nodes) is well within limits. Performance concerns arise at thousands of nodes or with frequent layout recalculation.

The `_Draw()` approach is recommended for the grid not for performance but for **simplicity** — drawing 32 colored rectangles with text in a loop is less code than constructing/managing 100+ nodes.

`QueueRedraw()` coalesces: calling it 10 times in one frame results in a single `_Draw()` call. The grid only needs redrawing on state changes, not every frame.

## 6. Input Handling

| Method | When called | Use case |
|--------|------------|----------|
| `_Input` | Every input event, before GUI | Global shortcuts |
| `_GuiInput` | Only on focused/hovered Control | Per-widget interaction |
| `_UnhandledInput` | After GUI processing, if unconsumed | Game input that shouldn't conflict with UI fields |

**Recommendation:**
- **Keyboard:** `_UnhandledInput` on GameController with keycode switch
- **Mouse on grid:** `_GuiInput` on the grid Control

```csharp
public override void _UnhandledInput(InputEvent @event)
{
    if (@event is InputEventKey key && key.Pressed && !key.Echo)
    {
        bool handled = key.Keycode switch
        {
            Key.Up    => MoveCursor(Direction.Up),
            Key.Down  => MoveCursor(Direction.Down),
            Key.Left  => MoveCursor(Direction.Left),
            Key.Right => MoveCursor(Direction.Right),
            Key.Key1  => UseTool(ToolType.Grab),
            Key.Key2  => UseTool(ToolType.Drop),
            Key.Key3 when key.ShiftPressed => UseTool(ToolType.ModalSplit),
            Key.Key3  => UseTool(ToolType.QuickSplit),
            Key.Key4  => UseTool(ToolType.Sort),
            Key.Key5  => UseTool(ToolType.AcquireRandom),
            Key.E     => EnterBag(),
            Key.Q     => LeaveBag(),
            Key.Z when key.CtrlPressed => Undo(),
            _ => false
        };
        if (handled) GetViewport().SetInputAsHandled();
    }
}
```

**`key.Echo`:** Godot sends repeated events when a key is held. Check `!key.Echo` for single-press only. Allow echo for arrow keys if you want held-key cursor movement.

## 7. Dynamic Updates

**Recommended: Direct method calls from GameController (simplest, matches TUI approach).**

```csharp
void RefreshUI(GameState state)
{
    _gridPanel.SetState(state.ActiveBag.Grid, state.Cursor);
    _gridPanel.QueueRedraw();

    _breadcrumbLabel.Text = FormatBreadcrumbs(state.Breadcrumbs);
    _descriptionLabel.Text = FormatDescription(state.ActiveBag, state.Cursor);
    _toolbarLabel.Text = FormatToolbar(state);

    _handPanel.SetState(state.HandBag);
    _handPanel.QueueRedraw();
}
```

Godot signals cannot pass arbitrary C# objects (only primitives, strings, Godot Objects, Variants). For Pockets where all panels need the same GameState, direct calls from a central controller are cleaner than signals.

Setting `.Text` on Label/RichTextLabel automatically triggers their internal redraw. `QueueRedraw()` is only needed for custom `_Draw()` controls.

## 8. Recommendation Summary

| Decision | Recommendation | Confidence |
|----------|---------------|------------|
| Grid rendering | Custom `_Draw()` on a single Control | High |
| Side panels | Composed Control nodes (Label, RichTextLabel, PanelContainer) | High |
| Layout | VBoxContainer + HBoxContainer with stretch ratios | High |
| Hit testing | Manual coordinate math in grid's `_GuiInput` | High |
| Keyboard input | `_UnhandledInput` with keycode switch | High |
| Mouse input | `_GuiInput` on the grid Control | High |
| Theming | Per-node overrides for first pass; shared Theme later | High |
| State updates | Direct method calls, full refresh per action | High |
| Font | Bundle a monospace .ttf | High |
| .tscn files | None — all built in `_Ready()` | High |

## 9. Architecture Outline

```
GameController (Node) — _UnhandledInput for keyboard
  UIRoot (VBoxContainer, anchored FullRect)
    TopBar (HBoxContainer, min height 30)
      BreadcrumbLabel (Label)
    MainContent (HBoxContainer, ExpandFill)
      LeftPanel (VBoxContainer, stretch ratio 3)
        GridPanel (Control, custom _Draw)  — _GuiInput for mouse
        DescriptionPanel (PanelContainer > RichTextLabel)
      RightPanel (VBoxContainer, stretch ratio 1)
        HandPanel (Control, custom _Draw)
        QueuePanel (PanelContainer > RichTextLabel)
    BottomBar (HBoxContainer, min height 30)
      ToolbarLabel (Label)
```

## 10. Open Questions

1. **Monospace font**: Bundle JetBrains Mono or similar. `SystemFont` unreliable cross-platform.
2. **High-DPI scaling**: `canvas_items` stretch mode with `keep` aspect is likely right for text-heavy UI. Needs testing.
3. **RichTextLabel BBCode**: `[color=red]text[/color]` for colored description text. Useful for category-colored item names.
4. **Font.GetStringSize()**: For text centering in cells — `font.GetStringSize("text", HorizontalAlignment.Left, -1, fontSize)`. Verify API in 4.6.1.
