using System.Collections.Immutable;
using Pockets.Core.Models;

namespace Pockets.Core.Data;

/// <summary>
/// Orchestrates the parse → resolve pipeline for loading game content from markdown files.
/// Parses ContentBlocks, routes them to typed parsers, and resolves cross-references
/// (e.g. recipe inputs referencing item names) into fully-resolved domain objects.
/// </summary>
public static class ContentLoader
{
    /// <summary>
    /// Loads content from a single markdown string. Parses all blocks, routes each to
    /// the appropriate parser, then resolves recipes using the parsed item set.
    /// </summary>
    public static ContentRegistry LoadFromMarkdown(string markdown, string sourceFile)
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
            .ToList();

        var recipes = recipeDefs
            .Select(def => ResolveRecipe(def, items))
            .ToImmutableDictionary(r => r.Id);

        return new ContentRegistry(items, recipes, facilities, gridTemplates, lootTableTemplates);
    }

    /// <summary>
    /// Loads content from all .md files found recursively under directoryPath.
    /// Merges all registries together; later files override earlier ones on key conflict.
    /// </summary>
    public static ContentRegistry LoadFromDirectory(string directoryPath) =>
        Directory.EnumerateFiles(directoryPath, "*.md", SearchOption.AllDirectories)
            .Select(file => LoadFromMarkdown(File.ReadAllText(file), file))
            .Aggregate(ContentRegistry.Empty, (acc, reg) => acc.Merge(reg));

    /// <summary>
    /// Resolves a RecipeDefinition into a fully-resolved Recipe by looking up item names
    /// in the provided item dictionary. For static-only pipelines, builds an OutputFactory
    /// that produces ItemStacks. For pipelines with generators or template refs, returns
    /// a placeholder factory producing an empty list (generator execution wired up later).
    /// </summary>
    private static Recipe ResolveRecipe(
        RecipeDefinition def,
        ImmutableDictionary<string, ItemType> itemsByName)
    {
        var inputs = def.Inputs
            .Select(input => new RecipeInput(itemsByName[input.ItemName], input.Count))
            .ToList();

        var allStatic = def.OutputPipeline.All(step => step is StaticItemStep);

        Func<IReadOnlyList<ItemStack>> outputFactory;

        if (allStatic)
        {
            // Capture the resolved output stacks for the factory closure.
            var outputStacks = def.OutputPipeline
                .Cast<StaticItemStep>()
                .Select(step => new ItemStack(itemsByName[step.ItemName], step.Count))
                .ToList();

            outputFactory = () => outputStacks;
        }
        else
        {
            // Placeholder: generator/template execution will be wired up later.
            outputFactory = () => Array.Empty<ItemStack>();
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
}
