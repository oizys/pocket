using System.Collections.Immutable;
using Pockets.Core.Models;

namespace Pockets.Core.Data;

/// <summary>
/// Loads ItemType definitions from markdown files.
/// </summary>
public static class ItemTypeLoader
{
    /// <summary>
    /// Parses a single markdown string into an ItemType.
    /// Optional config supplies the default max stack size when not specified in the file.
    /// </summary>
    public static ItemType ParseMarkdown(string filename, string content, GameConfig? config = null)
    {
        var defaultMax = config?.DefaultMaxStackSize ?? 20;

        var lines = content.Split('\n')
            .Select(l => l.Trim())
            .ToList();

        var name = lines
            .Where(l => l.StartsWith("# "))
            .Select(l => l[2..].Trim())
            .FirstOrDefault()
            ?? throw new ItemTypeParseException(filename, "Missing item name (no # heading found)");

        var category = ExtractField(lines, "Category", filename);
        if (!Enum.TryParse<Category>(category, ignoreCase: true, out var parsedCategory))
            throw new ItemTypeParseException(filename, $"Invalid category '{category}'");

        var stackableStr = ExtractField(lines, "Stackable", filename);
        var isStackable = stackableStr.Equals("Yes", StringComparison.OrdinalIgnoreCase);

        var maxStackSize = defaultMax;
        var maxStackField = TryExtractField(lines, "Max Stack Size");
        if (maxStackField is not null && int.TryParse(maxStackField, out var parsed))
            maxStackSize = parsed;

        var fieldPrefixes = new[] { "# ", "**Category**", "**Stackable**", "**Max Stack Size**" };
        var descriptionLines = lines
            .Where(l => l.Length > 0)
            .Where(l => !fieldPrefixes.Any(p => l.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        var description = string.Join("\n", descriptionLines);

        return new ItemType(name, parsedCategory, isStackable, maxStackSize, description);
    }

    /// <summary>
    /// Loads all .md files from a directory and parses them into ItemTypes.
    /// Optional config supplies the default max stack size when not specified in files.
    /// </summary>
    public static ImmutableArray<ItemType> LoadFromDirectory(string directoryPath, GameConfig? config = null)
    {
        return Directory.GetFiles(directoryPath, "*.md")
            .OrderBy(f => f)
            .Select(f => ParseMarkdown(Path.GetFileName(f), File.ReadAllText(f), config))
            .ToImmutableArray();
    }

    private static string ExtractField(List<string> lines, string fieldName, string filename)
    {
        return TryExtractField(lines, fieldName)
            ?? throw new ItemTypeParseException(filename, $"Missing required field '{fieldName}'");
    }

    private static string? TryExtractField(List<string> lines, string fieldName)
    {
        var prefix = $"**{fieldName}**:";
        return lines
            .Where(l => l.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .Select(l => l[prefix.Length..].Trim())
            .FirstOrDefault();
    }
}
