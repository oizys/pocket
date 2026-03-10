using System.Collections.Immutable;
using System.Text.RegularExpressions;

namespace Pockets.Core.Data;

/// <summary>
/// Parses a markdown string into a list of ContentBlock records.
/// Blocks are delimited by lines matching "# Type: Id" headers.
/// </summary>
public static class ContentBlockParser
{
    private static readonly Regex HeaderPattern = new(@"^#\s+(\w+):\s+(.+)$", RegexOptions.Compiled);
    private static readonly Regex FieldPattern = new(@"^\*\*(.+?)\*\*:\s*(.*)$", RegexOptions.Compiled);

    /// <summary>
    /// Splits markdown content on "# Type: Id" headers and parses each block's fields and body.
    /// Lines not matching the typed header pattern are ignored as block boundaries.
    /// </summary>
    public static IReadOnlyList<ContentBlock> Parse(string markdown, string sourceFile)
    {
        var lines = markdown.Split('\n').Select(l => l.TrimEnd()).ToList();

        // Find indices of lines that are typed headers (# Word: id)
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

                var fields = blockLines
                    .Select(l => FieldPattern.Match(l))
                    .Where(m => m.Success)
                    .ToImmutableDictionary(
                        m => m.Groups[1].Value,
                        m => m.Groups[2].Value.Trim());

                var body = string.Join('\n', blockLines
                    .Where(l => !FieldPattern.IsMatch(l) && !string.IsNullOrWhiteSpace(l)))
                    .Trim();

                return new ContentBlock(type, id, fields, body, sourceFile);
            })
            .ToList();
    }
}
