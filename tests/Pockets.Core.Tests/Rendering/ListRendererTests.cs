using System.Collections.Immutable;
using Pockets.Core.Models;
using Pockets.Core.Rendering;

namespace Pockets.Core.Tests.Rendering;

public class ListRendererTests
{
    private static readonly ItemType Ore = new("Iron Ore", Category.Material, IsStackable: true);
    private static readonly ItemType Sword = new("Magic Sword", Category.Weapon, IsStackable: false);
    private static readonly ImmutableArray<ItemType> Types = ImmutableArray.Create(Ore, Sword);

    private readonly ListRenderer _renderer = new();

    [Fact]
    public void Render_EmptyGrid_ShowsNoItems()
    {
        var state = GameState.CreateStage1(Types, Array.Empty<ItemStack>());
        var output = _renderer.Render(state);

        Assert.Contains("(no items)", output);
    }

    [Fact]
    public void Render_OnlyListsOccupiedCells()
    {
        var state = GameState.CreateStage1(Types, new[] { new ItemStack(Ore, 5), new ItemStack(Sword, 1) });
        var output = _renderer.Render(state);
        var itemLines = output.Split('\n').Where(l => l.TrimStart().StartsWith("(")).ToList();

        Assert.Equal(2, itemLines.Count);
    }

    [Fact]
    public void Render_HeaderShowsGridDimensions()
    {
        var state = GameState.CreateStage1(Types, Array.Empty<ItemStack>());
        var output = _renderer.Render(state);

        Assert.Contains("Grid: 8×4", output);
    }

    [Fact]
    public void Render_CursorCellMarked()
    {
        var state = GameState.CreateStage1(Types, new[] { new ItemStack(Ore, 5) });
        var output = _renderer.Render(state);

        Assert.Contains(">", output);
    }

    [Fact]
    public void Render_HandCellMarked()
    {
        var state = GameState.CreateStage1(Types, new[] { new ItemStack(Ore, 5) });
        state = state.ToolGrab();
        state = state.MoveCursor(Direction.Right); // move cursor off hand cell
        var output = _renderer.Render(state);

        Assert.Contains("#", output);
        Assert.Contains("Iron Ore", output);
    }

    [Fact]
    public void Render_IncludesCursorItemDescription()
    {
        var state = GameState.CreateStage1(Types, new[] { new ItemStack(Ore, 10) });
        var output = _renderer.Render(state);

        Assert.Contains("Iron Ore", output);
        Assert.Contains("Stackable", output);
    }
}
