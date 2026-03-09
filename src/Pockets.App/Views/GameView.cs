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
}
