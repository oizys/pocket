using System.Collections.Immutable;
using Pockets.Core.Models;
using Pockets.Core.Rendering;

namespace Pockets.Core.Tests.Rendering;

public class CompactGridRendererTests
{
    private static readonly ItemType Ore = new("Iron Ore", Category.Material, IsStackable: true);
    private static readonly ItemType Sword = new("Magic Sword", Category.Weapon, IsStackable: false);
    private static readonly ImmutableArray<ItemType> Types = ImmutableArray.Create(Ore, Sword);

    private readonly CompactGridRenderer _renderer = new();

    [Fact]
    public void Render_EmptyGrid_HasCorrectStructure()
    {
        var state = GameState.CreateStage1(Types, Array.Empty<ItemStack>());
        var output = _renderer.Render(state);

        Assert.Contains("┌", output);
        Assert.Contains("└", output);
        Assert.Contains("│", output);
    }

    [Fact]
    public void Render_SingleItem_ShowsAbbreviation()
    {
        var state = GameState.CreateStage1(Types, new[] { new ItemStack(Ore, 10) });
        var output = _renderer.Render(state);

        Assert.Contains("IO×10", output);
    }

    [Fact]
    public void Render_CursorMarkerPresent()
    {
        var state = GameState.CreateStage1(Types, new[] { new ItemStack(Ore, 10) });
        var output = _renderer.Render(state);

        Assert.Contains(">", output);
    }

    [Fact]
    public void Render_AllRowsSameLength()
    {
        var state = GameState.CreateStage1(Types, new[] { new ItemStack(Ore, 10), new ItemStack(Sword, 1) });
        var output = _renderer.Render(state);
        var lines = output.Split('\n');

        // Grid lines (borders + row content) should all be same length
        var gridLines = lines.Take(9).Select(l => l.TrimEnd('\r')).ToList(); // 4 rows + 5 borders
        var lengths = gridLines.Select(l => l.Length).Distinct().ToList();
        Assert.Single(lengths);
    }

    [Fact]
    public void Render_IncludesCursorDescription()
    {
        var state = GameState.CreateStage1(Types, new[] { new ItemStack(Ore, 10) });
        var output = _renderer.Render(state);

        Assert.Contains("Iron Ore", output);
        Assert.Contains("Material", output);
    }

    [Fact]
    public void Render_IncludesHandSummary()
    {
        var state = GameState.CreateStage1(Types, Array.Empty<ItemStack>());
        var output = _renderer.Render(state);

        Assert.Contains("Hand: empty", output);
    }
}
