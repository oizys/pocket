using System.Collections.Immutable;
using System.Text;
using Terminal.Gui;
using Pockets.Core;
using Pockets.Core.Fine;
using Pockets.Core.Models;
using Pockets.Core.Rendering;
using Pockets.Core.Sim;
using Pockets.App.Views;
using Pockets.Realization;
using TgKey = Terminal.Gui.Key; // disambiguate from this class's Key(GameKey) method

namespace Pockets.JourneyRunner;

/// <summary>
/// The TUI driver: runs the REAL shipping <see cref="GameView"/> headless under Terminal.Gui's
/// <see cref="FakeDriver"/> (80×25 in-memory buffer) and drives it through the actual
/// <c>ProcessKey</c> input path — the same code a real terminal exercises, including the TUI
/// keymap and UI refresh. Observations read the live controller session (for the view-model +
/// invariant pack) and the character buffer (for assert-render).
///
/// Poses (Slice 2.4, P2-D4): the driver builds its own <see cref="PoseStream"/> from the events the view received and
/// names it through the run's <see cref="EventText"/>. The TUI renders nothing new — it is n = 1, and its frame is the
/// u = 1 sample — so the stream exists only to be diffed against mock-godot's and the golden.
///
/// Terminal.Gui's Application is global/static, so at most one TuiDriver may exist at a time.
/// </summary>
public sealed class TuiDriver : IJourneyDriver, ISessionDriver
{
    private readonly GameView _view;
    private readonly int _cols;
    private readonly int _rows;

    /// <summary>One canonical event stream for the run (Slice 1.6), so a bag keeps its <c>bag#N</c> name.</summary>
    private readonly EventText _text = new();

    /// <summary>The event lines the view's controller emitted since the last <see cref="Observe"/>.</summary>
    private readonly List<string> _events = new();

    /// <summary>This run's pose stream (Slice 2.4), named by <see cref="_text"/>; created on the initial state.</summary>
    private readonly PoseStream _stream;

    /// <summary>The pose commits since the last <see cref="Observe"/>.</summary>
    private readonly List<ObservedCommit> _poses = new();

    public TuiDriver(DemoProfile profile)
    {
        try { Application.Shutdown(); } catch { /* ignore if not initialized */ }
        Application.Init(driver: new FakeDriver());

        _view = new GameView(profile.State, profile.Recipes, profile.FacilityRecipeMap,
            enableTickTimer: false, tickMode: profile.TickMode, dialogue: profile.Dialogue,
            worldSeed: profile.WorldSeed)
        {
            X = 0, Y = 0, Width = Dim.Fill(), Height = Dim.Fill()
        };
        Application.Top.Add(_view);

        var contents = Application.Driver.Contents;
        _rows = contents.GetLength(0);
        _cols = contents.GetLength(1);
        _stream = new PoseStream(_view.Controller.Session.Current, _text.Names, null, FineView.Of(_view.Controller.Session.Fine));
        Render();
    }

    public string Name => "tui";

    /// <inheritdoc />
    public GameSession Session => _view.Controller.Session;

    public DriverObservation Observe()
    {
        var session = _view.Controller.Session;
        var vm = ViewModelSerializer.Serialize(session);
        var events = _events.ToList();
        var poses = _poses.ToList();
        _events.Clear();
        _poses.Clear();
        return new DriverObservation(vm, session.Current, DumpBuffer(), events, poses);
    }

    public void Key(GameKey key)
    {
        _view.ProcessKey(new KeyEvent(MapToKey(key), new KeyModifiers()));
        Record(_view.LastEvents, atMs: 0);
        Render();
    }

    /// <summary>Back = the TUI's leave-bag handler (identical Core effect to the back button).</summary>
    public void Back() => Key(GameKey.LeaveBag);

    public void Tick()
    {
        // Rogue demo profile ticks per action; a wait step advances a facility tick explicitly,
        // matching the realtime timer's Controller.Tick(). (Render refreshes on the next key.)
        Record(_view.Controller.Tick().Events, GameController.TickLengthMs);
        Render();
    }

    public void Click(Position pos, ClickType type)
    {
        Record(_view.Controller.HandleGridClick(pos, type).Events, atMs: 0);
        Render();
    }

    /// <summary>
    /// Scripted clock advance (Slice 5). The harness GameView is built with enableTickTimer:false, so
    /// its controller keeps the default VirtualGameClock — this is the only thing that moves game time
    /// here. No wall clock is ever read.
    /// </summary>
    public void AdvanceTime(TimeSpan delta)
    {
        Record(_view.Controller.AdvanceClock(delta).Events, (int)delta.TotalMilliseconds);
        _view.RefreshUi();
        Render();
    }

    /// <summary>
    /// Appends <paramref name="events"/> to the pending lines, in this run's naming, then commits them to the pose stream
    /// as one commit (events named before poses, as the debug handler does). <paramref name="atMs"/> is the clock delta
    /// (P2-D13): the advance's ms, one tick's length, else 0 — the same values <see cref="PoseDebugCommandHandler.AtMs"/> gives.
    /// </summary>
    private void Record(ImmutableArray<SimEvent> events, int atMs)
    {
        _events.AddRange(events.Select(_text.Line));
        var lines = _stream.Commit(events, _view.Controller.Session.Current, atMs, FineView.Of(_view.Controller.Session.Fine));
        _poses.Add(new ObservedCommit(atMs, _view.Controller.Session.IntentLog.Count, lines));
    }

    // --- Terminal.Gui plumbing ---

    private void Render()
    {
        Application.Top.LayoutSubviews();
        Application.Top.Redraw(Application.Top.Bounds);
    }

    private string DumpBuffer()
    {
        var contents = Application.Driver.Contents;
        var sb = new StringBuilder();
        for (int y = 0; y < _rows; y++)
        {
            for (int x = 0; x < _cols; x++)
                sb.Append((char)contents[y, x, 0]);
            sb.Append('\n');
        }
        return sb.ToString();
    }

    /// <summary>
    /// Reverse of GameView.MapKey: turns a logical GameKey into the Terminal.Gui key the real
    /// TUI keymap recognizes, so the driver exercises that keymap end-to-end.
    /// </summary>
    private static TgKey MapToKey(GameKey key) => key switch
    {
        GameKey.Up => TgKey.CursorUp,
        GameKey.Down => TgKey.CursorDown,
        GameKey.Left => TgKey.CursorLeft,
        GameKey.Right => TgKey.CursorRight,
        GameKey.Primary => (TgKey)'1',
        GameKey.Secondary => (TgKey)'2',
        GameKey.QuickSplit => (TgKey)'3',
        GameKey.BeginSplit => (TgKey)'#',
        GameKey.Sort => (TgKey)'4',
        GameKey.AcquireRandom => (TgKey)'5',
        GameKey.RecipeMenu => (TgKey)'r',
        GameKey.Peek => (TgKey)'c',
        GameKey.LeaveBag => (TgKey)'q',
        GameKey.Undo => TgKey.Z | TgKey.CtrlMask,
        GameKey.Confirm => TgKey.Enter,
        GameKey.Cancel => TgKey.Esc,
        GameKey.FocusNext => TgKey.Tab,
        GameKey.FocusPrev => TgKey.BackTab,
        _ => throw new ArgumentOutOfRangeException(nameof(key), key, "No TUI key mapping")
    };

    public void Dispose()
    {
        try { Application.Shutdown(); } catch { /* ignore */ }
    }
}
