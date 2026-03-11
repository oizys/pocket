using System.Collections.Immutable;
using Pockets.Core.Data;
using Pockets.Core.Models;

namespace Pockets.Core.Tests.Data;

public class ContentLoaderTests
{
    [Fact]
    public void LoadFromMarkdown_ParsesItemsAndRecipes()
    {
        var markdown = """
            # Item: Plain Rock
            Category: Material
            Stackable: Yes

            A common grey stone.

            # Item: Rough Wood
            Category: Material
            Stackable: Yes

            A rough piece of wood.

            # Item: Stone Axe
            Category: Tool
            Stackable: No

            A crude axe.

            # Recipe: stone-axe
            Name: Stone Axe
            Duration: 3
            Grid: 3x1
            Input 1: Plain Rock ×5
            Input 2: Rough Wood ×3
            Output: 1 Stone Axe

            # Facility: Workbench
            Environment: Workbench
            ColorScheme: Brown
            Recipes: stone-axe
            """;

        var registry = ContentLoader.LoadFromMarkdown(markdown, "test.md");

        Assert.Equal(3, registry.Items.Count);
        Assert.True(registry.Items.ContainsKey("Plain Rock"));
        Assert.True(registry.Items.ContainsKey("Stone Axe"));

        Assert.Single(registry.Recipes);
        var recipe = registry.Recipes["stone-axe"];
        Assert.Equal("Stone Axe", recipe.Name);
        Assert.Equal(3, recipe.Duration);
        Assert.Equal(2, recipe.Inputs.Count);
        Assert.Equal("Plain Rock", recipe.Inputs[0].ItemType.Name);
        Assert.Equal(5, recipe.Inputs[0].Count);
        Assert.Equal("Rough Wood", recipe.Inputs[1].ItemType.Name);
        Assert.Equal(3, recipe.Inputs[1].Count);

        Assert.Single(registry.Facilities);
        Assert.Equal("Workbench", registry.Facilities["Workbench"].EnvironmentType);
    }

    [Fact]
    public void LoadFromMarkdown_ParsesTemplates()
    {
        var markdown = """
            # GridTemplate: forest-6x4
            Columns: 6
            Rows: 4
            Environment: Forest
            ColorScheme: Green

            # LootTableTemplate: forest-materials
            Items: Plain Rock ×2.0, Rough Wood ×3.0
            FillRatio: 0.6
            """;

        var registry = ContentLoader.LoadFromMarkdown(markdown, "test.md");

        Assert.Single(registry.GridTemplates);
        var grid = registry.GridTemplates["forest-6x4"];
        Assert.Equal(6, grid.Columns);
        Assert.Equal(4, grid.Rows);

        Assert.Single(registry.LootTableTemplates);
        var loot = registry.LootTableTemplates["forest-materials"];
        Assert.Equal(0.6, loot.FillRatio);
        Assert.Equal(2, loot.Entries.Length);
    }

    [Fact]
    public void LoadFromMarkdown_RecipeOutputFactory_ProducesCorrectStacks()
    {
        var markdown = """
            # Item: Plain Rock
            Category: Material
            Stackable: Yes

            # Item: Stone Axe
            Category: Tool
            Stackable: No

            # Recipe: stone-axe
            Name: Stone Axe
            Duration: 3
            Grid: 3x1
            Input 1: Plain Rock ×5
            Output: 1 Stone Axe
            """;

        var registry = ContentLoader.LoadFromMarkdown(markdown, "test.md");
        var recipe = registry.Recipes["stone-axe"];
        var output = recipe.OutputFactory();

        Assert.Single(output);
        Assert.Equal("Stone Axe", output[0].ItemType.Name);
        Assert.Equal(1, output[0].Count);
    }

    [Fact]
    public void LoadFromMarkdown_RecipeHasGridDimensions()
    {
        var markdown = """
            # Item: Plain Rock
            Category: Material
            Stackable: Yes

            # Recipe: test
            Name: Test
            Duration: 1
            Grid: 4x2
            Input 1: Plain Rock ×1
            Output: 1 Plain Rock
            """;

        var registry = ContentLoader.LoadFromMarkdown(markdown, "test.md");
        var recipe = registry.Recipes["test"];

        Assert.Equal(4, recipe.GridColumns);
        Assert.Equal(2, recipe.GridRows);
    }

    [Fact]
    public void LoadFromMultipleMarkdown_MergesRegistries()
    {
        var file1 = """
            # Item: Plain Rock
            Category: Material
            Stackable: Yes
            """;

        var file2 = """
            # Item: Rough Wood
            Category: Material
            Stackable: Yes
            """;

        var registry = ContentLoader.LoadFromMarkdown(file1, "items1.md")
            .Merge(ContentLoader.LoadFromMarkdown(file2, "items2.md"));

        Assert.Equal(2, registry.Items.Count);
        Assert.True(registry.Items.ContainsKey("Plain Rock"));
        Assert.True(registry.Items.ContainsKey("Rough Wood"));
    }

    [Fact]
    public void LoadFromDirectory_ScansRecursively()
    {
        // Create a temp directory structure with nested .md files
        var tempDir = Path.Combine(Path.GetTempPath(), $"pockets_test_{Guid.NewGuid():N}");
        var subDir = Path.Combine(tempDir, "items");
        Directory.CreateDirectory(subDir);

        try
        {
            File.WriteAllText(Path.Combine(tempDir, "rock.md"), """
                # Item: Plain Rock
                Category: Material
                Stackable: Yes

                A rock.
                """);

            File.WriteAllText(Path.Combine(subDir, "wood.md"), """
                # Item: Rough Wood
                Category: Material
                Stackable: Yes

                Some wood.
                """);

            var registry = ContentLoader.LoadFromDirectory(tempDir);

            Assert.Equal(2, registry.Items.Count);
            Assert.True(registry.Items.ContainsKey("Plain Rock"));
            Assert.True(registry.Items.ContainsKey("Rough Wood"));
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }
}
