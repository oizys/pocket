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
    private Position? _dragStart;

    public GameView(GameState initialState) : base("Pockets")
    {
        X = 0;
        Y = 0;
        Width = Dim.Fill();
        Height = Dim.Fill();

        _session = GameSession.New(initialState);

        _gridPanel = new GridPanel(_session.Current);
        _rightPanel = new RightPanel();

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
            UpdateUI();
            return true;
        }

        return base.ProcessKey(keyEvent);
    }

    private void UpdateUI()
    {
        _gridPanel.UpdateState(_session.Current);
        _rightPanel.UpdateLog(_session.ActionLog);
    }

    /// <summary>
    /// Tries to convert a mouse event's view-relative coordinates to a grid Position.
    /// Returns null if the mouse is outside the grid area.
    /// </summary>
    private Position? MouseToGridPosition(MouseEvent mouseEvent)
    {
        // GridView is inside GridPanel: GridPanel starts at (0,0) within GameView,
        // GridView starts at Y=1 within GridPanel (breadcrumb label at Y=0).
        // Account for FrameView borders (1 char each side).
        var gridView = _gridPanel.GetGridView();
        var screenX = mouseEvent.X;
        var screenY = mouseEvent.Y;

        // Convert from GameView coords to GridView coords
        // GameView border (1) + GridPanel border (1) + GridView offset within panel
        var gridViewX = screenX - 2; // 1 (Window border) + 1 (FrameView border)
        var gridViewY = screenY - 3; // 1 (Window border) + 1 (FrameView border) + 1 (breadcrumb label)

        if (gridViewX < 0 || gridViewY < 0)
            return null;

        var col = gridViewX / CellRenderer.CellWidth;
        var row = gridViewY / CellRenderer.CellHeight;

        var grid = _session.Current.ActiveBag.Grid;
        if (row < 0 || row >= grid.Rows || col < 0 || col >= grid.Columns)
            return null;

        return new Position(row, col);
    }

    public override bool OnMouseEvent(MouseEvent mouseEvent)
    {
        // Left button pressed — record drag start
        if (mouseEvent.Flags.HasFlag(MouseFlags.Button1Pressed))
        {
            _dragStart = MouseToGridPosition(mouseEvent);
            return true;
        }

        // Left button released — move cursor + interact, or complete drag
        if (mouseEvent.Flags.HasFlag(MouseFlags.Button1Released))
        {
            var releasePos = MouseToGridPosition(mouseEvent);
            if (releasePos is null)
            {
                _dragStart = null;
                return true;
            }

            if (_dragStart is not null && _dragStart != releasePos)
            {
                // Drag: grab from start, drop at end
                ExecuteDrag(_dragStart.Value, releasePos.Value);
            }
            else
            {
                // Click: move cursor to position and do primary action
                _session = _session.MoveCursor(_session.Current, releasePos.Value);
                _session = _session.ExecutePrimary();
            }

            _dragStart = null;
            UpdateUI();
            return true;
        }

        // Right click — secondary action (half/one)
        if (mouseEvent.Flags.HasFlag(MouseFlags.Button3Clicked))
        {
            var clickPos = MouseToGridPosition(mouseEvent);
            if (clickPos is not null)
            {
                _session = _session.MoveCursor(_session.Current, clickPos.Value);
                _session = _session.ExecuteSecondary();
            }
            UpdateUI();
            return true;
        }

        return base.OnMouseEvent(mouseEvent);
    }

    /// <summary>
    /// Executes a drag from one grid cell to another: primary on source (grab), primary on dest (drop).
    /// </summary>
    private void ExecuteDrag(Position from, Position to)
    {
        // Move cursor to source and do primary (will grab if empty hand + occupied cell)
        _session = _session.MoveCursor(_session.Current, from);
        _session = _session.ExecutePrimary();

        if (!_session.Current.HasItemsInHand)
            return; // nothing was grabbed

        // Move cursor to destination and do primary (will drop/merge/swap)
        _session = _session.MoveCursor(_session.Current, to);
        _session = _session.ExecutePrimary();
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
