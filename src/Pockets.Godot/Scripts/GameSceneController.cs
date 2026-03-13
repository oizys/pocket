using System.Collections.Immutable;
using Godot;
using Pockets.Core;
using Pockets.Core.Data;
using Pockets.Core.Models;
using Pockets.Core.Rendering;

namespace Pockets.Godot.Scripts;

/// <summary>
/// Main Godot scene controller. Builds the full UI layout in code (no .tscn),
/// maps Godot input events to GameKey/ClickType, and delegates to Core.GameController.
/// Root is a Control so it participates in the Godot layout system and fills the viewport.
/// </summary>
public partial class GameSceneController : Control
{
    private Core.Models.GameController _controller = null!;
    private readonly Random _rng = new();

    // UI references
    private GridDrawControl _gridPanel = null!;
    private GridDrawControl _handPanel = null!;
    private Button _backButton = null!;
    private Label _breadcrumbLabel = null!;
    private Label _descriptionLabel = null!;
    private Label _toolbarLabel = null!;
    private Label _handLabel = null!;
    private Label _statusLabel = null!;

    public override void _Ready()
    {
        InitializeGame();
        BuildUI();
        RefreshUI();
        StartDebugServer();
    }

    private void StartDebugServer()
    {
        var server = new DebugWebSocketServer();
        server.Controller = _controller;
        AddChild(server);
    }

    /// <summary>
    /// Called by DebugWebSocketServer (via CallDeferred) to refresh UI after a remote command.
    /// </summary>
    public void RequestRefreshUI() => RefreshUI();

    private void InitializeGame()
    {
        // Load content from data directory (walk up to find project root)
        var dir = new System.IO.DirectoryInfo(ProjectSettings.GlobalizePath("res://"));
        // In Godot, res:// is the project folder — walk up to find data/
        while (dir is not null && !System.IO.Directory.Exists(System.IO.Path.Combine(dir.FullName, "data")))
            dir = dir.Parent;

        GameState gameState;
        ImmutableArray<Recipe> recipes;

        if (dir is not null)
        {
            var dataPath = System.IO.Path.Combine(dir.FullName, "data");
            var registry = ContentLoader.LoadFromDirectory(dataPath);
            (gameState, recipes) = GameInitializer.CreateFromRegistry(registry, _rng);
        }
        else
        {
            // Fallback: create a minimal game with hardcoded items
            GD.PrintErr("Could not find data/ directory, using fallback items");
            var types = ImmutableArray.Create(
                new ItemType("Plain Rock", Category.Material, true),
                new ItemType("Rough Wood", Category.Material, true),
                new ItemType("Iron Ore", Category.Material, true),
                new ItemType("Healing Salve", Category.Medicine, true),
                new ItemType("Stone Axe", Category.Weapon, false)
            );
            gameState = GameInitializer.CreateRandomStage1Game(types, _rng);
            recipes = ImmutableArray<Recipe>.Empty;
        }

        var session = GameSession.New(gameState, recipes);
        _controller = new Core.Models.GameController(session);

        GD.Print($"Game initialized: {gameState.ActiveBag.Grid.Columns}x{gameState.ActiveBag.Grid.Rows} grid, " +
                 $"{gameState.ItemTypes.Length} item types");
    }

    private void BuildUI()
    {
        // Root control fills the entire viewport
        SetAnchorsPreset(LayoutPreset.FullRect);

        var uiRoot = new VBoxContainer();
        uiRoot.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(uiRoot);

        // --- Top Bar: back button + breadcrumbs ---
        var topBar = new PanelContainer();
        AddPanelStyle(topBar, new Color(0.08f, 0.08f, 0.1f));
        uiRoot.AddChild(topBar);

        var topBarContent = new HBoxContainer();
        topBar.AddChild(topBarContent);

        _backButton = new Button { Text = "<  Back" };
        _backButton.AddThemeFontSizeOverride("font_size", 14);
        _backButton.CustomMinimumSize = new Vector2(80, 0);
        _backButton.Pressed += OnBackButtonPressed;
        topBarContent.AddChild(_backButton);

        _breadcrumbLabel = new Label { Text = "Root" };
        _breadcrumbLabel.AddThemeFontSizeOverride("font_size", 16);
        _breadcrumbLabel.AddThemeColorOverride("font_color", new Color(0.7f, 0.8f, 1f));
        _breadcrumbLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _breadcrumbLabel.VerticalAlignment = VerticalAlignment.Center;
        topBarContent.AddChild(_breadcrumbLabel);

        // --- Main content: left (grid row + description) | right (action log) ---
        var mainContent = new HBoxContainer();
        mainContent.SizeFlagsVertical = SizeFlags.ExpandFill;
        uiRoot.AddChild(mainContent);

        // Left panel: grid row on top, description stretches below
        var leftPanel = new VBoxContainer();
        leftPanel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        leftPanel.SizeFlagsStretchRatio = 3.0f;
        mainContent.AddChild(leftPanel);

        // Grid row: inventory grid (fixed) + hand cell (fixed) side by side
        var gridRow = new HBoxContainer();
        leftPanel.AddChild(gridRow);

        _gridPanel = new GridDrawControl();
        _gridPanel.CellClicked += OnGridCellClicked;
        _gridPanel.CellHovered += OnGridCellHovered;
        gridRow.AddChild(_gridPanel);

        // Hand: fixed single-cell grid to the right of inventory
        var handBox = new VBoxContainer();
        handBox.AddThemeConstantOverride("separation", 2);
        gridRow.AddChild(handBox);

        var handTitleLabel = new Label { Text = "Hand" };
        handTitleLabel.AddThemeFontSizeOverride("font_size", 12);
        handTitleLabel.AddThemeColorOverride("font_color", new Color(0.6f, 0.7f, 0.8f));
        handTitleLabel.HorizontalAlignment = HorizontalAlignment.Center;
        handBox.AddChild(handTitleLabel);

        _handPanel = new GridDrawControl();
        handBox.AddChild(_handPanel);

        _handLabel = new Label { Text = "" };
        _handLabel.AddThemeFontSizeOverride("font_size", 11);
        _handLabel.AddThemeColorOverride("font_color", new Color(0.6f, 0.6f, 0.6f));
        _handLabel.HorizontalAlignment = HorizontalAlignment.Center;
        handBox.AddChild(_handLabel);

        // Description panel: stretches to fill remaining vertical space
        var descPanel = new PanelContainer();
        descPanel.CustomMinimumSize = new Vector2(0, 60);
        descPanel.SizeFlagsVertical = SizeFlags.ExpandFill;
        AddPanelStyle(descPanel, new Color(0.1f, 0.1f, 0.12f));
        leftPanel.AddChild(descPanel);

        _descriptionLabel = new Label();
        _descriptionLabel.AddThemeFontSizeOverride("font_size", 14);
        _descriptionLabel.AddThemeColorOverride("font_color", new Color(0.85f, 0.85f, 0.85f));
        _descriptionLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _descriptionLabel.VerticalAlignment = VerticalAlignment.Top;
        descPanel.AddChild(_descriptionLabel);

        // Right panel: action log, stretches to fill
        var rightPanel = new PanelContainer();
        rightPanel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        rightPanel.SizeFlagsStretchRatio = 1.0f;
        AddPanelStyle(rightPanel, new Color(0.08f, 0.08f, 0.1f));
        mainContent.AddChild(rightPanel);

        _statusLabel = new Label { Text = "" };
        _statusLabel.AddThemeFontSizeOverride("font_size", 12);
        _statusLabel.AddThemeColorOverride("font_color", new Color(0.5f, 0.5f, 0.6f));
        _statusLabel.VerticalAlignment = VerticalAlignment.Top;
        _statusLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        rightPanel.AddChild(_statusLabel);

        // --- Bottom Bar: toolbar / hotkeys ---
        var bottomBar = new PanelContainer();
        AddPanelStyle(bottomBar, new Color(0.08f, 0.08f, 0.1f));
        uiRoot.AddChild(bottomBar);

        _toolbarLabel = new Label();
        _toolbarLabel.AddThemeFontSizeOverride("font_size", 13);
        _toolbarLabel.AddThemeColorOverride("font_color", new Color(0.6f, 0.7f, 0.6f));
        bottomBar.AddChild(_toolbarLabel);
    }

    /// <summary>
    /// Maps Godot InputEventKey to abstract GameKey, or null if not a game key.
    /// Arrow keys allow echo (held-key repeat); all others are single-press only.
    /// </summary>
    private static GameKey? MapKey(InputEventKey key)
    {
        // Arrow keys: always map (echo allowed for held movement)
        var arrowKey = key.Keycode switch
        {
            Key.Up => (GameKey?)GameKey.Up,
            Key.Down => (GameKey?)GameKey.Down,
            Key.Left => (GameKey?)GameKey.Left,
            Key.Right => (GameKey?)GameKey.Right,
            _ => null
        };
        if (arrowKey is not null) return arrowKey;

        // All other keys: single-press only (no echo)
        if (key.Echo) return null;

        return key.Keycode switch
        {
            Key.Key1 or Key.E => GameKey.Primary,
            Key.Key2 => GameKey.Secondary,
            Key.Key3 when !key.ShiftPressed => GameKey.QuickSplit,
            Key.Key4 => GameKey.Sort,
            Key.Key5 => GameKey.AcquireRandom,
            Key.R => GameKey.CycleRecipe,
            Key.Q => GameKey.LeaveBag,
            Key.Z when key.CtrlPressed => GameKey.Undo,
            _ => null
        };
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is not InputEventKey key || !key.Pressed)
            return;

        var gameKey = MapKey(key);
        if (gameKey is null) return;

        var result = _controller.HandleKey(gameKey.Value, _rng);
        if (result.Handled)
        {
            RefreshUI();
            GetViewport().SetInputAsHandled();
        }
    }

    private void RefreshUI()
    {
        var state = _controller.Session.Current;

        // Grid
        _gridPanel.SetState(state.ActiveBag.Grid, state.Cursor.Position);

        // Hand
        _handPanel.SetState(state.HandBag.Grid, new Position(0, 0));
        _handLabel.Text = RenderHelpers.FormatHandSummary(state);

        // Breadcrumbs + back button
        var crumbs = state.BreadcrumbPath;
        _breadcrumbLabel.Text = string.Join(" > ", crumbs);
        _backButton.Visible = state.IsNested;

        // Description
        _descriptionLabel.Text = RenderHelpers.DescribeCursorItem(state);

        // Toolbar
        _toolbarLabel.Text = state.HasItemsInHand
            ? "[1]Grab [2]Drop [3]Split [4]Sort [5]Acquire  |  [E]Enter [Q]Leave [Ctrl-Z]Undo"
            : "[1]Grab [2]Drop [3]Split [4]Sort [5]Acquire  |  [E]Enter [Q]Leave [Ctrl-Z]Undo";

        // Action log (last 8 entries)
        var logEntries = _controller.Session.ActionLog.TakeLast(8);
        _statusLabel.Text = string.Join("\n", logEntries);
    }

    private void OnBackButtonPressed()
    {
        _controller.HandleBackClick();
        RefreshUI();
    }

    private void OnGridCellClicked(int index, int button)
    {
        var pos = Pockets.Core.Models.Position.FromIndex(index, _controller.Session.Current.ActiveBag.Grid.Columns);
        var clickType = button == (int)MouseButton.Left ? ClickType.Primary : ClickType.Secondary;
        _controller.HandleGridClick(pos, clickType);
        RefreshUI();
    }

    private void OnGridCellHovered(int index)
    {
        if (index < 0) return;
        // Update description for hovered cell
        var grid = _controller.Session.Current.ActiveBag.Grid;
        if (index < grid.Cells.Length)
        {
            var cell = grid.Cells[index];
            if (!cell.IsEmpty)
            {
                var stack = cell.Stack!;
                var type = stack.ItemType;
                var kind = type.IsStackable
                    ? $"Stackable {stack.Count}/{type.EffectiveMaxStackSize}"
                    : "Unique";
                var desc = string.IsNullOrEmpty(type.Description) ? "" : $"\n{type.Description}";
                _descriptionLabel.Text = $"{type.Name} | {type.Category} | {kind}{desc}";
            }
            else
            {
                _descriptionLabel.Text = "(empty)";
            }
        }
    }

    private static void AddPanelStyle(PanelContainer panel, Color bgColor)
    {
        var style = new StyleBoxFlat();
        style.BgColor = bgColor;
        style.ContentMarginLeft = 8;
        style.ContentMarginRight = 8;
        style.ContentMarginTop = 4;
        style.ContentMarginBottom = 4;
        panel.AddThemeStyleboxOverride("panel", style);
    }
}
