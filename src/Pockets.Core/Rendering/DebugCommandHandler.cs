using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using Pockets.Core.Fine;
using Pockets.Core.Models;
using Pockets.Core.Sim;

namespace Pockets.Core.Rendering;

/// <summary>
/// Transport-agnostic dispatch for the debug agent protocol. Parses a JSON command,
/// drives a <see cref="GameController"/>, and returns a JSON response embedding the
/// canonical view-model (see <see cref="ViewModelSerializer"/>).
///
/// This is the code the Godot <c>DebugWebSocketServer</c> runs per packet, lifted into
/// Core so that (a) the mock-transport Godot driver in the journey runner exercises the
/// exact same server-side path as the real WebSocket server, and (b) the two share one
/// serializer — removing a source of TUI↔Godot drift.
///
/// Protocol (screenshot is transport-specific and handled by the Godot shell, not here):
///   {"action":"key","key":"Primary"}                         → HandleKey
///   {"action":"click","row":0,"col":2,"button":"Primary"}    → HandleGridClick
///   {"action":"back"}                                         → HandleBackClick
///   {"action":"tick"}                                         → Tick
///   {"action":"advanceTime","ms":5000}                        → AdvanceClock (scripted clock advance)
///   {"action":"state"}                                        → serialize (no mutation)
///   {"action":"fine"}                                         → the L2a fine state as a <c>fine</c> line array (no mutation)
///   {"action":"setOffset","loc":"B","index":3,"dx":2,"dy":-1} → SetFineOffset (test-only, P2-D11)
/// <c>Pockets.Realization.PoseDebugCommandHandler</c> wraps this dispatch with a <c>poses</c> query and a
/// <c>poses</c> field on every success response (Slice 2.4), because Core cannot reference Realization; it plugs in
/// through the <c>extend</c> callback of the four-argument <see cref="Handle(GameController, string, out bool, Action{JsonNode, ControllerResult, JsonObject}?)"/>.
/// Response: {"handled":bool,"status":"...","state":{...},"events":["…"]} or {"error":"..."}.
/// <c>events</c> (Slice 1.6) is the command's <see cref="ControllerResult.Events"/> as canonical
/// <see cref="EventText"/> lines (P1-D8), so the Godot WebSocket driver and mock-godot see the same stream the
/// TUI consumes; it is empty for a query. Bags are named <c>bag#N</c> by one stream per controller, so a bag
/// keeps its name across responses.
/// </summary>
public static class DebugCommandHandler
{
    /// <summary>
    /// One <see cref="EventText"/> stream per controller (a debug run), held weakly so a dropped controller
    /// takes its naming with it. The handler is static and the transports pass only the controller.
    /// </summary>
    private static readonly ConditionalWeakTable<GameController, EventText> Streams = new();

    /// <summary>
    /// Handles a single JSON command string against the controller and returns the JSON
    /// response string. Mutating commands advance the controller in place. Returns true in
    /// <paramref name="mutated"/> when the command may have changed state (key/click/back/tick),
    /// letting a transport refresh its own UI; false for pure queries / errors.
    /// </summary>
    public static string Handle(GameController controller, string json, out bool mutated) =>
        Handle(controller, json, out mutated, null);

    /// <summary>
    /// The controller's one <see cref="EventText"/> naming stream (created on first use), the one the <c>events</c>
    /// lines are written with, so another stream (poses) can share its bag names.
    /// </summary>
    public static EventText EventTextFor(GameController controller) =>
        Streams.GetValue(controller, _ => new EventText());

    /// <summary>
    /// <see cref="Handle(GameController, string, out bool)"/> with an optional <paramref name="extend"/> callback,
    /// invoked as <c>extend(command, result, response)</c> for every success response after the standard fields are
    /// set and before it is serialized (never for an error), so a wrapper can add fields.
    /// </summary>
    public static string Handle(GameController controller, string json, out bool mutated,
        Action<JsonNode, ControllerResult, JsonObject>? extend)
    {
        mutated = false;
        JsonNode? cmd;
        try
        {
            cmd = JsonNode.Parse(json);
        }
        catch (System.Exception ex)
        {
            return Error($"Could not parse JSON: {ex.Message}");
        }

        if (cmd is null)
            return Error("Could not parse JSON");

        var action = cmd["action"]?.GetValue<string>();
        if (action is null)
            return Error("Missing 'action' field");

        switch (action)
        {
            case "key":
                mutated = true;
                return HandleKey(controller, cmd, extend);
            case "click":
                mutated = true;
                return HandleClick(controller, cmd, extend);
            case "back":
                mutated = true;
                return Success(controller, controller.HandleBackClick(), cmd, extend);
            case "tick":
                mutated = true;
                return Success(controller, controller.Tick(), cmd, extend);
            case "advanceTime":
                mutated = true;
                var ms = cmd["ms"]?.GetValue<long>() ?? 0;
                return Success(controller, controller.AdvanceClock(System.TimeSpan.FromMilliseconds(ms)), cmd, extend);
            case "state":
                return Success(controller, ControllerResult.Handle(controller.Session, "State query"), cmd, extend);
            case "fine":
                return Success(controller, ControllerResult.Handle(controller.Session, "Fine query"), cmd,
                    (c, r, response) =>
                    {
                        response["fine"] = new JsonArray(FineText.Lines(controller.Session.Fine, EventTextFor(controller).Names)
                            .Select(l => (JsonNode?)JsonValue.Create(l)).ToArray());
                        extend?.Invoke(c, r, response);
                    });
            case "setOffset":
                return HandleSetOffset(controller, cmd, extend, out mutated);
            default:
                return Error($"Unknown action: {action}");
        }
    }

    /// <summary>
    /// Convenience overload without the <c>mutated</c> flag.
    /// </summary>
    public static string Handle(GameController controller, string json) =>
        Handle(controller, json, out _);

    private static string HandleKey(GameController controller, JsonNode cmd,
        Action<JsonNode, ControllerResult, JsonObject>? extend)
    {
        var keyName = cmd["key"]?.GetValue<string>();
        if (keyName is null)
            return Error("Missing 'key' field");

        if (!System.Enum.TryParse<GameKey>(keyName, ignoreCase: true, out var gameKey))
            return Error($"Unknown key: {keyName}. Valid: {string.Join(", ", System.Enum.GetNames<GameKey>())}");

        return Success(controller, controller.HandleKey(gameKey), cmd, extend);
    }

    /// <summary>
    /// <c>setOffset</c>: validates <c>loc</c> (a defined <see cref="LocationId"/> name) and the int fields
    /// <c>index</c>, <c>dx</c>, <c>dy</c>, then sets the offset. Any missing or bad field is an error and no mutation.
    /// </summary>
    private static string HandleSetOffset(GameController controller, JsonNode cmd,
        Action<JsonNode, ControllerResult, JsonObject>? extend, out bool mutated)
    {
        mutated = false;
        var locName = cmd["loc"]?.GetValue<string>();
        if (locName is null || !System.Enum.TryParse<LocationId>(locName, ignoreCase: true, out var loc)
            || !System.Enum.GetNames<LocationId>().Contains(locName, StringComparer.OrdinalIgnoreCase))
            return Error($"Missing or unknown 'loc': {locName}. Valid: {string.Join(", ", System.Enum.GetNames<LocationId>())}");
        if (!TryInt(cmd, "index", out var index) || !TryInt(cmd, "dx", out var dx) || !TryInt(cmd, "dy", out var dy))
            return Error("Missing or invalid 'index'/'dx'/'dy' fields");

        mutated = true;
        return Success(controller, controller.SetFineOffset(loc, index, dx, dy), cmd, extend);
    }

    /// <summary>Reads <paramref name="field"/> of <paramref name="cmd"/> as an int; false when absent or not an int.</summary>
    private static bool TryInt(JsonNode cmd, string field, out int value)
    {
        value = 0;
        try
        {
            if (cmd[field] is not JsonValue v) return false;
            value = v.GetValue<int>();
            return true;
        }
        catch (System.Exception)
        {
            return false;
        }
    }

    private static string HandleClick(GameController controller, JsonNode cmd,
        Action<JsonNode, ControllerResult, JsonObject>? extend)
    {
        var row = cmd["row"]?.GetValue<int>() ?? -1;
        var col = cmd["col"]?.GetValue<int>() ?? -1;
        if (row < 0 || col < 0)
            return Error("Missing or invalid 'row'/'col' fields");

        var buttonName = cmd["button"]?.GetValue<string>() ?? "Primary";
        if (!System.Enum.TryParse<ClickType>(buttonName, ignoreCase: true, out var clickType))
            clickType = ClickType.Primary;

        return Success(controller, controller.HandleGridClick(new Position(row, col), clickType), cmd, extend);
    }

    /// <summary>
    /// The success response for <paramref name="result"/>: handled, status, the canonical view-model, and the
    /// result's events as this controller's <see cref="EventText"/> lines;
    /// then <paramref name="extend"/>, when given, may add fields.
    /// </summary>
    private static string Success(GameController controller, ControllerResult result, JsonNode cmd,
        Action<JsonNode, ControllerResult, JsonObject>? extend)
    {
        var text = EventTextFor(controller);
        var response = new JsonObject
        {
            ["handled"] = result.Handled,
            ["status"] = result.StatusMessage,
            ["state"] = ViewModelSerializer.Serialize(controller.Session),
            ["events"] = new JsonArray(result.Events.Select(e => (JsonNode?)JsonValue.Create(text.Line(e))).ToArray())
        };
        extend?.Invoke(cmd, result, response);
        return response.ToJsonString();
    }

    private static string Error(string message) =>
        new JsonObject { ["error"] = message }.ToJsonString();
}
