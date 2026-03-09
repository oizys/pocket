using Terminal.Gui;
using Pockets.Core.Models;

namespace Pockets.App.Views;

/// <summary>
/// Main game window. Holds GameState, handles arrow key input, and manages child panels.
/// </summary>
public class GameView : Window
{
    private GameState _state;
    private readonly GridPanel _gridPanel;
    private readonly Random _rng = new();

    public GameView(GameState initialState) : base("Pockets")
    {
        X = 0;
        Y = 0;
        Width = Dim.Fill();
        Height = Dim.Fill();

        _state = initialState;

        _gridPanel = new GridPanel(_state);
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
            _state = _state.MoveCursor(direction.Value);
            _gridPanel.UpdateState(_state);
            return true;
        }

        var result = keyEvent.Key switch
        {
            (Key)'1' => _state.ToolGrab(),
            (Key)'2' => _state.ToolDrop(),
            (Key)'3' => _state.ToolQuickSplit(),
            (Key)'4' => _state.ToolSort(),
            (Key)'5' => _state.ToolAcquireRandom(_rng),
            _ => null
        };

        if (result is not null)
        {
            _state = result.State;
            _gridPanel.UpdateState(_state);
            return true;
        }

        return base.ProcessKey(keyEvent);
    }
}
