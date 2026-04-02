namespace Pockets.Core.Models;

/// <summary>
/// Discriminated union for values flowing through a recipe output pipeline.
/// Each step in the pipeline produces and/or consumes a PipelineValue.
/// </summary>
public abstract record PipelineValue;

/// <summary>
/// A resolved template (GridTemplate, LootTableTemplate, etc.) identified by id.
/// The Template field holds the parsed data record; consumers cast to the expected type.
/// </summary>
public sealed record TemplateValue(string Id, object Template) : PipelineValue;

/// <summary>
/// A list of item stacks produced by a static output or generator.
/// May carry newly created bags that need to be registered in the BagStore.
/// </summary>
public sealed record StacksValue(IReadOnlyList<ItemStack> Stacks, IReadOnlyList<Bag>? NewBags = null) : PipelineValue;

/// <summary>
/// An intermediate or final bag produced by a generator (e.g. wilderness, bag).
/// </summary>
public sealed record BagValue(Bag Bag) : PipelineValue;
