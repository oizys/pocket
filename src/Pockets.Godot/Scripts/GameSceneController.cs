using System.Collections.Immutable;
using Godot;
using Pockets.Core;
using Pockets.Core.Data;
using Pockets.Core.Models;
using Pockets.Core.Rendering;
using Pockets.Realization;

namespace Pockets.Godot.Scripts;

/// <summary>
/// Main Godot scene controller. Builds the full UI layout in code (no .tscn),
/// maps Godot input events to GameKey/ClickType, and delegates to Core.GameController.
/// Root is a Control so it participates in the Godot layout system and fills the viewport.
///
/// Slice 2.5 (first animated consumer): the scene owns its own <see cref="PosePlayer"/> and commits every
/// ControllerResult's events to it — window input (keys, clicks, back) and socket commands (via the debug server's
/// commit hook, which carries tick/advanceTime clock commits with their deterministic clock delta). It never draws from
/// the debug handler's pose stream. <see cref="_Process"/> accumulates integer ms for sampling only. D7 snap and the A3
/// undo reset (Rewound) both happen inside the commit, so input handling does nothing extra.
///
/// Clock: this scene injects no SystemGameClock (it never has), so the controller keeps the VirtualGameClock and game
/// time moves only through socket tick/advanceTime. No clock commit can happen outside the socket, which is why
/// parity-godot's "tui vs live-godot poses" cannot diverge at clock commits. If a live clock is ever added, route its
/// syncs through the debug handler (so the handler's stream sees them) or keep it paused while the debug server is driven.
///
/// Slice 2.7 (D2-B): in a free bag (<see cref="GameSession.IsFree"/>, set by the debug <c>setMovement</c>) the arrow keys
/// and the left stick map to a held <c>Steer</c> (8-way, <see cref="SteerInput"/>) on press and release
/// instead of a <c>Move</c> per press. While a body needs it, <see cref="_Process"/> sends a <c>FineTick</c> every 125 ms of
/// frame time and commits each result as a fine tick (no snap, no body track, P2-D13); the body's motion is its fine
/// (Px, Py), re-read on every commit. Under the debug server (a client connected) the frame loop sends no fine ticks: the
/// socket drives them through <c>advanceTime</c>, the same rule as the clock. No interpolation track is added between
/// fine ticks (a policy and golden question, left for Aaron's pass).
/// </summary>
public partial class GameSceneController : Control
{
    private Core.Models.GameController _controller = null!;

    // Slice 2.5: this scene's own drawing timeline and its integer frame clock (sampling only, never the L1 clock).
    private PosePlayer _poses = null!;
    private readonly FrameClock _frameClock = new();

    // Slice 2.7 (D2-B): the debug server (frame fine ticks pause while it has a client), the frame time owed to the next
    // fine tick, the arrows held down, the left stick in milli-units, and the last held direction sent as Steer.
    private DebugWebSocketServer _server = null!;
    private int _fineAccumMs;
    private readonly HashSet<Key> _heldArrows = new();
    private int _stickX, _stickY;
    private Direction8? _steer;

    /// <summary>The stick dead zone in milli-units of the axis range.</summary>
    private const int StickDeadZone = 300;

    /// <summary>At most this many fine ticks per frame, so a long frame stall cannot spiral.</summary>
    private const int MaxFineTicksPerFrame = 4;

    // UI references
    private GridDrawControl _gridPanel = null!;
    private GridDrawControl _handPanel = null!;
    private Button _backButton = null!;
    private Label _breadcrumbLabel = null!;
    private Label _clockLabel = null!;   // Slice 5: top-bar clock readout
    private Label _queueLabel = null!;   // Slice 7: thin action-queue + minimap chrome
    private Label _descriptionLabel = null!;
    private Label _toolbarLabel = null!;
    private Label _handLabel = null!;
    private Label _statusLabel = null!;

    // Dialogue box (Slice 2): bottom-third overlay, colored-rect portrait placeholder + text.
    private Control _dialoguePanel = null!;
    private ColorRect _dialoguePortrait = null!;
    private Label _dialogueText = null!;
    private PanelContainer _recipeMenuPanel = null!;  // playtest: thin modal recipe menu
    private Label _recipeMenuLabel = null!;

    public override void _Ready()
    {
        InitializeGame();
        _poses = new PosePlayer(_controller.Session.Current, FineView.Of(_controller.Session.Fine));
        BuildUI();
        RefreshUI();
        StartDebugServer();
    }

    private void StartDebugServer()
    {
        _server = new DebugWebSocketServer();
        _server.Controller = _controller;
        _server.OnCommit = CommitPoses;
        AddChild(_server);
    }

    /// <summary>
    /// Called by DebugWebSocketServer (via CallDeferred) to refresh UI after a remote command.
    /// </summary>
    public void RequestRefreshUI() => RefreshUI();

    /// <summary>
    /// Commits one ControllerResult's events to the drawing timeline at clock delta <paramref name="atMs"/> (0 for
    /// input; the debug handler's AtMs for socket commands), with the session's fine view as the TUI driver does, then
    /// refreshes the UI so grid and poses change together.
    /// </summary>
    public void CommitPoses(ControllerResult result, int atMs)
    {
        var session = _controller.Session;
        _poses.Commit(result.Events, session.Current, atMs, FineView.Of(session.Fine));
        RefreshUI();
    }

    /// <summary>
    /// Commits one fine tick's result (Slice 2.7): as a fine commit, which neither snaps nor adds body tracks (P2-D13),
    /// so a concurrent pop or slide plays on while the body glides. Raises the tree cue on a derived bump.
    /// </summary>
    private void CommitFineTick(ControllerResult result)
    {
        var session = _controller.Session;
        _poses.Commit(result.Events, session.Current, 0, FineView.Of(session.Fine), fineTick: true);
        RefreshUI();
        ShowBumpCue(result);
    }

    /// <summary>
    /// Advances the drawing timeline by this frame's integer ms and redraws while it is animating. Slice 2.7: first sends
    /// the fine ticks this frame owes (one per 125 ms while a body needs them, none while the debug server has a client).
    /// </summary>
    public override void _Process(double delta)
    {
        var deltaMs = _frameClock.Tick((long)Time.GetTicksMsec());
        DriveFineTicks(deltaMs);
        if (_poses.Advance(deltaMs))
            PushPoses();
    }

    /// <summary>
    /// The frame-driven D2-B scheduler: accumulates <paramref name="deltaMs"/> and sends a FineTick per
    /// <see cref="GameSession.FineTickLengthMs"/>, while the session has a body to integrate and no socket client is
    /// driving it (then the socket's advanceTime is the only source, so the handler's stream misses nothing).
    /// </summary>
    private void DriveFineTicks(int deltaMs)
    {
        if (_server.HasPeers || !_controller.Session.NeedsFineTick)
        {
            _fineAccumMs = 0;
            return;
        }
        _fineAccumMs += deltaMs;
        for (var i = 0; i < MaxFineTicksPerFrame && _fineAccumMs >= GameSession.FineTickLengthMs; i++)
        {
            _fineAccumMs -= GameSession.FineTickLengthMs;
            CommitFineTick(_controller.FineTick());
        }
        if (_fineAccumMs > GameSession.FineTickLengthMs)
            _fineAccumMs = GameSession.FineTickLengthMs;
    }

    /// <summary>
    /// True when B's bag takes steering (a free world bag in realtime) and the arrows would reach B's cursor: no inline
    /// split or recipe menu owns them and B has focus. Otherwise arrows are presses mapped to Move, as before.
    /// </summary>
    private bool Steering =>
        _controller.Session.IsFree(LocationId.B) && _controller.Session.SplitMode is null
        && _controller.Session.RecipeMenu is null && _controller.Focus == LocationId.B;

    /// <summary>
    /// Sends the held direction (arrows first, else the stick) as a Steer when it differs from the last one sent, and
    /// commits it as input (it snaps, D7). Returns false when B is not steering, after forgetting the last direction.
    /// </summary>
    private bool UpdateSteer()
    {
        if (!Steering)
        {
            _steer = null;
            return false;
        }
        var dir = SteerInput.FromArrows(_heldArrows.Contains(Key.Up), _heldArrows.Contains(Key.Down),
                      _heldArrows.Contains(Key.Left), _heldArrows.Contains(Key.Right))
                  ?? SteerInput.FromStick(_stickX, _stickY, StickDeadZone);
        if (dir != _steer)
        {
            _steer = dir;
            CommitPoses(_controller.Steer(LocationId.B, dir), 0);
        }
        return true;
    }

    /// <summary>Hands the current sample to the grid (active bag, cursor of B) and hand panels.</summary>
    private void PushPoses()
    {
        var state = _controller.Session.Current;
        var drawn = _poses.Poses;
        _gridPanel.SetPoses(drawn, state.ActiveBag.Id, LocationId.B);
        _handPanel.SetPoses(drawn, state.HandBag.Id, null);
    }

    private void InitializeGame()
    {
        // Load content from data directory (walk up to find project root)
        var dir = new System.IO.DirectoryInfo(ProjectSettings.GlobalizePath("res://"));
        // In Godot, res:// is the project folder — walk up to find data/
        while (dir is not null && !System.IO.Directory.Exists(System.IO.Path.Combine(dir.FullName, "data")))
            dir = dir.Parent;

        GameSession session;

        if (dir is not null)
        {
            var dataPath = System.IO.Path.Combine(dir.FullName, "data");
            var registry = ContentLoader.LoadFromDirectory(dataPath);
            var dialogue = DialogueLoader.LoadFromDirectory(dataPath);
            // Shared, seeded demo profile — identical to the TUI build's start state, tick mode,
            // and dialogue beats (see Pockets.Core.GameInitializer.CreateDemoProfile).
            session = GameInitializer.CreateDemoProfile(registry, dialogue: dialogue).NewSession();
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
            // Seeded: the frontend owns no randomness (Slice 1.1), even on the no-data fallback.
            var gameState = GameInitializer.CreateRandomStage1Game(types, new Random(GameInitializer.DemoSeed));
            session = GameSession.New(gameState, ImmutableArray<Recipe>.Empty, TickMode.Rogue);
        }

        _controller = new Core.Models.GameController(session);

        var activeGrid = session.Current.ActiveBag.Grid;
        GD.Print($"Game initialized: {activeGrid.Columns}x{activeGrid.Rows} grid, " +
                 $"{session.Current.ItemTypes.Length} item types");
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

        // Slice 5: top-bar clock readout — "it was always running" (realtime mode). Visible only once
        // NoticeClock has materialized the ClockReadout chrome.
        _clockLabel = new Label { Text = "" };
        _clockLabel.AddThemeFontSizeOverride("font_size", 16);
        _clockLabel.AddThemeColorOverride("font_color", new Color(0.8f, 0.85f, 0.7f));
        _clockLabel.VerticalAlignment = VerticalAlignment.Center;
        topBarContent.AddChild(_clockLabel);

        // Slice 7: thin action-queue + minimap chrome (the real queue/radar are the TUI's — this is the
        // Godot thin equivalent). Visible only once FirstTimedAction / CompassCrafted materialize them.
        _queueLabel = new Label { Text = "" };
        _queueLabel.AddThemeFontSizeOverride("font_size", 16);
        _queueLabel.AddThemeColorOverride("font_color", new Color(0.7f, 0.85f, 0.8f));
        _queueLabel.VerticalAlignment = VerticalAlignment.Center;
        topBarContent.AddChild(_queueLabel);

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

        // --- Dialogue box (Slice 2): bottom-third overlay, drawn over everything else. ---
        // Anchored to the bottom third of the viewport; colored-rect portrait placeholder on the
        // left, the current line to its right. Placeholder art only (no asset pipeline).
        _dialoguePanel = new PanelContainer();
        _dialoguePanel.SetAnchorsPreset(LayoutPreset.BottomWide);
        _dialoguePanel.AnchorTop = 0.66f;
        _dialoguePanel.Visible = false;
        AddPanelStyle((PanelContainer)_dialoguePanel, new Color(0.05f, 0.05f, 0.08f));
        AddChild(_dialoguePanel);

        var dialogueRow = new HBoxContainer();
        dialogueRow.AddThemeConstantOverride("separation", 12);
        _dialoguePanel.AddChild(dialogueRow);

        _dialoguePortrait = new ColorRect();
        _dialoguePortrait.CustomMinimumSize = new Vector2(72, 72);
        _dialoguePortrait.Color = new Color(0.4f, 0.45f, 0.6f);
        dialogueRow.AddChild(_dialoguePortrait);

        _dialogueText = new Label();
        _dialogueText.AddThemeFontSizeOverride("font_size", 16);
        _dialogueText.AddThemeColorOverride("font_color", new Color(0.92f, 0.92f, 0.92f));
        _dialogueText.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _dialogueText.VerticalAlignment = VerticalAlignment.Center;
        _dialogueText.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        dialogueRow.AddChild(_dialogueText);

        // Modal recipe menu (playtest feature): a thin but functional centered overlay listing the
        // craftable recipes with the selection marked. Drawn from Core state; ↑/↓ move, Enter sets,
        // Esc/Q closes (handled by the controller). Aaron reversed the no-modal rule (see drift report).
        _recipeMenuPanel = new PanelContainer();
        _recipeMenuPanel.SetAnchorsPreset(LayoutPreset.Center);
        _recipeMenuPanel.Visible = false;
        AddPanelStyle(_recipeMenuPanel, new Color(0.08f, 0.08f, 0.12f));
        AddChild(_recipeMenuPanel);

        _recipeMenuLabel = new Label();
        _recipeMenuLabel.AddThemeFontSizeOverride("font_size", 16);
        _recipeMenuLabel.AddThemeColorOverride("font_color", new Color(0.9f, 0.9f, 0.8f));
        _recipeMenuPanel.AddChild(_recipeMenuLabel);
    }

    /// <summary>
    /// Maps a portrait-emotion tag to a placeholder rect color (no asset pipeline in-demo; the TUI
    /// build uses a glyph face for the same tags). Unknown tags fall back to neutral.
    /// </summary>
    private static Color PortraitColor(string emotion) => emotion.ToLowerInvariant() switch
    {
        "groggy" => new Color(0.35f, 0.38f, 0.5f),
        "puzzled" => new Color(0.5f, 0.42f, 0.3f),
        _ => new Color(0.4f, 0.45f, 0.6f)
    };

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
            Key.R => GameKey.RecipeMenu,
            Key.C => GameKey.Peek,
            Key.Q => GameKey.LeaveBag,
            Key.Enter or Key.KpEnter => GameKey.Confirm, // recipe-menu select / split commit
            Key.Escape => GameKey.Cancel,                // recipe-menu / split cancel
            Key.Z when key.CtrlPressed => GameKey.Undo,
            _ => null
        };
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        // Slice 2.7: the left stick (quantized to 8-way) steers a free body; it never maps to a Move.
        if (@event is InputEventJoypadMotion motion && motion.Axis is JoyAxis.LeftX or JoyAxis.LeftY)
        {
            var milli = (int)System.Math.Round(motion.AxisValue * 1000);
            if (motion.Axis == JoyAxis.LeftX) _stickX = milli; else _stickY = milli;
            if (UpdateSteer()) GetViewport().SetInputAsHandled();
            return;
        }

        // Slice 2.7: arrow press and release are tracked always; in a free bag they steer instead of moving.
        if (@event is InputEventKey arrow && arrow.Keycode is Key.Up or Key.Down or Key.Left or Key.Right && !arrow.Echo)
        {
            if (arrow.Pressed) _heldArrows.Add(arrow.Keycode); else _heldArrows.Remove(arrow.Keycode);
            if (UpdateSteer())
            {
                GetViewport().SetInputAsHandled();
                return;
            }
        }
        else if (@event is InputEventKey { Keycode: Key.Up or Key.Down or Key.Left or Key.Right, Echo: true } && Steering)
        {
            GetViewport().SetInputAsHandled(); // held-key repeat: the steer is already held
            return;
        }

        if (@event is not InputEventKey key || !key.Pressed)
            return;

        var gameKey = MapKey(key);
        if (gameKey is null) return;

        var result = _controller.HandleKey(gameKey.Value);
        CommitPoses(result, 0); // every mapped key is input: it commits (and so snaps, D7) even when unhandled
        if (result.Handled)
        {
            ShowBumpCue(result);
            GetViewport().SetInputAsHandled();
        }
    }

    /// <summary>
    /// Thin equivalent of the TUI shake/flash, from a result's Bumped event (Slice 1.6): on a refused enter-only peek or
    /// a tree refusal (a key Move, or since 2.7 an integrator-derived Move), surface the cue on the status line for this
    /// frame (the next RefreshUI overwrites it). The fire-once beats render in the shared dialogue box; this fires on
    /// every refusal.
    /// </summary>
    private void ShowBumpCue(ControllerResult result)
    {
        switch (result.Events.OfType<Core.Sim.SimEvent.Bumped>().FirstOrDefault()?.Reason)
        {
            case Core.Sim.BumpReason.FailedPeek:
                _statusLabel.Text = "✕ Enter-only — can't just peek. Press E to enter.\n" + _statusLabel.Text;
                break;
            case Core.Sim.BumpReason.Unnavigable: // Slice 6: a cursor move refused by a tree (fires on every bump).
                _statusLabel.Text = "✕ A tree. Solid — no way through.\n" + _statusLabel.Text;
                break;
        }
    }

    private void RefreshUI()
    {
        var state = _controller.Session.Current;

        // Grid — chrome-as-state: the room grid draws only once it has materialized (demo frame 0
        // is dialogue-box-only). Non-demo profiles are everything-on, so this is identical to before.
        _gridPanel.SetState(state.ActiveBag.Grid, state.Cursor.Position);
        _gridPanel.Visible = state.Ui.Has(ChromeElement.Grid);

        // Fullness pips (Slice 5): once the eye core is slotted, every bag cell shows its Core-computed
        // pip. Both frontends render only from this Core state.
        var showPips = state.Ui.Has(ChromeElement.FullnessPips);
        _gridPanel.SetPips(showPips
            ? i =>
            {
                var cells = state.ActiveBag.Grid.Cells;
                if (i < 0 || i >= cells.Length || cells[i].IsEmpty) return null;
                return Fullness.Of(state.Store, cells[i].Stack!);
            }
            : (System.Func<int, FullnessPip?>?)null);

        // Clock readout (Slice 5) — gated by NoticeClock (ClockReadout chrome).
        _clockLabel.Text = $"⏱ {ViewModelSerializer.FormatClock(_controller.Session.Elapsed)}"
            + (state.Ui.Has(ChromeElement.ShrineView) ? "  ◈ Shrine" : "");
        _clockLabel.Visible = state.Ui.Has(ChromeElement.ClockReadout) || state.Ui.Has(ChromeElement.ShrineView);

        // Action queue + minimap (Slice 7) — thin: the count of in-flight crafts and the lit-wedge count.
        // Each half draws only once its ledger flag is on (FirstTimedAction / CompassCrafted).
        var activeCrafts = state.Store.All.Count(b => b.FacilityState?.RecipeId is not null);
        var queueText = "";
        if (state.Ui.Has(ChromeElement.ActionQueue))
            queueText += $"⚙ {activeCrafts}  ";
        if (state.Ui.Has(ChromeElement.Minimap))
            queueText += $"◉ {state.ZonesReached.Count}/12";
        _queueLabel.Text = queueText.TrimEnd();
        _queueLabel.Visible = state.Ui.Has(ChromeElement.ActionQueue) || state.Ui.Has(ChromeElement.Minimap);

        // Hand
        _handPanel.SetState(state.HandBag.Grid, new Position(0, 0));
        _handLabel.Text = RenderHelpers.FormatHandSummary(state);

        // Chrome-as-state: each rendered element draws only when its UiLedger flag is on.
        // Non-demo profiles are everything-on (UiLedger.AllPresent) so this is identical to
        // before; the demo profile grows chrome in through gameplay triggers. (The C/W look-in
        // panels are not rendered by this frontend yet — see design/parity-drift-report.md #4;
        // the ledger already governs their existence for when they land.)

        // Breadcrumbs + back button. The env header (palette + Quiet+ glyph approximation for the Quiet 1
        // wilderness; empty for ordinary bags) is appended unconditionally — FormatEnvHeader carries its
        // own leading separator. This is a label/text placeholder for the real SVG texture (deferred
        // Godot glyph import, design/glyphs.md).
        var crumbs = state.BreadcrumbPath;
        _breadcrumbLabel.Text = string.Join(" > ", crumbs)
            + RenderHelpers.FormatEnvHeader(state.ActiveBag);
        _breadcrumbLabel.Visible = state.Ui.Has(ChromeElement.Breadcrumbs);
        _backButton.Visible = state.IsNested;

        // Description
        _descriptionLabel.Text = RenderHelpers.DescribeCursorItem(state);
        _descriptionLabel.Visible = state.Ui.Has(ChromeElement.DescriptionPane);

        // Toolbar (Slice 3) — the fixed inventory as the bottom bar. Aligned to the same model the TUI
        // T panel renders from (RenderHelpers.FormatToolbarSummary over the depth-invariant T bag), so
        // the demo's pickups appear here identically on both frontends. Only draws once FirstPickup has
        // materialized the toolbar chrome (ledger-gated, like every other panel).
        _toolbarLabel.Text = RenderHelpers.FormatToolbarSummary(state);
        _toolbarLabel.Visible = state.Ui.Has(ChromeElement.Toolbar);

        // Dialogue box — draw the active line (resolved from the beat book) or hide it.
        var dialogue = state.Dialogue;
        var beat = dialogue.IsActive ? _controller.Session.Beats.Get(dialogue.ActiveBeatId!) : null;
        if (beat is not null && dialogue.LineIndex < beat.Lines.Length)
        {
            var line = beat.Lines[dialogue.LineIndex];
            _dialogueText.Text = line.Text;
            _dialoguePortrait.Color = PortraitColor(line.Portrait);
            _dialoguePanel.Visible = true;
        }
        else
        {
            _dialoguePanel.Visible = false;
        }

        // Modal recipe menu (playtest feature): a thin functional overlay drawn from Core state.
        var menu = _controller.Session.RecipeMenu;
        if (menu is not null)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"Craft at the {menu.FacilityEnvironment}:");
            if (menu.RecipeNames.IsEmpty)
                sb.AppendLine("  (no recipes known yet)");
            else
                for (var i = 0; i < menu.RecipeNames.Length; i++)
                    sb.AppendLine((i == menu.SelectedIndex ? "> " : "  ") + menu.RecipeNames[i]);
            sb.Append("↑/↓ select · Enter set · Esc close");
            _recipeMenuLabel.Text = sb.ToString();
            _recipeMenuPanel.Visible = true;
        }
        else
        {
            _recipeMenuPanel.Visible = false;
        }

        // Action log (last 8 entries)
        var logEntries = _controller.Session.ActionLog.TakeLast(8);
        _statusLabel.Text = string.Join("\n", logEntries);

        PushPoses();
    }

    private void OnBackButtonPressed()
    {
        CommitPoses(_controller.HandleBackClick(), 0);
    }

    private void OnGridCellClicked(int index, int button)
    {
        var pos = Pockets.Core.Models.Position.FromIndex(index, _controller.Session.Current.ActiveBag.Grid.Columns);
        var clickType = button == (int)MouseButton.Left ? ClickType.Primary : ClickType.Secondary;
        CommitPoses(_controller.HandleGridClick(pos, clickType), 0);
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
