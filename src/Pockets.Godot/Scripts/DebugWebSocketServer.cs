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
            var cmd = JsonNode.Parse(json);
            if (cmd is null)
                return ErrorResponse("Could not parse JSON");

            var action = cmd["action"]?.GetValue<string>();
            if (action is null)
                return ErrorResponse("Missing 'action' field");

            if (Controller is null)
                return ErrorResponse("GameController not initialized");

            return action switch
            {
                "key" => HandleKeyAction(cmd),
                "click" => HandleClickAction(cmd),
                "back" => HandleBackAction(),
                "tick" => HandleTickAction(),
                "state" => SuccessResponse(true, "State query"),
                "screenshot" => HandleScreenshot(cmd),
                _ => ErrorResponse($"Unknown action: {action}")
            };
        }
        catch (System.Exception ex)
        {
            return ErrorResponse($"Exception: {ex.Message}");
        }
    }

    private string HandleKeyAction(JsonNode cmd)
    {
        var keyName = cmd["key"]?.GetValue<string>();
        if (keyName is null)
            return ErrorResponse("Missing 'key' field");

        if (!System.Enum.TryParse<GameKey>(keyName, ignoreCase: true, out var gameKey))
            return ErrorResponse($"Unknown key: {keyName}. Valid: {string.Join(", ", System.Enum.GetNames<GameKey>())}");

        var result = Controller!.HandleKey(gameKey);
        CallDeferred(nameof(DeferredRefreshUI));
        return SuccessResponse(result.Handled, result.StatusMessage);
    }

    private string HandleClickAction(JsonNode cmd)
    {
        var row = cmd["row"]?.GetValue<int>() ?? -1;
        var col = cmd["col"]?.GetValue<int>() ?? -1;
        if (row < 0 || col < 0)
            return ErrorResponse("Missing or invalid 'row'/'col' fields");

        var buttonName = cmd["button"]?.GetValue<string>() ?? "Primary";
        if (!System.Enum.TryParse<ClickType>(buttonName, ignoreCase: true, out var clickType))
            clickType = ClickType.Primary;

        var pos = new Position(row, col);
        var result = Controller!.HandleGridClick(pos, clickType);
        CallDeferred(nameof(DeferredRefreshUI));
        return SuccessResponse(result.Handled, result.StatusMessage);
    }

    private string HandleBackAction()
    {
        var result = Controller!.HandleBackClick();
        CallDeferred(nameof(DeferredRefreshUI));
        return SuccessResponse(result.Handled, result.StatusMessage);
    }

    private string HandleTickAction()
    {
        var result = Controller!.Tick();
        CallDeferred(nameof(DeferredRefreshUI));
        return SuccessResponse(result.Handled, result.StatusMessage);
    }

    private string HandleScreenshot(JsonNode cmd)
    {
        var path = cmd["path"]?.GetValue<string>() ?? "/tmp/pockets_screenshot.png";
        var img = GetViewport().GetTexture().GetImage();
        var err = img.SavePng(path);
        if (err != Error.Ok)
            return ErrorResponse($"Screenshot failed: {err}");
        return SuccessResponse(true, $"Screenshot saved to {path}");
    }

    /// <summary>
    /// Signals the scene controller to refresh UI on the main thread.
    /// </summary>
    private void DeferredRefreshUI()
    {
        var scene = GetParent<GameSceneController>();
        scene?.RequestRefreshUI();
    }

    private string SuccessResponse(bool handled, string? status)
    {
        var state = SerializeState(Controller!.Session);
        var response = new JsonObject
        {
            ["handled"] = handled,
            ["status"] = status,
            ["state"] = state
        };
        return response.ToJsonString();
    }

    private static string ErrorResponse(string message)
    {
        var response = new JsonObject
        {
            ["error"] = message
        };
        return response.ToJsonString();
    }

    /// <summary>
    /// Serializes the current game state into a JSON object suitable for agent consumption.
    /// Includes grid contents, cursor, hand, breadcrumbs, and action log.
    /// </summary>
    private static JsonObject SerializeState(GameSession session)
    {
        var state = session.Current;
        var activeBag = state.ActiveBag;
        var grid = activeBag.Grid;

        var cells = new JsonArray();
        for (var i = 0; i < grid.Cells.Length; i++)
        {
            var cell = grid.Cells[i];
            var pos = Position.FromIndex(i, grid.Columns);
            var cellObj = new JsonObject
            {
                ["row"] = pos.Row,
                ["col"] = pos.Col,
                ["empty"] = cell.IsEmpty
            };

            if (!cell.IsEmpty)
            {
                var stack = cell.Stack!;
                cellObj["item"] = stack.ItemType.Name;
                cellObj["category"] = stack.ItemType.Category.ToString();
                cellObj["count"] = stack.Count;
                cellObj["maxStack"] = stack.ItemType.EffectiveMaxStackSize;
                cellObj["hasBag"] = stack.ContainedBag is not null;
            }

            if (cell.CategoryFilter is not null)
                cellObj["filter"] = cell.CategoryFilter.Value.ToString();

            if (cell.Frame is not null)
                cellObj["frame"] = cell.Frame.GetType().Name;

            cells.Add(cellObj);
        }

        // Hand bag contents
        var hand = new JsonArray();
        foreach (var cell in state.HandBag.Grid.Cells)
        {
            if (!cell.IsEmpty)
            {
                hand.Add(new JsonObject
                {
                    ["item"] = cell.Stack!.ItemType.Name,
                    ["count"] = cell.Stack.Count
                });
            }
        }

        // Breadcrumb path
        var breadcrumbs = new JsonArray();
        foreach (var crumb in state.BreadcrumbPath)
            breadcrumbs.Add(crumb);

        // Recent action log
        var log = new JsonArray();
        foreach (var entry in session.ActionLog.TakeLast(20))
            log.Add(entry);

        return new JsonObject
        {
            ["gridColumns"] = grid.Columns,
            ["gridRows"] = grid.Rows,
            ["cells"] = cells,
            ["cursor"] = new JsonObject
            {
                ["row"] = state.Cursor.Position.Row,
                ["col"] = state.Cursor.Position.Col
            },
            ["hand"] = hand,
            ["handEmpty"] = !state.HasItemsInHand,
            ["breadcrumbs"] = breadcrumbs,
            ["isNested"] = state.IsNested,
            ["tickMode"] = session.TickMode.ToString(),
            ["tickCount"] = session.TickCount,
            ["actionLog"] = log
        };
    }
}
