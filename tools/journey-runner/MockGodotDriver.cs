using System.Text.Json.Nodes;
using Pockets.Core.Models;
using Pockets.Realization;

namespace Pockets.JourneyRunner;

/// <summary>
/// The Godot driver against a MOCK transport: it holds a GameController in process and routes
/// every command through <see cref="PoseDebugCommandHandler"/> (the shared Core dispatch plus poses, Slice 2.4) — the
/// exact server-side dispatch the real Godot WebSocket server runs per packet. Only the socket is mocked; the command handling
/// and view-model serialization are the real Godot path. This is the driver used by `make parity`
/// in environments (like WSL) where the Godot editor/runtime can't run headless. The real
/// <see cref="GodotWebSocketDriver"/> talks to a running Godot binary on a machine that has one.
/// </summary>
public sealed class MockGodotDriver : IJourneyDriver, ISessionDriver
{
    private readonly GameController _controller;

    /// <summary>The <c>events</c> lines of every response since the last <see cref="Observe"/> (Slice 1.6).</summary>
    private readonly List<string> _events = new();

    /// <summary>The <c>poses</c> of every mutating response since the last <see cref="Observe"/>, one per commit (Slice 2.4).</summary>
    private readonly List<ObservedCommit> _poses = new();

    public MockGodotDriver(GameController controller) => _controller = controller;

    public string Name => "mock-godot";

    /// <inheritdoc />
    public GameSession Session => _controller.Session;

    public DriverObservation Observe()
    {
        // Route a "state" query through the same handler the socket would — proving the mock
        // transport yields the identical payload — then expose FullState for the invariant pack.
        var resp = JsonNode.Parse(PoseDebugCommandHandler.Handle(_controller, """{"action":"state"}"""))!;
        var vm = (JsonObject)resp["state"]!;
        var events = _events.ToList();
        var poses = _poses.ToList();
        _events.Clear();
        _poses.Clear();
        return new DriverObservation(vm, _controller.Session.Current, RenderProbe: null, events, poses);
    }

    public void Key(GameKey key) =>
        Send(new JsonObject { ["action"] = "key", ["key"] = key.ToString() });

    public void Tick() =>
        Send(new JsonObject { ["action"] = "tick" });

    public void AdvanceTime(TimeSpan delta) =>
        Send(new JsonObject { ["action"] = "advanceTime", ["ms"] = (long)delta.TotalMilliseconds });

    /// <summary>Slice 2.7: the debug <c>steer</c> command (a null direction releases).</summary>
    public void Steer(LocationId at, Direction8? dir) =>
        Send(new JsonObject { ["action"] = "steer", ["loc"] = at.ToString(), ["dir"] = dir?.ToString() ?? "none" });

    /// <summary>Slice 2.7: the debug <c>setMovement</c> command.</summary>
    public void SetMovement(LocationId at, MovementModel model) =>
        Send(new JsonObject { ["action"] = "setMovement", ["loc"] = at.ToString(), ["model"] = model.ToString() });

    public void Back() =>
        Send(new JsonObject { ["action"] = "back" });

    public void Click(Position pos, ClickType type) =>
        Send(new JsonObject
        {
            ["action"] = "click",
            ["row"] = pos.Row,
            ["col"] = pos.Col,
            ["button"] = type.ToString()
        });

    private void Send(JsonObject command)
    {
        var resp = JsonNode.Parse(PoseDebugCommandHandler.Handle(_controller, command.ToJsonString()))!;
        if (resp["error"] is { } err)
            throw new InvalidOperationException($"mock-godot transport error: {err.GetValue<string>()}");
        _events.AddRange(EventLines(resp));
        _poses.Add(new ObservedCommit(PoseDebugCommandHandler.AtMs(command), _controller.Session.IntentLog.Count, PoseLines(resp)!));
    }

    /// <summary>A response's <c>events</c> lines (the shared handler always sends the array).</summary>
    internal static IEnumerable<string> EventLines(JsonNode resp) =>
        resp["events"]?.AsArray().Select(e => e!.GetValue<string>()) ?? Enumerable.Empty<string>();

    /// <summary>A response's <c>poses</c> lines (Slice 2.4), or null when the response has no such field (a pre-2.4 Godot).</summary>
    internal static IReadOnlyList<string>? PoseLines(JsonNode resp) =>
        resp["poses"]?.AsArray().Select(p => p!.GetValue<string>()).ToList();

    public void Dispose() { }
}
