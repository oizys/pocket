using System.Text.Json;
using System.Text.Json.Nodes;
using Pockets.Core.Models;

namespace Pockets.JourneyRunner;

/// <summary>One ordered step of a journey script.</summary>
public record JourneyStep
{
    /// <summary>Optional label; when set the checkpoint carries it (else the step index).</summary>
    public string? Checkpoint { get; init; }

    /// <summary>Free-text note (no action).</summary>
    public string? Comment { get; init; }

    // --- Exactly one action per step (or none, for a pure checkpoint/assert). ---
    public GameKey? Key { get; init; }
    public int? Tick { get; init; }
    public bool Back { get; init; }
    public ClickSpec? Click { get; init; }
    /// <summary>Scripted clock advance in milliseconds (Slice 5 — deterministic, never wall-clock).</summary>
    public int? AdvanceTimeMs { get; init; }

    // --- Assertions evaluated after the action. ---
    /// <summary>Subset match against the observed view-model (recursive containment).</summary>
    public JsonObject? AssertViewModel { get; init; }
    /// <summary>Substring that must appear in the driver's render probe (skipped if none).</summary>
    public string? AssertRender { get; init; }

    /// <summary>
    /// Whether this step must conserve the global item census versus the previous step.
    /// Defaults to true; set false for steps that legitimately transform totals (craft
    /// completion, ambient plant maturity, acquire/harvest).
    /// </summary>
    public bool Conserves { get; init; } = true;

    /// <summary>
    /// Marks this step's checkpoint as a golden-capture point (Slice 8). At a golden checkpoint a
    /// driver that exposes a render surface records it: the TUI dumps its character buffer (→
    /// <c>--goldens</c>) and the real Godot driver saves a viewport screenshot (→ <c>--screenshots</c>).
    /// The target-demo journey flags exactly the progressive-UI ledger rows (each materialization
    /// moment), so the golden set is one artifact per chrome element the moment it comes into being.
    /// Ignored unless the corresponding output dir is passed; never affects the checkpoint stream.
    /// </summary>
    public bool Golden { get; init; }
}

/// <summary>A grid click target.</summary>
public record ClickSpec(int Row, int Col, ClickType Button);

/// <summary>A whole journey: ordered steps driven identically on every driver.</summary>
public record JourneyScript(string Name, string? Description, IReadOnlyList<JourneyStep> Steps)
{
    /// <summary>
    /// Parses a journey script from JSON. Shape:
    /// { "name": "...", "description": "...", "steps": [ { "key": "Right" }, ... ] }
    /// Each step: optional key|tick|back|click action, optional checkpoint/comment,
    /// optional assertViewModel/assertRender, optional conserves (default true).
    /// </summary>
    public static JourneyScript Load(string path)
    {
        var root = JsonNode.Parse(File.ReadAllText(path)) as JsonObject
            ?? throw new FormatException($"Journey script {path} is not a JSON object");

        var name = root["name"]?.GetValue<string>() ?? Path.GetFileNameWithoutExtension(path);
        var description = root["description"]?.GetValue<string>();

        var steps = new List<JourneyStep>();
        if (root["steps"] is JsonArray arr)
        {
            for (int i = 0; i < arr.Count; i++)
                steps.Add(ParseStep((JsonObject)arr[i]!, i));
        }

        return new JourneyScript(name, description, steps);
    }

    private static JourneyStep ParseStep(JsonObject o, int index)
    {
        GameKey? key = null;
        if (o["key"]?.GetValue<string>() is { } keyName)
        {
            if (!Enum.TryParse<GameKey>(keyName, ignoreCase: true, out var parsed))
                throw new FormatException($"Step {index}: unknown key '{keyName}'");
            key = parsed;
        }

        ClickSpec? click = null;
        if (o["click"] is JsonObject c)
        {
            var button = c["button"]?.GetValue<string>() ?? "Primary";
            if (!Enum.TryParse<ClickType>(button, ignoreCase: true, out var bt)) bt = ClickType.Primary;
            click = new ClickSpec(c["row"]?.GetValue<int>() ?? 0, c["col"]?.GetValue<int>() ?? 0, bt);
        }

        return new JourneyStep
        {
            Checkpoint = o["checkpoint"]?.GetValue<string>(),
            Comment = o["comment"]?.GetValue<string>(),
            Key = key,
            Tick = o["tick"]?.GetValue<int>(),
            Back = o["back"]?.GetValue<bool>() ?? false,
            Click = click,
            AdvanceTimeMs = o["advanceTime"]?.GetValue<int>(),
            AssertViewModel = o["assertViewModel"] as JsonObject,
            AssertRender = o["assertRender"]?.GetValue<string>(),
            Conserves = o["conserves"]?.GetValue<bool>() ?? true,
            Golden = o["golden"]?.GetValue<bool>() ?? false
        };
    }
}
