using System.Net.WebSockets;
using System.Text;
using System.Text.Json.Nodes;
using Pockets.Core.Models;

namespace Pockets.JourneyRunner;

/// <summary>
/// The REAL Godot driver: a WebSocket client to a running Godot build's DebugWebSocketServer
/// (default ws://localhost:9080). Uses the in-framework <see cref="ClientWebSocket"/> (no extra
/// dependency). Requires an actual Godot process, so it is intended for a machine with a Godot
/// runtime (e.g. Aaron's Windows box) — see design/parity-drift-report.md for why WSL uses the
/// mock transport instead.
///
/// FullState is null: the wire protocol exposes only the active-bag view-model, not the whole
/// bag store, so the global conservation census is unavailable here (the runner falls back to a
/// view-model-scoped invariant check and flags conservation as transport-limited).
/// </summary>
public sealed class GodotWebSocketDriver : IJourneyDriver
{
    private readonly ClientWebSocket _ws = new();
    private readonly Uri _uri;

    public GodotWebSocketDriver(string url = "ws://localhost:9080")
    {
        _uri = new Uri(url);
        _ws.ConnectAsync(_uri, CancellationToken.None).GetAwaiter().GetResult();
    }

    public string Name => "godot";

    public DriverObservation Observe()
    {
        var resp = Send(new JsonObject { ["action"] = "state" });
        return new DriverObservation((JsonObject)resp["state"]!, FullState: null, RenderProbe: null);
    }

    public void Key(GameKey key) =>
        Send(new JsonObject { ["action"] = "key", ["key"] = key.ToString() });

    public void Tick() => Send(new JsonObject { ["action"] = "tick" });

    public void Back() => Send(new JsonObject { ["action"] = "back" });

    public void Click(Position pos, ClickType type) =>
        Send(new JsonObject
        {
            ["action"] = "click",
            ["row"] = pos.Row,
            ["col"] = pos.Col,
            ["button"] = type.ToString()
        });

    private JsonObject Send(JsonObject command)
    {
        var bytes = Encoding.UTF8.GetBytes(command.ToJsonString());
        _ws.SendAsync(bytes, WebSocketMessageType.Text, endOfMessage: true, CancellationToken.None)
            .GetAwaiter().GetResult();

        var buffer = new byte[64 * 1024];
        var sb = new StringBuilder();
        WebSocketReceiveResult result;
        do
        {
            result = _ws.ReceiveAsync(buffer, CancellationToken.None).GetAwaiter().GetResult();
            sb.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
        } while (!result.EndOfMessage);

        var resp = (JsonObject)JsonNode.Parse(sb.ToString())!;
        if (resp["error"] is { } err)
            throw new InvalidOperationException($"godot transport error: {err.GetValue<string>()}");
        return resp;
    }

    public void Dispose()
    {
        try
        {
            _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None)
                .GetAwaiter().GetResult();
        }
        catch { /* best effort */ }
        _ws.Dispose();
    }
}
