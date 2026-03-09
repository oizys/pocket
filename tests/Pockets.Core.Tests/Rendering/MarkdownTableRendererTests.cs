using System.Collections.Immutable;
using Pockets.Core.Models;
using Pockets.Core.Rendering;

namespace Pockets.Core.Tests.Rendering;

public class MarkdownTableRendererTests
{
    private static readonly ItemType Ore = new("Iron Ore", Category.Material, IsStackable: true);
    private static readonly ItemType Sword = new("Magic Sword", Category.Weapon, IsStackable: false);
    private static readonly ImmutableArray<ItemType> Types = ImmutableArray.Create(Ore, Sword);

    private readonly MarkdownTableRenderer _renderer = new();

    [Fact]
    public void Render_EmptyGrid_HasHeaderAndRows()
    {
        var state = GameState.CreateStage1(Types, Array.Empty<ItemStack>());
        var output = _renderer.Render(state);

        // Should have pipe-separated header
        Assert.Contains("| 0 |", output);
        Assert.Contains("---|", output);
    }

    [Fact]
    public void Render_CursorCell_IsBold()
    {
        var state = GameState.CreateStage1(Types, new[] { new ItemStack(Ore, 5) });
        var output = _renderer.Render(state);

        Assert.Contains("**>", output);
    }

    [Fact]
    public void Render_IncludesHandSummary()
    {
        var state = GameState.CreateStage1(Types, Array.Empty<ItemStack>());
        var output = _renderer.Render(state);

        Assert.Contains("Hand: empty", output);
    }

    [Fact]
    public void Render_IncludesCursorDescription()
    {
        var state = GameState.CreateStage1(Types, new[] { new ItemStack(Ore, 10) });
        var output = _renderer.Render(state);

        Assert.Contains("Iron Ore", output);
    }
}
