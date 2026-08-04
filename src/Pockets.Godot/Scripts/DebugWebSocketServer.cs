using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Godot;
using Pockets.Core.Models;

namespace Pockets.Godot.Scripts;

/// <summary>
/// WebSocket server that exposes the GameController API for external agents.
/// Runs inside the Godot scene tree, polling connections each frame.
/// Protocol: send JSON command, receive JSON result.
///
/// Commands:
///   {"action": "key", "key": "Primary"}           → HandleKey
///   {"action": "click", "row": 0, "col": 2, "button": "Primary"}  → HandleGridClick
///   {"action": "back"}                              → HandleBackClick
///   {"action": "tick"}                              → Tick
///   {"action": "advanceTime", "ms": 5000}           → AdvanceClock (scripted clock advance)
///   {"action": "state"}                             → Return current state (no mutation)
///   {"action": "screenshot", "path": "/tmp/ss.png"} → Save viewport screenshot
///
/// Response: {"handled": bool, "status": "...", "state": {...}}
/// </summary>
public partial class DebugWebSocketServer : Node
{
    [Export] public int Port { get; set; } = 9080;

    private TcpServer _tcpServer = new();
    private readonly Dictionary<int, WebSocketPeer> _peers = new();
    private int _lastPeerId;

    /// <summary>
    /// The game controller to drive. Set by GameSceneController after initialization.
    /// </summary>
    public Core.Models.GameController? Controller { get; set; }

    public override void _Ready()
    {
        var err = _tcpServer.Listen((ushort)Port);
        if (err == Error.Ok)
            GD.Print($"[WS] Debug server listening on port {Port}");
        else
        {
            GD.PushError($"[WS] Failed to start server on port {Port}: {err}");
            SetProcess(false);
        }
    }

    public override void _Process(double delta)
    {
        // Accept new TCP connections
        while (_tcpServer.IsConnectionAvailable())
        {
            _lastPeerId++;
            var ws = new WebSocketPeer();
            ws.AcceptStream(_tcpServer.TakeConnection());
            _peers[_lastPeerId] = ws;
            GD.Print($"[WS] + Peer {_lastPeerId} connected");
        }

        // Poll each peer
        var toRemove = new List<int>();
        foreach (var (peerId, peer) in _peers)
        {
            peer.Poll();
            var state = peer.GetReadyState();

            if (state == WebSocketPeer.State.Open)
            {
                while (peer.GetAvailablePacketCount() > 0)
                {
                    var packet = peer.GetPacket();
                    if (peer.WasStringPacket())
                    {
                        var text = packet.GetStringFromUtf8();
                        var response = HandleCommand(text);
                        peer.SendText(response);
                    }
                }
            }
            else if (state == WebSocketPeer.State.Closed)
            {
                toRemove.Add(peerId);
                GD.Print($"[WS] - Peer {peerId} disconnected: {peer.GetCloseCode()} {peer.GetCloseReason()}");
            }
        }
        foreach (var id in toRemove)
            _peers.Remove(id);
    }

    private string HandleCommand(string json)
    {
        try
        {
            if (Controller is null)
                return ErrorResponse("GameController not initialized");

            // Screenshot needs the Godot viewport, so it stays transport-local. Every other
            // action routes through the shared Core dispatch (Pockets.Core.Rendering.
            // DebugCommandHandler) — the exact same server-side path the journey runner's
            // mock transport exercises, and one shared view-model serializer for both.
            var cmd = JsonNode.Parse(json);
            if (cmd is not null && cmd["action"]?.GetValue<string>() == "screenshot")
                return HandleScreenshot(cmd);

            var response = Core.Rendering.DebugCommandHandler.Handle(Controller, json, out var mutated);
            if (mutated)
                CallDeferred(nameof(DeferredRefreshUI));
            return response;
        }
        catch (System.Exception ex)
        {
            return ErrorResponse($"Exception: {ex.Message}");
        }
    }

    private string HandleScreenshot(JsonNode cmd)
    {
        var path = cmd["path"]?.GetValue<string>() ?? "/tmp/pockets_screenshot.png";
        var img = GetViewport().GetTexture().GetImage();
        var err = img.SavePng(path);
        if (err != Error.Ok)
            return ErrorResponse($"Screenshot failed: {err}");
        var response = new JsonObject
        {
            ["handled"] = true,
            ["status"] = $"Screenshot saved to {path}",
            ["state"] = Core.Rendering.ViewModelSerializer.Serialize(Controller!.Session)
        };
        return response.ToJsonString();
    }

    /// <summary>
    /// Signals the scene controller to refresh UI on the main thread.
    /// </summary>
    private void DeferredRefreshUI()
    {
        var scene = GetParent<GameSceneController>();
        scene?.RequestRefreshUI();
    }

    private static string ErrorResponse(string message)
    {
        var response = new JsonObject
        {
            ["error"] = message
        };
        return response.ToJsonString();
    }
}
