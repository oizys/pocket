using System.Collections.Immutable;
using Pockets.Core.Models;

namespace Pockets.Core.Data;

/// <summary>
/// Converts ContentBlock records into domain types.
/// Each parser method reads typed fields from the block's Fields dictionary
/// and returns an appropriate domain or intermediate type.
/// </summary>
public static class ContentParsers
{
    /// <summary>
    /// Parses an Item block into an ItemType.
    /// Required fields: Category, Stackable ("Yes"/"No").
    /// Optional fields: Max Stack Size (default 20).
    /// Body becomes Description. Name comes from block.Id.
    /// </summary>
    public static ItemType ParseItem(ContentBlock block)
    {
        var category = Enum.Parse<Category>(block.Fields["Category"]);
        var isStackable = block.Fields["Stackable"] == "Yes";
        var maxStackSize = block.Fields.TryGetValue("Max Stack Size", out var maxStr)
            ? int.Parse(maxStr)
            : 20;
        var description = block.Body;

        return new ItemType(block.Id, category, isStackable, maxStackSize, description);
    }

    /// <summary>
    /// Parses a Recipe block into a RecipeDefinition (intermediate type with unresolved item names).
    /// Required fields: Name, Duration, Grid ("CxR" format), Output.
    /// Input fields: numbered "Input 1", "Input 2", etc. in format "Item Name ×Count" or "Item Name xCount".
    /// </summary>
    public static RecipeDefinition ParseRecipe(ContentBlock block)
    {
        var name = block.Fields["Name"];
        var duration = int.Parse(block.Fields["Duration"]);
        var gridParts = block.Fields["Grid"].Split('x');
        var gridColumns = int.Parse(gridParts[0]);
        var gridRows = int.Parse(gridParts[1]);

        var inputs = block.Fields
            .Where(kv => kv.Key.StartsWith("Input "))
            .OrderBy(kv => int.Parse(kv.Key["Input ".Length..]))
            .Select(kv => ParseInput(kv.Value))
            .ToImmutableArray();

        var outputPipeline = PipelineParser.Parse(block.Fields["Output"]);

        return new RecipeDefinition(block.Id, name, gridColumns, gridRows, inputs, outputPipeline, duration);
    }

    /// <summary>
    /// Parses a single input entry in the format "Item Name ×Count" or "Item Name xCount".
    /// </summary>
    private static (string ItemName, int Count) ParseInput(string value)
    {
        // Support both × (U+00D7) and x (letter)
        var separatorIndex = value.LastIndexOfAny(['×', 'x']);
        var itemName = value[..separatorIndex].Trim();
        var count = int.Parse(value[(separatorIndex + 1)..].Trim());
        return (itemName, count);
    }

    /// <summary>
    /// Parses a Facility block into a FacilityDefinition.
    /// Required fields: Environment, ColorScheme, Recipes (comma-separated recipe ids).
    /// </summary>
    public static FacilityDefinition ParseFacility(ContentBlock block)
    {
        var environmentType = block.Fields["Environment"];
        var colorScheme = block.Fields["ColorScheme"];
        var recipeIds = block.Fields["Recipes"]
            .Split(',')
            .Select(r => r.Trim())
            .ToImmutableArray();

        return new FacilityDefinition(block.Id, environmentType, colorScheme, recipeIds);
    }

    /// <summary>
    /// Parses a GridTemplate block into a GridTemplate.
    /// Required fields: Columns, Rows.
    /// Optional fields: Environment (default "Default"), ColorScheme (default "Default").
    /// Id comes from block.Id.
    /// </summary>
    public static GridTemplate ParseGridTemplate(ContentBlock block)
    {
        var columns = int.Parse(block.Fields["Columns"]);
        var rows = int.Parse(block.Fields["Rows"]);
        var environment = block.Fields.TryGetValue("Environment", out var env) ? env : "Default";
        var colorScheme = block.Fields.TryGetValue("ColorScheme", out var cs) ? cs : "Default";

        return new GridTemplate(block.Id, columns, rows, environment, colorScheme);
    }

    /// <summary>
    /// Parses a LootTableTemplate block into a LootTableTemplate.
    /// Required fields: Items (comma-separated "Item Name ×Weight" entries where Weight is double).
    /// Optional fields: FillRatio (default 0.5).
    /// Id comes from block.Id.
    /// </summary>
    public static LootTableTemplate ParseLootTableTemplate(ContentBlock block)
    {
        var entries = block.Fields["Items"]
            .Split(',')
            .Select(e => ParseLootEntry(e.Trim()))
            .ToImmutableArray();

        var fillRatio = block.Fields.TryGetValue("FillRatio", out var fr)
            ? double.Parse(fr)
            : 0.5;

        return new LootTableTemplate(block.Id, entries, fillRatio);
    }

    /// <summary>
    /// Parses a single loot table entry in the format "Item Name ×Weight".
    /// </summary>
    private static LootTableEntry ParseLootEntry(string entry)
    {
        var separatorIndex = entry.LastIndexOf('×');
        var itemName = entry[..separatorIndex].Trim();
        var weight = double.Parse(entry[(separatorIndex + 1)..].Trim());
        return new LootTableEntry(itemName, weight);
    }
}
