using System.Collections.Immutable;
using Pockets.Core.Models;

namespace Pockets.Core.Data;

/// <summary>
/// Orchestrates the parse → resolve pipeline for loading game content from markdown files.
/// Phase 1 (parse): extract typed blocks from markdown, parse into raw definitions.
/// Phase 2 (resolve): cross-reference item names, build recipe output factories.
/// </summary>
public static class ContentLoader
{
    /// <summary>
    /// Loads and resolves content from a single markdown string.
    /// For self-contained files where all cross-references exist within the file.
    /// </summary>
    public static ContentRegistry LoadFromMarkdown(string markdown, string sourceFile)
    {
        var raw = ParseRaw(markdown, sourceFile);
        return Resolve(raw);
    }

    /// <summary>
    /// Loads content from all .md files found recursively under directoryPath.
    /// All files are parsed first, raw data merged, then cross-references resolved.
    /// </summary>
    public static ContentRegistry LoadFromDirectory(string directoryPath)
    {
        var raw = Directory.EnumerateFiles(directoryPath, "*.md", SearchOption.AllDirectories)
            .Select(file => ParseRaw(File.ReadAllText(file), file))
            .Aggregate(RawContent.Empty, (acc, r) => acc.Merge(r));

        return Resolve(raw);
    }

    /// <summary>
    /// Phase 1: parse a markdown string into raw (unresolved) content.
    /// No cross-reference resolution — item names are still strings.
    /// </summary>
    private static RawContent ParseRaw(string markdown, string sourceFile)
    {
        var blocks = ContentBlockParser.Parse(markdown, sourceFile);

        var items = blocks
            .Where(b => b.Type == "Item")
            .Select(ContentParsers.ParseItem)
            .ToImmutableDictionary(item => item.Name);

        var gridTemplates = blocks
            .Where(b => b.Type == "GridTemplate")
            .Select(ContentParsers.ParseGridTemplate)
            .ToImmutableDictionary(t => t.Id);

        var lootTableTemplates = blocks
            .Where(b => b.Type == "LootTableTemplate")
            .Select(ContentParsers.ParseLootTableTemplate)
            .ToImmutableDictionary(t => t.Id);

        var facilities = blocks
            .Where(b => b.Type == "Facility")
            .Select(ContentParsers.ParseFacility)
            .ToImmutableDictionary(f => f.Id);

        var recipeDefs = blocks
            .Where(b => b.Type == "Recipe")
            .Select(ContentParsers.ParseRecipe)
            .ToImmutableDictionary(r => r.Id);

        return new RawContent(items, recipeDefs, facilities, gridTemplates, lootTableTemplates);
    }

    /// <summary>
    /// Phase 2: resolve cross-references in raw content.
    /// Builds Recipe objects from RecipeDefinitions by resolving item names to ItemTypes
    /// and constructing output factories.
    /// </summary>
    private static ContentRegistry Resolve(RawContent raw)
    {
        // Build template lookup for pipeline execution (GridTemplates + LootTableTemplates)
        var templates = raw.GridTemplates
            .ToImmutableDictionary(kv => kv.Key, kv => (object)kv.Value)
            .SetItems(raw.LootTableTemplates
                .ToImmutableDictionary(kv => kv.Key, kv => (object)kv.Value));

        var generators = GeneratorBuiltins.GetAll(raw.Items);

        var recipes = raw.RecipeDefs
            .Select(kv => ResolveRecipe(kv.Value, raw.Items, templates, generators))
            .ToImmutableDictionary(r => r.Id);

        return new ContentRegistry(raw.Items, recipes, raw.Facilities,
            raw.GridTemplates, raw.LootTableTemplates);
    }

    /// <summary>
    /// Resolves a RecipeDefinition into a fully-resolved Recipe by looking up item names
    /// in the provided item dictionary. For static-only pipelines, builds an OutputFactory
    /// that produces ItemStacks. For pipelines with generators, executes the pipeline via
    /// PipelineExecutor to produce outputs (called fresh each invocation for unique instances).
    /// </summary>
    private static Recipe ResolveRecipe(
        RecipeDefinition def,
        ImmutableDictionary<string, ItemType> itemsByName,
        ImmutableDictionary<string, object> templates,
        ImmutableDictionary<string, GeneratorFunc> generators)
    {
        var inputs = def.Inputs
            .Select(input => new RecipeInput(itemsByName[input.ItemName], input.Count))
            .ToList();

        var allStatic = def.OutputPipeline.All(step => step is StaticItemStep);
        var pipeline = def.OutputPipeline;

        Func<IReadOnlyList<ItemStack>> outputFactory;

        if (allStatic)
        {
            var outputStacks = pipeline
                .Cast<StaticItemStep>()
                .Select(step => new ItemStack(itemsByName[step.ItemName], step.Count))
                .ToList();

            outputFactory = () => outputStacks;
        }
        else
        {
            // Capture pipeline context for deferred execution
            outputFactory = () =>
            {
                var result = PipelineExecutor.Execute(pipeline, itemsByName, templates, generators);
                return result switch
                {
                    StacksValue sv => sv.Stacks,
                    BagValue bv => new[]
                    {
                        // Find the static item step for the output item name/count
                        pipeline.OfType<StaticItemStep>().Select(s =>
                            new ItemStack(itemsByName[s.ItemName], s.Count, ContainedBag: bv.Bag))
                            .FirstOrDefault()
                        ?? new ItemStack(
                            new ItemType("Unknown", Category.Bag, IsStackable: false),
                            1, ContainedBag: bv.Bag)
                    },
                    _ => Array.Empty<ItemStack>()
                };
            };
        }

        return new Recipe(
            def.Id,
            def.Name,
            inputs,
            outputFactory,
            def.Duration,
            def.GridColumns,
            def.GridRows);
    }

    /// <summary>
    /// Intermediate container for parsed-but-unresolved content from one or more files.
    /// </summary>
    private record RawContent(
        ImmutableDictionary<string, ItemType> Items,
        ImmutableDictionary<string, RecipeDefinition> RecipeDefs,
        ImmutableDictionary<string, FacilityDefinition> Facilities,
        ImmutableDictionary<string, GridTemplate> GridTemplates,
        ImmutableDictionary<string, LootTableTemplate> LootTableTemplates)
    {
        public static RawContent Empty { get; } = new(
            ImmutableDictionary<string, ItemType>.Empty,
            ImmutableDictionary<string, RecipeDefinition>.Empty,
            ImmutableDictionary<string, FacilityDefinition>.Empty,
            ImmutableDictionary<string, GridTemplate>.Empty,
            ImmutableDictionary<string, LootTableTemplate>.Empty);

        public RawContent Merge(RawContent other) => new(
            Items.SetItems(other.Items),
            RecipeDefs.SetItems(other.RecipeDefs),
            Facilities.SetItems(other.Facilities),
            GridTemplates.SetItems(other.GridTemplates),
            LootTableTemplates.SetItems(other.LootTableTemplates));
    }
}
