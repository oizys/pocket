using System.Collections.Immutable;
using Pockets.Core.Dsl;
using Pockets.Core.Models;

namespace Pockets.Core.Tests.Dsl;

/// <summary>
/// Tests that the DSL cond-table implementations of primary and secondary
/// match the behavior of the direct GameState.ToolPrimary/ToolSecondary methods.
/// </summary>
public class PrimarySecondaryDslTests
{
    private static readonly ItemType Rock = new("Rock", Category.Material, IsStackable: true, MaxStackSize: 20);
    private static readonly ItemType Sword = new("Sword", Category.Weapon, IsStackable: false);
    private static readonly ItemType BagType = new("Pouch", Category.Bag, IsStackable: false);
    private static readonly ImmutableArray<ItemType> Types = ImmutableArray.Create(Rock, Sword, BagType);

    private static GameState MakeState(params (int index, ItemStack stack)[] items)
    {
        var grid = Grid.Create(4, 2);
        foreach (var (index, stack) in items)
            grid = grid.SetCell(index, new Cell(stack));
        var rootBag = new Bag(grid);
        var handBag = GameState.CreateHandBag();
        var store = BagStore.Empty.Add(rootBag).Add(handBag);
        return new GameState(store, LocationMap.Create(handBag.Id, rootBag.Id), Types);
    }

    private static GameState WithHand(GameState state, ItemStack item)
    {
        var handBag = GameState.CreateHandBag();
        var (filledHand, _) = handBag.AcquireItems(new[] { item });
        return state with { Store = state.Store.Set(state.HandBagId, filledHand) };
    }

    private static OpResult RunPrimary(GameState state) =>
        DslInterpreter.RunProgram(state, StandardMacros.PrimaryDsl);

    private static OpResult RunSecondary(GameState state) =>
        DslInterpreter.RunProgram(state, StandardMacros.SecondaryDsl);

    // ==================== Primary: matches ToolPrimary ====================

    [Fact]
    public void Primary_EmptyCell_EmptyHand_NoOp()
    {
        var state = MakeState();
        var dsl = RunPrimary(state);
        var direct = state.ToolPrimary();

        Assert.Equal(direct.State.CurrentCell.IsEmpty, dsl.State.CurrentCell.IsEmpty);
        Assert.Equal(direct.State.HasItemsInHand, dsl.State.HasItemsInHand);
    }

    [Fact]
    public void Primary_OccupiedCell_EmptyHand_Grabs()
    {
        var state = MakeState((0, new ItemStack(Rock, 5)));
        var dsl = RunPrimary(state);
        var direct = state.ToolPrimary();

        Assert.True(dsl.State.HasItemsInHand);
        Assert.True(dsl.State.CurrentCell.IsEmpty);
        Assert.Equal(direct.State.HasItemsInHand, dsl.State.HasItemsInHand);
    }

    [Fact]
    public void Primary_EmptyCell_FullHand_Drops()
    {
        var state = WithHand(MakeState(), new ItemStack(Rock, 5));
        // Move to an empty cell
        state = state.MoveCursor(Direction.Right);
        var dsl = RunPrimary(state);
        var direct = state.ToolPrimary();

        Assert.False(dsl.State.HasItemsInHand);
        Assert.False(direct.State.HasItemsInHand);
    }

    [Fact]
    public void Primary_SameType_FullHand_Merges()
    {
        var state = MakeState((0, new ItemStack(Rock, 3)));
        state = WithHand(state, new ItemStack(Rock, 5));
        var dsl = RunPrimary(state);
        var direct = state.ToolPrimary();

        // Both should merge: cell should have 8, hand empty
        Assert.Equal(direct.State.CurrentCell.Stack!.Count, dsl.State.CurrentCell.Stack!.Count);
        Assert.Equal(direct.State.HasItemsInHand, dsl.State.HasItemsInHand);
    }

    [Fact]
    public void Primary_DifferentType_FullHand_Swaps()
    {
        var state = MakeState((0, new ItemStack(Rock, 3)));
        state = WithHand(state, new ItemStack(Sword, 1));
        var dsl = RunPrimary(state);
        var direct = state.ToolPrimary();

        Assert.Equal(direct.State.CurrentCell.Stack!.ItemType.Name, dsl.State.CurrentCell.Stack!.ItemType.Name);
        Assert.Equal(direct.State.HandItems[0].ItemType.Name, dsl.State.HandItems[0].ItemType.Name);
    }

    [Fact]
    public void Primary_CellHasBag_Enters()
    {
        var innerBag = new Bag(Grid.Create(3, 3), "Forest");
        var state = MakeState((0, new ItemStack(BagType, 1, ContainedBagId: innerBag.Id)));
        state = state with { Store = state.Store.Add(innerBag) };

        var dsl = RunPrimary(state);
        var direct = state.ToolPrimary();

        Assert.Equal(direct.State.IsNested, dsl.State.IsNested);
        Assert.True(dsl.State.IsNested);
    }

    // ==================== Secondary: matches ToolSecondary ====================

    [Fact]
    public void Secondary_EmptyCell_EmptyHand_NoOp()
    {
        var state = MakeState();
        var dsl = RunSecondary(state);
        var direct = state.ToolSecondary();

        Assert.Equal(direct.State.HasItemsInHand, dsl.State.HasItemsInHand);
    }

    [Fact]
    public void Secondary_OccupiedCell_EmptyHand_GrabsHalf()
    {
        var state = MakeState((0, new ItemStack(Rock, 10)));
        var dsl = RunSecondary(state);
        var direct = state.ToolSecondary();

        Assert.Equal(direct.State.HasItemsInHand, dsl.State.HasItemsInHand);
        Assert.Equal(direct.State.HandItems[0].Count, dsl.State.HandItems[0].Count);
        Assert.Equal(direct.State.CurrentCell.Stack!.Count, dsl.State.CurrentCell.Stack!.Count);
    }

    [Fact]
    public void Secondary_SingleItem_EmptyHand_NoOp()
    {
        var state = MakeState((0, new ItemStack(Rock, 1)));
        var dsl = RunSecondary(state);
        var direct = state.ToolSecondary();

        // count <= 1, so no-op
        Assert.Equal(direct.State.HasItemsInHand, dsl.State.HasItemsInHand);
        Assert.False(dsl.State.HasItemsInHand);
    }

    [Fact]
    public void Secondary_EmptyCell_FullHand_DropsOne()
    {
        var state = WithHand(MakeState(), new ItemStack(Rock, 5));
        state = state.MoveCursor(Direction.Right);
        var dsl = RunSecondary(state);
        var direct = state.ToolSecondary();

        Assert.Equal(direct.State.CurrentCell.Stack!.Count, dsl.State.CurrentCell.Stack!.Count);
        Assert.Equal(1, dsl.State.CurrentCell.Stack!.Count);
        Assert.Equal(direct.State.HandItems[0].Count, dsl.State.HandItems[0].Count);
    }

    [Fact]
    public void Secondary_SameType_FullHand_DropsOne()
    {
        var state = MakeState((0, new ItemStack(Rock, 3)));
        state = WithHand(state, new ItemStack(Rock, 5));
        var dsl = RunSecondary(state);
        var direct = state.ToolSecondary();

        Assert.Equal(direct.State.CurrentCell.Stack!.Count, dsl.State.CurrentCell.Stack!.Count);
        Assert.Equal(4, dsl.State.CurrentCell.Stack!.Count); // 3 + 1
    }

    [Fact]
    public void Secondary_DifferentType_FullHand_NoOp()
    {
        var state = MakeState((0, new ItemStack(Rock, 3)));
        state = WithHand(state, new ItemStack(Sword, 1));
        var dsl = RunSecondary(state);
        var direct = state.ToolSecondary();

        // Different type → no-op
        Assert.Equal(direct.State.CurrentCell.Stack!.Count, dsl.State.CurrentCell.Stack!.Count);
        Assert.Equal(3, dsl.State.CurrentCell.Stack!.Count);
    }
}
