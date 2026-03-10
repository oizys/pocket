namespace Pockets.Core.Models;

/// <summary>
/// AST node for a single step in a recipe output pipeline.
/// Parsed from the Output field syntax: "3 Rock -> !wilderness(@forest) -> !shuffle"
/// </summary>
public abstract record PipelineStep;

/// <summary>
/// A static item output: "3 Plain Rock" or "1 Stone Axe".
/// Item name is resolved to an ItemType during the resolve phase.
/// </summary>
public sealed record StaticItemStep(string ItemName, int Count) : PipelineStep;

/// <summary>
/// A template reference: "@forest-6x4". Resolved to a GridTemplate, LootTableTemplate, etc.
/// </summary>
public sealed record TemplateRefStep(string TemplateId) : PipelineStep;

/// <summary>
/// A generator invocation: "!wilderness" or "!attach-bag(@belt-pouch, @forest-materials)".
/// Receives the previous pipeline value as input. TemplateArgs are resolved to templates.
/// </summary>
public sealed record GeneratorStep(string GeneratorId, IReadOnlyList<string> TemplateArgs) : PipelineStep;
