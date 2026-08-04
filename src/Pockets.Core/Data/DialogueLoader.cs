using System.Collections.Immutable;
using System.Text.RegularExpressions;
using Pockets.Core.Models;

namespace Pockets.Core.Data;

/// <summary>
/// Loads dialogue beats from human-editable markdown, the same posture as item cards in <c>/data</c>.
/// Reuses <see cref="ContentBlockParser"/> (blocks delimited by <c>"# Type: Id"</c>) and picks the
/// <c>Dialogue</c> blocks — so a dialogue file can live right alongside item cards under <c>/data</c>
/// and the item <see cref="ContentLoader"/> simply ignores its unknown block type.
///
/// Block shape:
/// <code>
/// # Dialogue: opening
/// Trigger: GameStart
/// Reveals: Grid
///
/// - (groggy) …cold. Was I reaching for something?
/// </code>
/// Fields: <c>Trigger:</c> (required, see <see cref="DialogueTrigger.Parse"/>), <c>Reveals:</c>
/// (optional <see cref="ChromeElement"/> materialized on dismiss). Body: one line per
/// <c>- (portrait) text</c> entry.
/// </summary>
public static class DialogueLoader
{
    private static readonly Regex LinePattern = new(@"^-\s*\((?<portrait>[^)]*)\)\s*(?<text>.+)$", RegexOptions.Compiled);

    /// <summary>Loads every <c>Dialogue</c> block found recursively under <paramref name="directoryPath"/>.</summary>
    public static DialogueBook LoadFromDirectory(string directoryPath)
    {
        var beats = Directory.EnumerateFiles(directoryPath, "*.md", SearchOption.AllDirectories)
            .SelectMany(file => ContentBlockParser.Parse(File.ReadAllText(file), file))
            .Where(b => b.Type == "Dialogue")
            .Select(ParseBeat)
            .ToImmutableDictionary(b => b.Id);

        return new DialogueBook(beats);
    }

    /// <summary>Loads dialogue beats from a single markdown string (self-contained; for tests).</summary>
    public static DialogueBook LoadFromMarkdown(string markdown, string sourceFile = "<inline>")
    {
        var beats = ContentBlockParser.Parse(markdown, sourceFile)
            .Where(b => b.Type == "Dialogue")
            .Select(ParseBeat)
            .ToImmutableDictionary(b => b.Id);

        return new DialogueBook(beats);
    }

    /// <summary>Parses one <c>Dialogue</c> content block into a <see cref="DialogueBeat"/>.</summary>
    private static DialogueBeat ParseBeat(ContentBlock block)
    {
        if (!block.Fields.TryGetValue("Trigger", out var triggerRaw))
            throw new FormatException($"Dialogue beat '{block.Id}' is missing a Trigger field");
        var trigger = DialogueTrigger.Parse(triggerRaw);

        ChromeElement? reveals = null;
        if (block.Fields.TryGetValue("Reveals", out var revealsRaw))
        {
            if (!Enum.TryParse<ChromeElement>(revealsRaw, ignoreCase: true, out var element))
                throw new FormatException($"Dialogue beat '{block.Id}' Reveals unknown chrome: {revealsRaw}");
            reveals = element;
        }

        var lines = block.Body
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => LinePattern.Match(l.Trim()))
            .Where(m => m.Success)
            .Select(m => new DialogueLine(m.Groups["text"].Value.Trim(), m.Groups["portrait"].Value.Trim()))
            .ToImmutableArray();

        if (lines.Length == 0)
            throw new FormatException($"Dialogue beat '{block.Id}' has no lines (expected '- (portrait) text')");

        return new DialogueBeat(block.Id, trigger, lines, reveals);
    }
}
