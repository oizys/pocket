using System.Collections.Immutable;
using Terminal.Gui;
using Pockets.Core.Models;
using Pockets.App.Rendering;

namespace Pockets.App.Views;

/// <summary>
/// Main game window. Delegates input handling to GameController, manages child panels and UI.
/// Modal split dialog stays here (it's UI-specific).
/// </summary>
public class GameView : Window
{
    private readonly GameController _controller;
    private readonly GridPanel _gridPanel;
    private readonly RightPanel _rightPanel;
    private readonly Random _rng = new();
    private object? _tickTimer;
    private readonly bool _enableTickTimer;

    /// <summary>
    /// Interval between realtime ticks (1 second per tick).
    /// </summary>
    private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(1);

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

        _gridPanel = new GridPanel(_controller.Session.Current);
        if (!recipes.IsDefaultOrEmpty)
            _gridPanel.SetRecipes(recipes);
        _gridPanel.SetFacilityRecipeMap(facilityRecipeMap);
        _rightPanel = new RightPanel();

        // Wire mouse events from GridView back to us
        _gridPanel.GetGridView().GridCellClicked += OnGridCellClicked;
        _gridPanel.GetGridView().GridCellRightClicked += OnGridCellRightClicked;
        _gridPanel.GetGridView().MouseStateChanged += OnMouseStateChanged;
        _gridPanel.GetBackButton().BackClicked += OnBackClicked;

        Add(_gridPanel, _rightPanel);

        // Start realtime tick timer after the main loop is ready
        if (_enableTickTimer)
        {
            Application.MainLoop.AddIdle(() =>
            {
                StartTickTimer();
                return false; // Run once
            });
        }
    }

    /// <summary>
    /// Starts the realtime tick timer. Called once after the main loop is initialized.
    /// </summary>
    private void StartTickTimer()
    {
        if (_controller.Session.TickMode != TickMode.Realtime || _tickTimer is not null)
            return;

        _tickTimer = Application.MainLoop.AddTimeout(TickInterval, _ =>
        {
            var result = _controller.Tick();
            if (result.Handled)
                UpdateUI();
            return true; // Keep repeating
        });
    }

    /// <summary>
    /// Maps Terminal.Gui KeyEvent to abstract GameKey, or null if not a game key.
    /// </summary>
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
            _ => null
        };
    }

    public override bool ProcessKey(KeyEvent keyEvent)
    {
        // Shift-3 (#) = Modal Split dialog (UI-specific, not in GameController)
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

    private void OnMouseStateChanged(MouseFlags flags)
    {
        var l = flags.HasFlag(MouseFlags.Button1Pressed) ? "■" : "·";
        var r = flags.HasFlag(MouseFlags.Button3Pressed) ? "■" : "·";
        _gridPanel.SetInputStatus($"[L{l}] [R{r}] {flags}");
    }

    private void UpdateUI()
    {
        _gridPanel.UpdateState(_controller.Session.Current);
        _rightPanel.UpdateLog(_controller.Session.ActionLog);
    }

    /// <summary>
    /// Opens a dialog for modal split: user enters the amount to grab.
    /// </summary>
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
