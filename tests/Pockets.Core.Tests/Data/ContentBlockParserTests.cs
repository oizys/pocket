using Pockets.Core.Data;

namespace Pockets.Core.Tests.Data;

public class ContentBlockParserTests
{
    [Fact]
    public void ParseSingleItemBlock()
    {
        var markdown = """
            # Item: Plain Rock
            **Category**: Material
            **Stackable**: Yes

            A common grey stone found throughout the world.
            """;

        var blocks = ContentBlockParser.Parse(markdown, "test.md");

        Assert.Single(blocks);
        var block = blocks[0];
        Assert.Equal("Item", block.Type);
        Assert.Equal("Plain Rock", block.Id);
        Assert.Equal("Material", block.Fields["Category"]);
        Assert.Equal("Yes", block.Fields["Stackable"]);
        Assert.Contains("common grey stone", block.Body);
        Assert.Equal("test.md", block.SourceFile);
    }

    [Fact]
    public void ParseMultipleBlocksInOneFile()
    {
        var markdown = """
            # Item: Workbench
            **Category**: Structure
            **Stackable**: No
            A crafting station.

            # Facility: Workbench
            **Environment**: Workbench
            **ColorScheme**: Brown
            **Recipes**: stone-axe, stone-bench

            # Recipe: stone-axe
            **Name**: Stone Axe
            **Duration**: 3
            **Grid**: 3x1
            **Input 1**: Plain Rock ×5
            **Input 2**: Rough Wood ×3
            **Output**: 1 Stone Axe
            """;

        var blocks = ContentBlockParser.Parse(markdown, "workbench.md");

        Assert.Equal(3, blocks.Count);
        Assert.Equal("Item", blocks[0].Type);
        Assert.Equal("Workbench", blocks[0].Id);
        Assert.Equal("Facility", blocks[1].Type);
        Assert.Equal("Workbench", blocks[1].Id);
        Assert.Equal("Recipe", blocks[2].Type);
        Assert.Equal("stone-axe", blocks[2].Id);
    }

    [Fact]
    public void FieldsAreParsedCorrectly()
    {
        var markdown = """
            # Recipe: stone-axe
            **Name**: Stone Axe
            **Duration**: 3
            **Grid**: 3x1
            **Input 1**: Plain Rock ×5
            **Input 2**: Rough Wood ×3
            **Output**: 1 Stone Axe
            """;

        var blocks = ContentBlockParser.Parse(markdown, "test.md");
        var fields = blocks[0].Fields;

        Assert.Equal("Stone Axe", fields["Name"]);
        Assert.Equal("3", fields["Duration"]);
        Assert.Equal("3x1", fields["Grid"]);
        Assert.Equal("Plain Rock ×5", fields["Input 1"]);
        Assert.Equal("Rough Wood ×3", fields["Input 2"]);
        Assert.Equal("1 Stone Axe", fields["Output"]);
    }

    [Fact]
    public void BodyExcludesFieldLines()
    {
        var markdown = """
            # Item: Plain Rock
            **Category**: Material
            **Stackable**: Yes

            A common grey stone found throughout the world.
            It can be found in rivers and caves.
            """;

        var blocks = ContentBlockParser.Parse(markdown, "test.md");
        var body = blocks[0].Body;

        Assert.DoesNotContain("Category", body);
        Assert.DoesNotContain("Stackable", body);
        Assert.Contains("common grey stone", body);
        Assert.Contains("rivers and caves", body);
    }

    [Fact]
    public void EmptyFileReturnsNoBlocks()
    {
        var blocks = ContentBlockParser.Parse("", "empty.md");
        Assert.Empty(blocks);
    }

    [Fact]
    public void FileWithNoTypedHeadersReturnsNoBlocks()
    {
        var markdown = """
            # Just A Title
            Some text without a type prefix.
            """;

        var blocks = ContentBlockParser.Parse(markdown, "plain.md");
        Assert.Empty(blocks);
    }

    [Fact]
    public void WhitespaceInFieldValuesIsTrimmed()
    {
        var markdown = """
            # Item: Test
            **Category**:   Material
            **Stackable**:  Yes
            """;

        var blocks = ContentBlockParser.Parse(markdown, "test.md");
        Assert.Equal("Material", blocks[0].Fields["Category"]);
        Assert.Equal("Yes", blocks[0].Fields["Stackable"]);
    }

    [Fact]
    public void FieldsWithoutBoldAreParsed()
    {
        var markdown = """
            # Item: Plain Rock
            Category: Material
            Stackable: Yes

            A common grey stone.
            """;

        var blocks = ContentBlockParser.Parse(markdown, "test.md");
        Assert.Single(blocks);
        Assert.Equal("Material", blocks[0].Fields["Category"]);
        Assert.Equal("Yes", blocks[0].Fields["Stackable"]);
        Assert.Contains("common grey stone", blocks[0].Body);
    }

    [Fact]
    public void MixedBoldAndNonBoldFields()
    {
        var markdown = """
            # Recipe: test
            **Name**: Stone Axe
            Duration: 3
            **Grid**: 3x1
            Output: 1 Stone Axe
            """;

        var blocks = ContentBlockParser.Parse(markdown, "test.md");
        var fields = blocks[0].Fields;
        Assert.Equal("Stone Axe", fields["Name"]);
        Assert.Equal("3", fields["Duration"]);
        Assert.Equal("3x1", fields["Grid"]);
        Assert.Equal("1 Stone Axe", fields["Output"]);
    }

    [Fact]
    public void ProseWithColonsIsNotParsedAsField()
    {
        var markdown = """
            # Item: Test Rock
            Category: Material
            Stackable: Yes

            A stone often described as: the most common rock around.
            """;

        var blocks = ContentBlockParser.Parse(markdown, "test.md");
        Assert.Contains("described as", blocks[0].Body);
        Assert.DoesNotContain("described as", blocks[0].Fields.Values.First());
    }

    [Fact]
    public void FieldLikeLineInBodyIsNotParsedAsField()
    {
        var markdown = """
            # Item: Test Rock
            Category: Material

            Notes: this looks like a field but is body text after the blank line.
            """;

        var blocks = ContentBlockParser.Parse(markdown, "test.md");
        Assert.Single(blocks[0].Fields); // only Category
        Assert.Contains("Notes", blocks[0].Body);
    }

    [Fact]
    public void BlockWithNoBodyHasEmptyBody()
    {
        var markdown = """
            # Facility: Workbench
            Environment: Workbench
            ColorScheme: Brown
            Recipes: stone-axe
            """;

        var blocks = ContentBlockParser.Parse(markdown, "test.md");
        Assert.Equal("", blocks[0].Body);
        Assert.Equal(3, blocks[0].Fields.Count);
    }

    [Fact]
    public void TemplateTypesAreParsed()
    {
        var markdown = """
            # GridTemplate: forest-6x4
            **Columns**: 6
            **Rows**: 4
            **Environment**: Forest
            **ColorScheme**: Green

            # LootTableTemplate: forest-materials
            **Items**: Plain Rock ×2.0, Rough Wood ×3.0, Forest Seed ×0.5
            **FillRatio**: 0.6
            """;

        var blocks = ContentBlockParser.Parse(markdown, "forest.md");

        Assert.Equal(2, blocks.Count);
        Assert.Equal("GridTemplate", blocks[0].Type);
        Assert.Equal("forest-6x4", blocks[0].Id);
        Assert.Equal("LootTableTemplate", blocks[1].Type);
        Assert.Equal("forest-materials", blocks[1].Id);
    }
}
