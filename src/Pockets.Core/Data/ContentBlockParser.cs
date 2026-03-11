using System.Collections.Immutable;
using System.Text.RegularExpressions;

namespace Pockets.Core.Data;

/// <summary>
/// Parses a markdown string into a list of ContentBlock records.
/// Blocks are delimited by lines matching "# Type: Id" headers.
/// Within each block, fields come first (key: value or **key**: value),
/// then a blank line separator, then body text.
/// </summary>
public static class ContentBlockParser
{
    private static readonly Regex HeaderPattern = new(@"^#\s+(\w+):\s+(.+)$", RegexOptions.Compiled);
    private static readonly Regex FieldPattern = new(@"^(?:\*\*)?(\w+(?:\s\w+){0,2})(?:\*\*)?:\s+(.+)$", RegexOptions.Compiled);

    /// <summary>
    /// Splits markdown content on "# Type: Id" headers and parses each block's fields and body.
    /// Fields are parsed from lines before the first blank line; body is everything after.
    /// </summary>
    public static IReadOnlyList<ContentBlock> Parse(string markdown, string sourceFile)
    {
        var lines = markdown.Split('\n').Select(l => l.TrimEnd()).ToList();

        var headerIndices = lines
            .Select((line, idx) => (line, idx))
            .Where(t => HeaderPattern.IsMatch(t.line))
            .Select(t => t.idx)
            .ToList();

        if (headerIndices.Count == 0)
            return Array.Empty<ContentBlock>();

        return headerIndices
            .Select((headerIdx, i) =>
            {
                var headerLine = lines[headerIdx];
                var headerMatch = HeaderPattern.Match(headerLine);
                var type = headerMatch.Groups[1].Value;
                var id = headerMatch.Groups[2].Value.Trim();

                var nextHeaderIdx = i + 1 < headerIndices.Count ? headerIndices[i + 1] : lines.Count;
                var blockLines = lines.Skip(headerIdx + 1).Take(nextHeaderIdx - headerIdx - 1).ToList();

                // Split on first blank line: fields before, body after
                var blankIndex = blockLines.FindIndex(l => string.IsNullOrWhiteSpace(l));
                var fieldLines = blankIndex >= 0 ? blockLines.Take(blankIndex) : blockLines;
                var bodyLines = blankIndex >= 0 ? blockLines.Skip(blankIndex + 1) : Enumerable.Empty<string>();

                var fields = fieldLines
                    .Select(l => FieldPattern.Match(l))
                    .Where(m => m.Success)
                    .ToImmutableDictionary(
                        m => m.Groups[1].Value,
                        m => m.Groups[2].Value.Trim());

                var body = string.Join('\n', bodyLines
                    .Where(l => !string.IsNullOrWhiteSpace(l)))
                    .Trim();

                return new ContentBlock(type, id, fields, body, sourceFile);
            })
            .ToList();
    }
}
