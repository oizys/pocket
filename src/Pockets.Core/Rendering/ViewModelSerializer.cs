using System.Text.Json;
using System.Text.Json.Nodes;
using Pockets.Core.Models;

namespace Pockets.Core.Rendering;

/// <summary>
/// Canonical, driver-agnostic projection of a <see cref="GameSession"/> into a JSON
/// view-model. This is the single source of truth for the parity checkpoint stream:
/// the TUI driver (headless FakeDriver), the Godot WebSocket server, and the mock
/// transport all serialize through here, so identical game state yields byte-identical
/// output by construction.
///
/// Determinism contract: the output contains NO nondeterministic fields (no bag GUIDs,
/// no wall-clock time). Every value is a pure function of the immutable GameState /
/// GameSession, so two runs of the same script over the same demo profile diff clean.
/// </summary>
public static class ViewModelSerializer
{
    /// <summary>
    /// Number of trailing action-log entries included in the view-model. Kept in sync
    /// with the historical Godot debug-server payload so real-Godot state snapshots
    /// match the runner's checkpoints exactly.
    /// </summary>
    public const int ActionLogTail = 20;

    /// <summary>Stable order for serializing open non-inventory panels.</summary>
    private static readonly LocationId[] PanelOrder = { LocationId.C, LocationId.W, LocationId.T };

    /// <summary>
    /// Serializes the session's current view into a canonical <see cref="JsonObject"/>.
    /// Includes the active bag's grid, cursor, hand, breadcrumbs, tick info, and the
    /// tail of the action log.
    /// </summary>
    public static JsonObject Serialize(GameSession session)
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
                cellObj["hasBag"] = stack.ContainedBagId is not null;
            }

            if (cell.CategoryFilter is not null)
                cellObj["filter"] = cell.CategoryFilter.Value.ToString();

            if (cell.Frame is not null)
                cellObj["frame"] = cell.Frame.GetType().Name;

            cells.Add(cellObj);
        }

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

        // Toolbar (fixed inventory, Slice 3): the T bag's occupied slots, resolved straight from the
        // store so the projection is depth-invariant — identical no matter how deep B has navigated.
        // Each slot carries its index (so slot-fill order is diff-assertable) and, for a slot holding
        // a carrying bag, one level of nested contents (so an overflow pickup's destination cell inside
        // the nested bag is a first-class checkpoint signal). Absent when there is no toolbar.
        var toolbar = new JsonArray();
        if (state.ToolbarBagId is { } toolbarId && state.Store.GetById(toolbarId) is { } toolbarBag)
        {
            for (var i = 0; i < toolbarBag.Grid.Cells.Length; i++)
            {
                var cell = toolbarBag.Grid.Cells[i];
                if (cell.IsEmpty) continue;
                var slot = new JsonObject
                {
                    ["slot"] = i,
                    ["item"] = cell.Stack!.ItemType.Name,
                    ["count"] = cell.Stack.Count,
                    ["hasBag"] = cell.Stack.ContainedBagId is not null
                };
                if (cell.Stack.ContainedBagId is { } nestedId && state.Store.GetById(nestedId) is { } nestedBag)
                {
                    var contents = new JsonArray();
                    for (var j = 0; j < nestedBag.Grid.Cells.Length; j++)
                    {
                        var nc = nestedBag.Grid.Cells[j];
                        if (nc.IsEmpty) continue;
                        contents.Add(new JsonObject
                        {
                            ["slot"] = j,
                            ["item"] = nc.Stack!.ItemType.Name,
                            ["count"] = nc.Stack.Count
                        });
                    }
                    slot["contents"] = contents;
                }
                toolbar.Add(slot);
            }
        }

        var breadcrumbs = new JsonArray();
        foreach (var crumb in state.BreadcrumbPath)
            breadcrumbs.Add(crumb);

        // Open non-inventory panels (C=Container, W=World, T=Toolbar) as chrome-state.
        // Deterministic (id → bag env type), so opening/closing a bag as a panel is a
        // first-class, cross-driver-diffable checkpoint signal. Stable key order.
        var openPanels = new JsonObject();
        foreach (var id in PanelOrder)
        {
            var loc = state.Locations.TryGet(id);
            if (loc is null) continue;
            var bag = state.Store.GetById(loc.BagId);
            openPanels[id.ToString()] = bag?.EnvironmentType ?? "";
        }

        // UiLedger (chrome-as-state): every ChromeElement as a bool, in enum order (stable and
        // independent of the underlying set's iteration order) so the field is diff-clean across
        // drivers and journey steps can subset-assert individual flags (e.g. {"toolbar": true}).
        var ui = new JsonObject();
        foreach (var element in Enum.GetValues<ChromeElement>())
            ui[ToCamel(element.ToString())] = state.Ui.Has(element);

        // Active dialogue (beat id, line text, portrait tag, advance index) resolved against the
        // session's beat book — null when the box is closed. This is the cross-driver checkpoint
        // signal for the demo's narrative beats; the text comes straight from the data file.
        JsonNode? dialogue = null;
        var dlg = state.Dialogue;
        if (dlg.IsActive && session.Beats.Get(dlg.ActiveBeatId!) is { } beat && dlg.LineIndex < beat.Lines.Length)
        {
            var line = beat.Lines[dlg.LineIndex];
            dialogue = new JsonObject
            {
                ["beatId"] = beat.Id,
                ["index"] = dlg.LineIndex,
                ["line"] = line.Text,
                ["portrait"] = line.Portrait
            };
        }

        var log = new JsonArray();
        foreach (var entry in session.ActionLog.TakeLast(ActionLogTail))
            log.Add(entry);

        return new JsonObject
        {
            ["gridColumns"] = grid.Columns,
            ["gridRows"] = grid.Rows,
            ["activeBag"] = activeBag.EnvironmentType,
            ["cells"] = cells,
            ["cursor"] = new JsonObject
            {
                ["row"] = state.Cursor.Position.Row,
                ["col"] = state.Cursor.Position.Col
            },
            ["hand"] = hand,
            ["handEmpty"] = !state.HasItemsInHand,
            ["toolbar"] = toolbar,
            ["breadcrumbs"] = breadcrumbs,
            ["openPanels"] = openPanels,
            ["ui"] = ui,
            ["dialogue"] = dialogue,
            ["isNested"] = state.IsNested,
            ["tickMode"] = session.TickMode.ToString(),
            ["tickCount"] = session.TickCount,
            ["actionLog"] = log
        };
    }

    /// <summary>
    /// Serializes the session into a compact JSON string (single line, stable field
    /// order). Suitable for a checkpoint stream that is diffed across drivers.
    /// </summary>
    public static string SerializeToString(GameSession session) =>
        Serialize(session).ToJsonString();

    /// <summary>
    /// Serializes the session into an indented JSON string (human-readable checkpoint).
    /// </summary>
    public static string SerializeToPretty(GameSession session) =>
        Serialize(session).ToJsonString(PrettyOptions);

    /// <summary>Lower-cases the first character (PascalCase enum name → camelCase JSON key).</summary>
    private static string ToCamel(string s) =>
        string.IsNullOrEmpty(s) ? s : char.ToLowerInvariant(s[0]) + s[1..];

    private static readonly JsonSerializerOptions PrettyOptions = new()
    {
        WriteIndented = true,
        TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver()
    };
}
