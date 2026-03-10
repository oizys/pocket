using System.Collections.Immutable;

namespace Pockets.Core.Data;

/// <summary>
/// A single definition block parsed from a markdown file.
/// Represents one "# Type: Id" section with its fields and body text.
/// </summary>
public record ContentBlock(
    string Type,
    string Id,
    ImmutableDictionary<string, string> Fields,
    string Body,
    string SourceFile);
