using System.Text.Json.Nodes;
using Pockets.Core.Models;
using Pockets.Core.Rendering;

namespace Pockets.JourneyRunner;

/// <summary>
/// The Godot driver against a MOCK transport: it holds a GameController in process and routes
/// every command through <see cref="DebugCommandHandler"/> — the exact server-side dispatch the
/// real Godot WebSocket server runs per packet. Only the socket is mocked; the command handling
/// and view-model serialization are the real Godot path. This is the driver used by `make parity`
/// in environments (like WSL) where the Godot editor/runtime can't run headless. The real
/// <see cref="GodotWebSocketDriver"/> talks to a running Godot binary on a machine that has one.
/// </summary>
public sealed class MockGodotDriver : IJourneyDriver
{
    private readonly GameController _controller;

    public MockGodotDriver(GameController controller) => _controller = controller;

    public string Name => "mock-godot";

    public DriverObservation Observe()
    {
        // Route a "state" query through the same handler the socket would — proving the mock
        // transport yields the identical payload — then expose FullState for the invariant pack.
        var resp = JsonNode.Parse(DebugCommandHandler.Handle(_controller, """{"action":"state"}"""))!;
        var vm = (JsonObject)resp["state"]!;
        return new DriverObservation(vm, _controller.Session.Current, RenderProbe: null);
    }

    public void Key(GameKey key) =>
        Send(new JsonObject { ["action"] = "key", ["key"] = key.ToString() });

    public void Tick() =>
        Send(new JsonObject { ["action"] = "tick" });

    public void AdvanceTime(TimeSpan delta) =>
        Send(new JsonObject { ["action"] = "advanceTime", ["ms"] = (long)delta.TotalMilliseconds });

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
        var resp = JsonNode.Parse(DebugCommandHandler.Handle(_controller, command.ToJsonString()))!;
        if (resp["error"] is { } err)
            throw new InvalidOperationException($"mock-godot transport error: {err.GetValue<string>()}");
    }

    public void Dispose() { }
}
