using System.Collections.Immutable;
using Pockets.Core.Models;

namespace Pockets.Core.Tests.Models;

public class GameStateTests
{
    private static readonly ItemType Ore = new("Iron Ore", Category.Material, IsStackable: true, MaxStackSize: 20);
    private static readonly ItemType Gem = new("Ruby Gem", Category.Material, IsStackable: true, MaxStackSize: 20);
    private static readonly ItemType Sword = new("Magic Sword", Category.Weapon, IsStackable: false);

    private static readonly ImmutableArray<ItemType> SampleTypes =
        ImmutableArray.Create(Ore, Gem, Sword);

    [Fact]
    public void CreateStage1_HasEightByFourGrid()
    {
        var state = GameState.CreateStage1(SampleTypes, Array.Empty<ItemStack>());
        Assert.Equal(8, state.RootBag.Grid.Columns);
        Assert.Equal(4, state.RootBag.Grid.Rows);
    }

    [Fact]
    public void CreateStage1_CursorAtOrigin()
    {
        var state = GameState.CreateStage1(SampleTypes, Array.Empty<ItemStack>());
        Assert.Equal(new Position(0, 0), state.Cursor.Position);
    }

    [Fact]
    public void CreateStage1_StoresItemTypes()
    {
        var state = GameState.CreateStage1(SampleTypes, Array.Empty<ItemStack>());
        Assert.Equal(3, state.ItemTypes.Length);
    }

    [Fact]
    public void CreateStage1_AcquiresInitialStacks()
    {
        var stacks = new[] { new ItemStack(Ore, 10), new ItemStack(Gem, 5) };
        var state = GameState.CreateStage1(SampleTypes, stacks);

        var cell0 = state.RootBag.Grid.GetCell(0);
        Assert.NotNull(cell0.Stack);
        Assert.Equal(Ore, cell0.Stack.ItemType);
        Assert.Equal(10, cell0.Stack.Count);

        var cell1 = state.RootBag.Grid.GetCell(1);
        Assert.NotNull(cell1.Stack);
        Assert.Equal(Gem, cell1.Stack.ItemType);
        Assert.Equal(5, cell1.Stack.Count);
    }

    [Fact]
    public void MoveCursor_Right_UpdatesPosition()
    {
        var state = GameState.CreateStage1(SampleTypes, Array.Empty<ItemStack>());
        var moved = state.MoveCursor(Direction.Right);
        Assert.Equal(new Position(0, 1), moved.Cursor.Position);
    }

    [Fact]
    public void MoveCursor_WrapsAtEdge()
    {
        var state = GameState.CreateStage1(SampleTypes, Array.Empty<ItemStack>());
        var moved = state.MoveCursor(Direction.Left);
        Assert.Equal(new Position(0, 7), moved.Cursor.Position);
    }

    [Fact]
    public void MoveCursor_ReturnsNewState_OriginalUnchanged()
    {
        var state = GameState.CreateStage1(SampleTypes, Array.Empty<ItemStack>());
        var moved = state.MoveCursor(Direction.Down);
        Assert.Equal(new Position(0, 0), state.Cursor.Position);
        Assert.Equal(new Position(1, 0), moved.Cursor.Position);
    }

    [Fact]
    public void CurrentCell_ReturnsEmptyForEmptyGrid()
    {
        var state = GameState.CreateStage1(SampleTypes, Array.Empty<ItemStack>());
        Assert.True(state.CurrentCell.IsEmpty);
    }

    [Fact]
    public void CurrentCell_ReturnsCellAtCursorPosition()
    {
        var stacks = new[] { new ItemStack(Ore, 10) };
        var state = GameState.CreateStage1(SampleTypes, stacks);

        Assert.NotNull(state.CurrentCell.Stack);
        Assert.Equal(Ore, state.CurrentCell.Stack.ItemType);

        var moved = state.MoveCursor(Direction.Right);
        Assert.True(moved.CurrentCell.IsEmpty);
    }
}
