using Pockets.Core.Models;
using Pockets.Core.Data;

namespace Pockets.Core.Tests.Data;

public class ItemTypeLoaderTests
{
    [Fact]
    public void ParseMarkdown_ValidStackableItemWithAllFields_ReturnsCorrectItemType()
    {
        var markdown = """
            # Iron Ore

            **Category**: Material
            **Stackable**: Yes
            **Max Stack Size**: 50

            A common metallic ore used in crafting.
            """;

        var result = ItemTypeLoader.ParseMarkdown("iron_ore.md", markdown);

        Assert.Equal("Iron Ore", result.Name);
        Assert.Equal(Category.Material, result.Category);
        Assert.True(result.IsStackable);
        Assert.Equal(50, result.MaxStackSize);
        Assert.Equal("A common metallic ore used in crafting.", result.Description);
    }

    [Fact]
    public void ParseMarkdown_MinimalItem_UsesDefaults()
    {
        var markdown = """
            # Stone

            **Category**: Material
            **Stackable**: Yes
            """;

        var result = ItemTypeLoader.ParseMarkdown("stone.md", markdown);

        Assert.Equal("Stone", result.Name);
        Assert.Equal(Category.Material, result.Category);
        Assert.True(result.IsStackable);
        Assert.Equal(20, result.MaxStackSize);
        Assert.Equal(string.Empty, result.Description);
    }

    [Fact]
    public void ParseMarkdown_CustomMaxStackSize_ReturnsCorrectValue()
    {
        var markdown = """
            # Gold Coin

            **Category**: Material
            **Stackable**: Yes
            **Max Stack Size**: 999
            """;

        var result = ItemTypeLoader.ParseMarkdown("gold_coin.md", markdown);

        Assert.Equal(999, result.MaxStackSize);
    }

    [Fact]
    public void ParseMarkdown_UniqueItem_HasEffectiveMaxStackSizeOne()
    {
        var markdown = """
            # Magic Sword

            **Category**: Weapon
            **Stackable**: No

            A legendary blade with mystical properties.
            """;

        var result = ItemTypeLoader.ParseMarkdown("magic_sword.md", markdown);

        Assert.Equal("Magic Sword", result.Name);
        Assert.Equal(Category.Weapon, result.Category);
        Assert.False(result.IsStackable);
        Assert.Equal(1, result.EffectiveMaxStackSize);
        Assert.Equal("A legendary blade with mystical properties.", result.Description);
    }

    [Fact]
    public void ParseMarkdown_MultiLineDescription_CombinesLines()
    {
        var markdown = """
            # Health Potion

            **Category**: Medicine
            **Stackable**: Yes

            A magical potion that restores health.
            Can be used in combat or out of combat.
            Very useful for survival.
            """;

        var result = ItemTypeLoader.ParseMarkdown("health_potion.md", markdown);

        var expectedDescription = "A magical potion that restores health.\nCan be used in combat or out of combat.\nVery useful for survival.";
        Assert.Equal(expectedDescription, result.Description);
    }

    [Fact]
    public void ParseMarkdown_MissingName_ThrowsItemTypeParseException()
    {
        var markdown = """
            **Category**: Material
            **Stackable**: Yes
            """;

        var exception = Assert.Throws<ItemTypeParseException>(() =>
            ItemTypeLoader.ParseMarkdown("no_name.md", markdown));

        Assert.Equal("no_name.md", exception.Filename);
        Assert.Contains("name", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ParseMarkdown_MissingCategory_ThrowsItemTypeParseException()
    {
        var markdown = """
            # Mysterious Item

            **Stackable**: Yes
            """;

        var exception = Assert.Throws<ItemTypeParseException>(() =>
            ItemTypeLoader.ParseMarkdown("no_category.md", markdown));

        Assert.Equal("no_category.md", exception.Filename);
        Assert.Contains("category", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ParseMarkdown_MissingStackable_ThrowsItemTypeParseException()
    {
        var markdown = """
            # Mysterious Item

            **Category**: Material
            """;

        var exception = Assert.Throws<ItemTypeParseException>(() =>
            ItemTypeLoader.ParseMarkdown("no_stackable.md", markdown));

        Assert.Equal("no_stackable.md", exception.Filename);
        Assert.Contains("stackable", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ParseMarkdown_InvalidCategory_ThrowsItemTypeParseException()
    {
        var markdown = """
            # Bad Item

            **Category**: InvalidCategory
            **Stackable**: Yes
            """;

        var exception = Assert.Throws<ItemTypeParseException>(() =>
            ItemTypeLoader.ParseMarkdown("bad_category.md", markdown));

        Assert.Equal("bad_category.md", exception.Filename);
        Assert.Contains("category", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(Category.Material, "Material")]
    [InlineData(Category.Weapon, "Weapon")]
    [InlineData(Category.Consumable, "Consumable")]
    [InlineData(Category.Medicine, "Medicine")]
    [InlineData(Category.Tool, "Tool")]
    [InlineData(Category.Misc, "Misc")]
    public void ParseMarkdown_AllCategoryTypes_ParseCorrectly(Category expectedCategory, string categoryString)
    {
        var markdown = $"""
            # Test Item

            **Category**: {categoryString}
            **Stackable**: Yes
            """;

        var result = ItemTypeLoader.ParseMarkdown("test.md", markdown);

        Assert.Equal(expectedCategory, result.Category);
    }

    [Fact]
    public void ParseMarkdown_ExceptionIncludesFilename()
    {
        var markdown = """
            **Category**: Material
            **Stackable**: Yes
            """;

        var exception = Assert.Throws<ItemTypeParseException>(() =>
            ItemTypeLoader.ParseMarkdown("specific_file.md", markdown));

        Assert.Equal("specific_file.md", exception.Filename);
    }

    [Fact]
    public void LoadFromDirectory_MultipleFiles_ReturnsAllItemTypes()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);

        try
        {
            File.WriteAllText(Path.Combine(tempDir, "iron_ore.md"), """
                # Iron Ore

                **Category**: Material
                **Stackable**: Yes
                """);

            File.WriteAllText(Path.Combine(tempDir, "sword.md"), """
                # Steel Sword

                **Category**: Weapon
                **Stackable**: No
                """);

            File.WriteAllText(Path.Combine(tempDir, "potion.md"), """
                # Health Potion

                **Category**: Medicine
                **Stackable**: Yes
                **Max Stack Size**: 10
                """);

            var results = ItemTypeLoader.LoadFromDirectory(tempDir);

            Assert.Equal(3, results.Length);
            Assert.Contains(results, item => item.Name == "Iron Ore" && item.Category == Category.Material);
            Assert.Contains(results, item => item.Name == "Steel Sword" && item.Category == Category.Weapon);
            Assert.Contains(results, item => item.Name == "Health Potion" && item.Category == Category.Medicine);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void LoadFromDirectory_EmptyDirectory_ReturnsEmptyArray()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);

        try
        {
            var results = ItemTypeLoader.LoadFromDirectory(tempDir);

            Assert.Empty(results);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }
}
