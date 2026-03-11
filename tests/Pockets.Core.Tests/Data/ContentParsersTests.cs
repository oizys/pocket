using System.Collections.Immutable;
using Pockets.Core.Data;
using Pockets.Core.Models;

namespace Pockets.Core.Tests.Data;

public class ContentParsersTests
{
    // ==================== Item parsing ====================

    [Fact]
    public void ParseItem_BasicStackable()
    {
        var block = MakeBlock("Item", "Plain Rock",
            ("Category", "Material"), ("Stackable", "Yes"));

        var item = ContentParsers.ParseItem(block);

        Assert.Equal("Plain Rock", item.Name);
        Assert.Equal(Category.Material, item.Category);
        Assert.True(item.IsStackable);
        Assert.Equal(20, item.MaxStackSize);
    }

    [Fact]
    public void ParseItem_UniqueItem()
    {
        var block = MakeBlock("Item", "Stone Axe",
            ("Category", "Tool"), ("Stackable", "No"));

        var item = ContentParsers.ParseItem(block);

        Assert.Equal("Stone Axe", item.Name);
        Assert.Equal(Category.Tool, item.Category);
        Assert.False(item.IsStackable);
    }

    [Fact]
    public void ParseItem_WithMaxStackSize()
    {
        var block = MakeBlock("Item", "Arrow",
            ("Category", "Weapon"), ("Stackable", "Yes"), ("Max Stack Size", "50"));

        var item = ContentParsers.ParseItem(block);

        Assert.Equal(50, item.MaxStackSize);
    }

    [Fact]
    public void ParseItem_WithDescription()
    {
        var block = new ContentBlock("Item", "Plain Rock",
            ImmutableDictionary<string, string>.Empty
                .Add("Category", "Material")
                .Add("Stackable", "Yes"),
            "A common grey stone found throughout the world.",
            "test.md");

        var item = ContentParsers.ParseItem(block);

        Assert.Equal("A common grey stone found throughout the world.", item.Description);
    }

    // ==================== Recipe parsing ====================

    [Fact]
    public void ParseRecipe_BasicWithStaticOutput()
    {
        var block = MakeBlock("Recipe", "stone-axe",
            ("Name", "Stone Axe"),
            ("Duration", "3"),
            ("Grid", "3x1"),
            ("Input 1", "Plain Rock ×5"),
            ("Input 2", "Rough Wood ×3"),
            ("Output", "1 Stone Axe"));

        var recipe = ContentParsers.ParseRecipe(block);

        Assert.Equal("stone-axe", recipe.Id);
        Assert.Equal("Stone Axe", recipe.Name);
        Assert.Equal(3, recipe.Duration);
        Assert.Equal(3, recipe.GridColumns);
        Assert.Equal(1, recipe.GridRows);
        Assert.Equal(2, recipe.Inputs.Length);
        Assert.Equal("Plain Rock", recipe.Inputs[0].ItemName);
        Assert.Equal(5, recipe.Inputs[0].Count);
        Assert.Equal("Rough Wood", recipe.Inputs[1].ItemName);
        Assert.Equal(3, recipe.Inputs[1].Count);
        Assert.Single(recipe.OutputPipeline);
        Assert.IsType<StaticItemStep>(recipe.OutputPipeline[0]);
    }

    [Fact]
    public void ParseRecipe_WithPipelineOutput()
    {
        var block = MakeBlock("Recipe", "seedling-forest",
            ("Name", "Forest Bag"),
            ("Duration", "8"),
            ("Grid", "3x1"),
            ("Input 1", "Forest Seed ×5"),
            ("Input 2", "Rich Soil ×3"),
            ("Output", "1 Forest Bag -> !wilderness(@forest-6x4, @forest-materials)"));

        var recipe = ContentParsers.ParseRecipe(block);

        Assert.Equal(2, recipe.OutputPipeline.Count);
        Assert.IsType<StaticItemStep>(recipe.OutputPipeline[0]);
        var gen = Assert.IsType<GeneratorStep>(recipe.OutputPipeline[1]);
        Assert.Equal("wilderness", gen.GeneratorId);
        Assert.Equal(2, gen.TemplateArgs.Count);
    }

    [Fact]
    public void ParseRecipe_InputWithUnicodeMultiplySign()
    {
        var block = MakeBlock("Recipe", "test",
            ("Name", "Test"),
            ("Duration", "1"),
            ("Grid", "2x1"),
            ("Input 1", "Plain Rock ×5"),
            ("Output", "1 Stone Axe"));

        var recipe = ContentParsers.ParseRecipe(block);

        Assert.Equal("Plain Rock", recipe.Inputs[0].ItemName);
        Assert.Equal(5, recipe.Inputs[0].Count);
    }

    [Fact]
    public void ParseRecipe_InputWithLetterX()
    {
        var block = MakeBlock("Recipe", "test",
            ("Name", "Test"),
            ("Duration", "1"),
            ("Grid", "2x1"),
            ("Input 1", "Plain Rock x5"),
            ("Output", "1 Stone Axe"));

        var recipe = ContentParsers.ParseRecipe(block);

        Assert.Equal("Plain Rock", recipe.Inputs[0].ItemName);
        Assert.Equal(5, recipe.Inputs[0].Count);
    }

    // ==================== Facility parsing ====================

    [Fact]
    public void ParseFacility_Basic()
    {
        var block = MakeBlock("Facility", "Workbench",
            ("Environment", "Workbench"),
            ("ColorScheme", "Brown"),
            ("Recipes", "stone-axe, stone-bench"));

        var facility = ContentParsers.ParseFacility(block);

        Assert.Equal("Workbench", facility.Id);
        Assert.Equal("Workbench", facility.EnvironmentType);
        Assert.Equal("Brown", facility.ColorScheme);
        Assert.Equal(2, facility.RecipeIds.Length);
        Assert.Equal("stone-axe", facility.RecipeIds[0]);
        Assert.Equal("stone-bench", facility.RecipeIds[1]);
    }

    [Fact]
    public void ParseFacility_SingleRecipe()
    {
        var block = MakeBlock("Facility", "Tanner",
            ("Environment", "Tanner"),
            ("ColorScheme", "Brown"),
            ("Recipes", "tanner-pouch"));

        var facility = ContentParsers.ParseFacility(block);

        Assert.Single(facility.RecipeIds);
        Assert.Equal("tanner-pouch", facility.RecipeIds[0]);
    }

    // ==================== GridTemplate parsing ====================

    [Fact]
    public void ParseGridTemplate()
    {
        var block = MakeBlock("GridTemplate", "forest-6x4",
            ("Columns", "6"),
            ("Rows", "4"),
            ("Environment", "Forest"),
            ("ColorScheme", "Green"));

        var template = ContentParsers.ParseGridTemplate(block);

        Assert.Equal("forest-6x4", template.Id);
        Assert.Equal(6, template.Columns);
        Assert.Equal(4, template.Rows);
        Assert.Equal("Forest", template.EnvironmentType);
        Assert.Equal("Green", template.ColorScheme);
    }

    [Fact]
    public void ParseGridTemplate_DefaultEnvironmentAndColor()
    {
        var block = MakeBlock("GridTemplate", "small-bag",
            ("Columns", "3"),
            ("Rows", "2"));

        var template = ContentParsers.ParseGridTemplate(block);

        Assert.Equal("Default", template.EnvironmentType);
        Assert.Equal("Default", template.ColorScheme);
    }

    // ==================== LootTableTemplate parsing ====================

    [Fact]
    public void ParseLootTableTemplate()
    {
        var block = MakeBlock("LootTableTemplate", "forest-materials",
            ("Items", "Plain Rock ×2.0, Rough Wood ×3.0, Forest Seed ×0.5"),
            ("FillRatio", "0.6"));

        var template = ContentParsers.ParseLootTableTemplate(block);

        Assert.Equal("forest-materials", template.Id);
        Assert.Equal(0.6, template.FillRatio);
        Assert.Equal(3, template.Entries.Length);
        Assert.Equal("Plain Rock", template.Entries[0].ItemName);
        Assert.Equal(2.0, template.Entries[0].Weight);
        Assert.Equal("Rough Wood", template.Entries[1].ItemName);
        Assert.Equal(3.0, template.Entries[1].Weight);
        Assert.Equal("Forest Seed", template.Entries[2].ItemName);
        Assert.Equal(0.5, template.Entries[2].Weight);
    }

    [Fact]
    public void ParseLootTableTemplate_DefaultFillRatio()
    {
        var block = MakeBlock("LootTableTemplate", "cave-materials",
            ("Items", "Iron Ore ×1.0"));

        var template = ContentParsers.ParseLootTableTemplate(block);

        Assert.Equal(0.5, template.FillRatio);
        Assert.Single(template.Entries);
    }

    // ==================== Helpers ====================

    private static ContentBlock MakeBlock(string type, string id, params (string Key, string Value)[] fields)
    {
        var dict = fields.ToImmutableDictionary(f => f.Key, f => f.Value);
        return new ContentBlock(type, id, dict, "", "test.md");
    }
}
