using Terminal.Gui;
using Pockets.Core.Models;
using Pockets.App.Rendering;

namespace Pockets.App.Views;

/// <summary>
/// Main game window. Holds GameSession (state + undo + log), handles input, manages child panels.
/// </summary>
public class GameView : Window
{
    private GameSession _session;
    private readonly GridPanel _gridPanel;
    private readonly RightPanel _rightPanel;
    private readonly Random _rng = new();

    public GameView(GameState initialState) : base("Pockets")
    {
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

        _session = GameSession.New(initialState);

        _gridPanel = new GridPanel(_session.Current);
        _rightPanel = new RightPanel();

        // Wire mouse events from GridView back to us
        _gridPanel.GetGridView().GridCellClicked += OnGridCellClicked;
        _gridPanel.GetGridView().GridCellRightClicked += OnGridCellRightClicked;
        _gridPanel.GetGridView().MouseStateChanged += OnMouseStateChanged;
        _gridPanel.GetBackButton().BackClicked += OnBackClicked;

        Add(_gridPanel, _rightPanel);
    }

    public override bool ProcessKey(KeyEvent keyEvent)
    {
        Direction? direction = keyEvent.Key switch
        {
            Key.CursorUp or (Key)'w' or (Key)'W' => Direction.Up,
            Key.CursorDown or (Key)'s' or (Key)'S' => Direction.Down,
            Key.CursorLeft or (Key)'a' or (Key)'A' => Direction.Left,
            Key.CursorRight or (Key)'d' or (Key)'D' => Direction.Right,
            _ => null
        };

        if (direction is not null)
        {
            _session = _session.MoveCursor(direction.Value);
            _gridPanel.SetInputStatus($"Key: {keyEvent.Key}");
            UpdateUI();
            return true;
        }

        // Ctrl-Z = Undo
        if (keyEvent.Key == (Key.Z | Key.CtrlMask))
        {
            var undone = _session.Undo();
            if (undone is not null)
            {
                _session = undone;
                _gridPanel.SetInputStatus("Undo");
                UpdateUI();
            }
            return true;
        }

        // Shift-3 (#) = Modal Split dialog
        if (keyEvent.Key == (Key)'#')
        {
            ShowModalSplitDialog();
            return true;
        }

        GameSession? newSession = keyEvent.Key switch
        {
            (Key)'1' or (Key)'e' or (Key)'E' => _session.ExecutePrimary(),
            (Key)'2' => _session.ExecuteSecondary(),
            (Key)'4' => _session.ExecuteSort(),
            (Key)'5' => _session.ExecuteAcquireRandom(_rng),
            (Key)'q' or (Key)'Q' => _session.ExecuteLeaveBag(),
            _ => null
        };

        if (newSession is not null)
        {
            _session = newSession;
            _gridPanel.SetInputStatus($"Key: {keyEvent.Key}");
            UpdateUI();
            return true;
        }

        return base.ProcessKey(keyEvent);
    }

    private void OnGridCellClicked(Position pos)
    {
        _session = _session.MoveCursor(_session.Current, pos);
        _session = _session.ExecutePrimary();
        _gridPanel.SetInputStatus($"LClick: ({pos.Row},{pos.Col})");
        UpdateUI();
    }

    private void OnGridCellRightClicked(Position pos)
    {
        _session = _session.MoveCursor(_session.Current, pos);
        _session = _session.ExecuteSecondary();
        _gridPanel.SetInputStatus($"RClick: ({pos.Row},{pos.Col})");
        UpdateUI();
    }

    private void OnBackClicked()
    {
        var newSession = _session.ExecuteLeaveBag();
        _session = newSession;
        _gridPanel.SetInputStatus("Back (click)");
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
        _gridPanel.UpdateState(_session.Current);
        _rightPanel.UpdateLog(_session.ActionLog);
    }

    /// <summary>
    /// Opens a dialog for modal split: user enters the amount to grab.
    /// </summary>
    private void ShowModalSplitDialog()
    {
        var cell = _session.Current.CurrentCell;
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
                var newSession = _session.ExecuteModalSplit(leftCount);
                if (newSession is not null)
                {
                    _session = newSession;
                    UpdateUI();
                }
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
