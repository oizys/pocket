using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using Pockets.Core.Models;
using Pockets.Core.Rendering;

namespace Pockets.Realization;

/// <summary>
/// The debug protocol with poses (Slice 2.4): <see cref="DebugCommandHandler"/>'s dispatch plus one
/// <see cref="PoseStream"/> per controller. Every success response gains <c>"poses"</c> (the commit's pose lines; empty
/// for a <c>state</c> query, which is not a commit), and <c>{"action":"poses"}</c> returns the last commit's lines.
/// </summary>
public static class PoseDebugCommandHandler
{
    /// <summary>One pose stream per controller, held weakly like the event naming stream.</summary>
    private static readonly ConditionalWeakTable<GameController, PoseStream> Streams = new();

    /// <summary>
    /// Handles one JSON command: a <c>poses</c> query is answered here; everything else goes through the Core handler
    /// with a callback that appends the poses. <paramref name="mutated"/> is as the Core handler's.
    /// </summary>
    public static string Handle(GameController controller, string json, out bool mutated)
    {
        mutated = false;
        JsonNode? cmd;
        try { cmd = JsonNode.Parse(json); }
        catch (System.Exception) { cmd = null; }

        var stream = Streams.GetValue(controller,
            c => new PoseStream(c.Session.Current, DebugCommandHandler.EventTextFor(c).Names));

        if (cmd is JsonObject && cmd["action"]?.GetValue<string>() == "poses")
            return new JsonObject
            {
                ["handled"] = true,
                ["status"] = "Poses query",
                ["state"] = ViewModelSerializer.Serialize(controller.Session),
                ["events"] = new JsonArray(),
                ["poses"] = Strings(stream.Last)
            }.ToJsonString();

        return DebugCommandHandler.Handle(controller, json, out mutated, (c, result, response) =>
            response["poses"] = c["action"]?.GetValue<string>() == "state"
                ? new JsonArray()
                : Strings(stream.Commit(result.Events, controller.Session.Current, AtMs(c))));
    }

    /// <summary>Convenience overload without the <c>mutated</c> flag.</summary>
    public static string Handle(GameController controller, string json) => Handle(controller, json, out _);

    /// <summary>
    /// The commit's clock delta in ms: <c>advanceTime</c>'s <c>ms</c> (clamped into the int range), one tick's
    /// length for <c>tick</c>, else 0. Deterministic, never wall-clock; non-clock commits snap, so 0 is irrelevant there.
    /// </summary>
    public static int AtMs(JsonNode cmd)
    {
        switch (cmd["action"]?.GetValue<string>())
        {
            case "advanceTime":
                var ms = cmd["ms"] is JsonValue v ? (v.TryGetValue<long>(out var l) ? l : v.TryGetValue<int>(out var i) ? i : 0) : 0;
                return ms > int.MaxValue ? int.MaxValue : ms < int.MinValue ? int.MinValue : (int)ms;
            case "tick":
                return GameController.TickLengthMs;
            default:
                return 0;
        }
    }

    /// <summary>The lines as a JSON array of strings.</summary>
    private static JsonArray Strings(IEnumerable<string> lines) =>
        new(lines.Select(l => (JsonNode?)JsonValue.Create(l)).ToArray());
}
