using System.Collections.Immutable;
using Pockets.Core.Models;

namespace Pockets.Core.Data;

/// <summary>
/// Intermediate parsed recipe before cross-reference resolution.
/// Item names are strings, resolved to ItemTypes in the resolve phase.
/// </summary>
public record RecipeDefinition(
    string Id,
    string Name,
    int GridColumns,
    int GridRows,
    ImmutableArray<(string ItemName, int Count)> Inputs,
    IReadOnlyList<PipelineStep> OutputPipeline,
    int Duration);

/// <summary>
/// Parsed facility definition. Recipe ids are resolved to Recipe refs in the resolve phase.
/// </summary>
public record FacilityDefinition(
    string Id,
    string EnvironmentType,
    string ColorScheme,
    ImmutableArray<string> RecipeIds);
