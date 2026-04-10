using System.Collections.Immutable;
using Terminal.Gui;
using Pockets.Core.Models;
using Pockets.App.Rendering;

namespace Pockets.App.Views;

/// <summary>
/// Main game window. Manages multiple panels (B, C, W, T, H) with focus tracking.
/// Delegates input handling to GameController.
/// </summary>
public class GameView : Window
{
    private readonly GameController _controller;
    private readonly GridPanel _gridPanel;       // B panel (inventory)
    private readonly RightPanel _rightPanel;     // Action log
    private readonly BagPanelView _containerPanel; // C panel
    private readonly BagPanelView _worldPanel;     // W panel
    private readonly BagPanelView _toolbarPanel;   // T panel
    private readonly Random _rng = new();
    private object? _tickTimer;
    private readonly bool _enableTickTimer;

    private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(1);

    /// <summary>X offset where all bag panels start (after H hand cell + gap).</summary>
    private const int PanelXOffset = CellRenderer.CellWidth + 4;

    public GameView(
        GameState initialState,
        ImmutableArray<Recipe> recipes = default,
        ImmutableDictionary<string, ImmutableArray<string>>? facilityRecipeMap = null,
        bool enableTickTimer = true) : base("Pockets")
    {
        _enableTickTimer = enableTickTimer;
        X = 0;
        Y = 0;
        Width = Dim.Fill();
        Height = Dim.Fill();

        ColorScheme = new ColorScheme
        {
            Normal = Application.Driver.MakeAttribute(Color.White, Color.Black),
            Focus = Application.Driver.MakeAttribute(Color.White, Color.Black),
            HotNormal = Application.Driver.MakeAttribute(Color.White, Color.Black),
            HotFocus = Application.Driver.MakeAttribute(Color.White, Color.Black)
        };

        var session = facilityRecipeMap is not null
            ? GameSession.New(initialState, recipes, facilityRecipeMap)
            : recipes.IsDefaultOrEmpty
                ? GameSession.New(initialState)
                : GameSession.New(initialState, recipes);

        _controller = new GameController(session);

        // B panel (main inventory — existing GridPanel)
        _gridPanel = new GridPanel(_controller.Session.Current);
        if (!recipes.IsDefaultOrEmpty)
            _gridPanel.SetRecipes(recipes);
        _gridPanel.SetFacilityRecipeMap(facilityRecipeMap);

        // Right panel (action log)
        _rightPanel = new RightPanel();

        // C panel (container/facility) — starts hidden
        _containerPanel = new BagPanelView(LocationId.C, "Container");

        // W panel (world/wilderness) — starts hidden
        _worldPanel = new BagPanelView(LocationId.W, "World");

        // T panel (toolbar) — always visible
        _toolbarPanel = new BagPanelView(LocationId.T, "Toolbar");

        // Wire mouse events
        _gridPanel.GetGridView().GridCellClicked += OnGridCellClicked;
        _gridPanel.GetGridView().GridCellRightClicked += OnGridCellRightClicked;
        _gridPanel.GetGridView().MouseStateChanged += OnMouseStateChanged;
        _gridPanel.GetBackButton().BackClicked += OnBackClicked;

        _containerPanel.CellClicked += OnPanelCellClicked;
        _worldPanel.CellClicked += OnPanelCellClicked;
        _toolbarPanel.CellClicked += OnPanelCellClicked;

        Add(_containerPanel, _gridPanel, _worldPanel, _toolbarPanel, _rightPanel);

        // Initial layout
        UpdatePanelLayout();

        if (_enableTickTimer)
        {
            Application.MainLoop.AddIdle(() =>
            {
                StartTickTimer();
                return false;
            });
        }
    }

    private void StartTickTimer()
    {
        if (_controller.Session.TickMode != TickMode.Realtime || _tickTimer is not null)
            return;

        _tickTimer = Application.MainLoop.AddTimeout(TickInterval, _ =>
        {
            var result = _controller.Tick();
            if (result.Handled)
                UpdateUI();
            return true;
        });
    }

    private static GameKey? MapKey(KeyEvent keyEvent)
    {
        return keyEvent.Key switch
        {
            Key.CursorUp or (Key)'w' or (Key)'W' => GameKey.Up,
            Key.CursorDown or (Key)'s' or (Key)'S' => GameKey.Down,
            Key.CursorLeft or (Key)'a' or (Key)'A' => GameKey.Left,
            Key.CursorRight or (Key)'d' or (Key)'D' => GameKey.Right,
            Key.Z | Key.CtrlMask => GameKey.Undo,
            (Key)'1' or (Key)'e' or (Key)'E' => GameKey.Primary,
            (Key)'2' => GameKey.Secondary,
            (Key)'3' => GameKey.QuickSplit,
            (Key)'4' => GameKey.Sort,
            (Key)'5' => GameKey.AcquireRandom,
            (Key)'r' or (Key)'R' => GameKey.CycleRecipe,
            (Key)'q' or (Key)'Q' => GameKey.LeaveBag,
            Key.Tab => GameKey.FocusNext,
            Key.BackTab => GameKey.FocusPrev,
            _ => null
        };
    }

    public override bool ProcessKey(KeyEvent keyEvent)
    {
        // Shift-3 (#) = Modal Split dialog
        if (keyEvent.Key == (Key)'#')
        {
            ShowModalSplitDialog();
            return true;
        }

        var gameKey = MapKey(keyEvent);
        if (gameKey is null)
            return base.ProcessKey(keyEvent);

        var result = _controller.HandleKey(gameKey.Value, _rng);
        if (result.Handled)
        {
            _gridPanel.SetInputStatus(result.StatusMessage ?? $"Key: {keyEvent.Key}");
            UpdateUI();
        }
        return result.Handled || base.ProcessKey(keyEvent);
    }

    private void OnGridCellClicked(Position pos)
    {
        var result = _controller.HandleGridClick(pos, ClickType.Primary);
        _gridPanel.SetInputStatus(result.StatusMessage ?? $"LClick: ({pos.Row},{pos.Col})");
        UpdateUI();
    }

    private void OnGridCellRightClicked(Position pos)
    {
        var result = _controller.HandleGridClick(pos, ClickType.Secondary);
        _gridPanel.SetInputStatus(result.StatusMessage ?? $"RClick: ({pos.Row},{pos.Col})");
        UpdateUI();
    }

    private void OnBackClicked()
    {
        var result = _controller.HandleBackClick();
        _gridPanel.SetInputStatus(result.StatusMessage ?? "Back (click)");
        UpdateUI();
    }

    private void OnPanelCellClicked(LocationId panelId, Position pos, ClickType type)
    {
        var result = _controller.HandleGridClick(panelId, pos, type);
        _gridPanel.SetInputStatus(result.StatusMessage ?? $"Click {panelId}: ({pos.Row},{pos.Col})");
        UpdateUI();
    }

    private void OnMouseStateChanged(MouseFlags flags)
    {
        var l = flags.HasFlag(MouseFlags.Button1Pressed) ? "■" : "·";
        var r = flags.HasFlag(MouseFlags.Button3Pressed) ? "■" : "·";
        _gridPanel.SetInputStatus($"[L{l}] [R{r}] {flags}");
    }

    private void UpdateUI()
    {
        _gridPanel.UpdateState(_controller.Session.Current, _controller.Focus);
        _rightPanel.UpdateLog(_controller.Session.ActionLog);
        UpdatePanelLayout();
    }

    /// <summary>
    /// Updates all panel positions and visibility based on current LocationMap and focus.
    /// Layout: C above B, W below B, T at bottom, H left of all (handled by GridPanel).
    /// </summary>
    private void UpdatePanelLayout()
    {
        var state = _controller.Session.Current;
        var focus = _controller.Focus;

        // Update B panel focus border
        _gridPanel.Title = focus == LocationId.B ? "► Inventory" : "  Inventory";

        // Container panel (C) — above B
        var cLoc = state.Locations.TryGet(LocationId.C);
        if (cLoc is not null)
        {
            var cBag = state.Store.GetById(cLoc.BagId);
            _containerPanel.Title = cBag?.EnvironmentType ?? "Container";
            _containerPanel.UpdatePanel(cBag, cLoc.Cursor, focus == LocationId.C);
        }
        else
        {
            _containerPanel.UpdatePanel(null, null, false);
        }

        // World panel (W) — below B
        var wLoc = state.Locations.TryGet(LocationId.W);
        if (wLoc is not null)
        {
            var wBag = state.Store.GetById(wLoc.BagId);
            _worldPanel.Title = wBag?.EnvironmentType ?? "World";
            _worldPanel.UpdatePanel(wBag, wLoc.Cursor, focus == LocationId.W);
        }
        else
        {
            _worldPanel.UpdatePanel(null, null, false);
        }

        // Toolbar panel (T) — at bottom
        var tLoc = state.Locations.TryGet(LocationId.T);
        if (tLoc is not null)
        {
            var tBag = state.Store.GetById(tLoc.BagId);
            _toolbarPanel.Title = "Toolbar";
            _toolbarPanel.UpdatePanel(tBag, tLoc.Cursor, focus == LocationId.T);
        }

        // Position panels vertically in left column
        var y = 0;

        // C panel at top (if visible)
        if (_containerPanel.Visible && cLoc is not null)
        {
            var cBag = state.Store.GetById(cLoc.BagId);
            var cHeight = cBag is not null ? CellRenderer.CellHeight * cBag.Grid.Rows + 2 : 0;
            _containerPanel.X = PanelXOffset;
            _containerPanel.Y = y;
            y += cHeight;
        }

        // B panel (GridPanel)
        _gridPanel.X = 0;
        _gridPanel.Y = y;
        y += CellRenderer.CellHeight * state.RootBag.Grid.Rows + 6;

        // W panel below B (if visible)
        if (_worldPanel.Visible && wLoc is not null)
        {
            var wBag = state.Store.GetById(wLoc.BagId);
            var wHeight = wBag is not null ? CellRenderer.CellHeight * wBag.Grid.Rows + 2 : 0;
            _worldPanel.X = PanelXOffset;
            _worldPanel.Y = y;
            y += wHeight;
        }

        // T panel at bottom
        if (_toolbarPanel.Visible && tLoc is not null)
        {
            var tBag = state.Store.GetById(tLoc.BagId);
            var tHeight = tBag is not null ? CellRenderer.CellHeight * tBag.Grid.Rows + 2 : 5;
            _toolbarPanel.X = PanelXOffset;
            _toolbarPanel.Y = Pos.AnchorEnd(tHeight + 3);
        }
    }

    private void ShowModalSplitDialog()
    {
        var cell = _controller.Session.Current.CurrentCell;
        if (cell.IsEmpty || cell.Stack!.Count <= 1)
            return;

        var stack = cell.Stack;
        var max = stack.Count - 1;

        var dialog = new Dialog("Split", 40, 8);
        var label = new Label($"Grab how many {stack.ItemType.Name}? (1-{max})")
        {
            X = 1, Y = 0
        };
        var input = new TextField("")
        {
            X = 1, Y = 1, Width = 10
        };
        var okButton = new Button("OK");
        okButton.Clicked += () =>
        {
            if (int.TryParse(input.Text.ToString(), out var grabCount))
            {
                var leftCount = stack.Count - grabCount;
                var newSession = _controller.Session.ExecuteModalSplit(leftCount);
                _controller.SetSession(newSession);
                UpdateUI();
            }
            Application.RequestStop();
        };
        var cancelButton = new Button("Cancel");
        cancelButton.Clicked += () => Application.RequestStop();

        dialog.AddButton(okButton);
        dialog.AddButton(cancelButton);
        dialog.Add(label, input);
        input.SetFocus();

        Application.Run(dialog);
    }
}
