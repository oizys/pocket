using System.Collections.Immutable;
using Pockets.Core.Models;
using Pockets.Core.Rendering;

namespace Pockets.Core.Tests.Rendering;

public class RenderHelpersTests
{
    private static readonly ItemType Ore = new("Iron Ore", Category.Material, IsStackable: true);
    private static readonly ItemType Sword = new("Magic Sword", Category.Weapon, IsStackable: false);
    private static readonly ItemType Gem = new("Amber", Category.Material, IsStackable: true);

    [Theory]
    [InlineData("Iron Ore", "IO")]
    [InlineData("Healing Potion Pack", "HPP")]
    [InlineData("A", "A")]
    public void AbbreviateName_MultiWord_ReturnsInitials(string name, string expected) =>
        Assert.Equal(expected, RenderHelpers.AbbreviateName(name));

    [Theory]
    [InlineData("Amber", "AMBER")]
    [InlineData("Gem", "GEM")]
    [InlineData("Ammunition", "AMMUN")]
    public void AbbreviateName_SingleWord(string name, string expected) =>
        Assert.Equal(expected, RenderHelpers.AbbreviateName(name));

    [Fact]
    public void FormatStack_Stackable_ShowsSymbolAbbrCount()
    {
        var stack = new ItemStack(Ore, 10);
        Assert.Equal("mIO×10", RenderHelpers.FormatStack(stack));
    }

    [Fact]
    public void FormatStack_Unique_NoCount()
    {
        var stack = new ItemStack(Sword, 1);
        Assert.Equal("wMS", RenderHelpers.FormatStack(stack));
    }

    [Fact]
    public void FormatStack_Null_ReturnsEmpty() =>
        Assert.Equal("", RenderHelpers.FormatStack(null));

    [Fact]
    public void CategorySymbol_AllCategoriesMapped()
    {
        foreach (var cat in Enum.GetValues<Category>())
        {
            var sym = RenderHelpers.CategorySymbol(cat);
            Assert.NotEqual('\0', sym);
        }
    }

    [Fact]
    public void FormatCell_CursorMarker()
    {
        var stack = new ItemStack(Ore, 5);
        var result = RenderHelpers.FormatCell(stack, isCursor: true, isHand: false);
        Assert.StartsWith(">", result);
        Assert.Contains("IO×5", result);
    }

    [Fact]
    public void FormatCell_HandMarker()
    {
        var stack = new ItemStack(Ore, 5);
        var result = RenderHelpers.FormatCell(stack, isCursor: false, isHand: true);
        Assert.StartsWith("#", result);
    }

    [Fact]
    public void FormatCell_Empty_NoMarkers()
    {
        var result = RenderHelpers.FormatCell(null, isCursor: false, isHand: false);
        Assert.Equal("", result);
    }

    [Fact]
    public void DescribeCursorItem_EmptyCell()
    {
        var state = GameState.CreateStage1(ImmutableArray<ItemType>.Empty, Array.Empty<ItemStack>());
        Assert.Equal("(empty)", RenderHelpers.DescribeCursorItem(state));
    }

    [Fact]
    public void DescribeCursorItem_WithStack_IncludesDetails()
    {
        var types = ImmutableArray.Create(Ore);
        var state = GameState.CreateStage1(types, new[] { new ItemStack(Ore, 10) });
        var desc = RenderHelpers.DescribeCursorItem(state);
        Assert.Contains("Iron Ore", desc);
        Assert.Contains("Material", desc);
        Assert.Contains("10/20", desc);
    }

    [Fact]
    public void FormatHandSummary_EmptyHand()
    {
        var state = GameState.CreateStage1(ImmutableArray<ItemType>.Empty, Array.Empty<ItemStack>());
        Assert.Equal("Hand: empty", RenderHelpers.FormatHandSummary(state));
    }

    [Fact]
    public void FormatHandSummary_WithHand()
    {
        var types = ImmutableArray.Create(Ore);
        var state = GameState.CreateStage1(types, new[] { new ItemStack(Ore, 5) });
        state = state.ToolGrab(); // grabs cursor cell (0,0)
        var summary = RenderHelpers.FormatHandSummary(state);
        Assert.Contains("Hand:", summary);
        Assert.Contains("IO", summary);
        Assert.Contains("(0,0)", summary);
    }
}
