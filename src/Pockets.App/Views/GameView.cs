using Terminal.Gui;
using Pockets.Core.Models;

namespace Pockets.App.Views;

/// <summary>
/// Main game window. Holds GameSession (state + undo + log), handles input, manages child panels.
/// </summary>
public class GameView : Window
{
    private GameSession _session;
    private readonly GridPanel _gridPanel;
    private readonly Random _rng = new();

    public GameView(GameState initialState) : base("Pockets")
    {
        X = 0;
        Y = 0;
        Width = Dim.Fill();
        Height = Dim.Fill();

        _session = GameSession.New(initialState);

        _gridPanel = new GridPanel(_session.Current);
        var rightPanel = new RightPanel();

        Add(_gridPanel, rightPanel);
    }

    public override bool ProcessKey(KeyEvent keyEvent)
    {
        Direction? direction = keyEvent.Key switch
        {
            Key.CursorUp => Direction.Up,
            Key.CursorDown => Direction.Down,
            Key.CursorLeft => Direction.Left,
            Key.CursorRight => Direction.Right,
            _ => null
        };

        if (direction is not null)
        {
            _session = _session.MoveCursor(direction.Value);
            _gridPanel.UpdateState(_session.Current);
            return true;
        }

        // Ctrl-Z = Undo
        if (keyEvent.Key == (Key.Z | Key.CtrlMask))
        {
            var undone = _session.Undo();
            if (undone is not null)
            {
                _session = undone;
                _gridPanel.UpdateState(_session.Current);
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
            (Key)'1' => _session.ExecuteGrab(),
            (Key)'2' => _session.ExecuteDrop(),
            (Key)'3' => _session.ExecuteQuickSplit(),
            (Key)'4' => _session.ExecuteSort(),
            (Key)'5' => _session.ExecuteAcquireRandom(_rng),
            (Key)'e' or (Key)'E' => _session.ExecuteEnterBag(),
            (Key)'q' or (Key)'Q' => _session.ExecuteLeaveBag(),
            _ => null
        };

        if (newSession is not null)
        {
            _session = newSession;
            _gridPanel.UpdateState(_session.Current);
            return true;
        }

        return base.ProcessKey(keyEvent);
    }

    /// <summary>
    /// Opens a dialog for modal split: user enters the amount to keep in the cell.
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
                    _gridPanel.UpdateState(_session.Current);
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
