namespace Pockets.Core.Tests.Models;

using Pockets.Core.Models;
using Xunit;

public class ItemStackTests
{
    private static readonly ItemType Ore = new("Iron Ore", Category.Material, IsStackable: true, MaxStackSize: 20);
    private static readonly ItemType Gem = new("Ruby Gem", Category.Material, IsStackable: true, MaxStackSize: 20);
    private static readonly ItemType Sword = new("Magic Sword", Category.Weapon, IsStackable: false);

    // TryMerge tests

    [Fact]
    public void TryMerge_SameTypeFitsEntirely_MergesWithoutRemainder()
    {
        var stack1 = new ItemStack(Ore, 10);
        var stack2 = new ItemStack(Ore, 5);

        var (merged, remainder) = stack1.TryMerge(stack2);

        Assert.Equal(Ore, merged.ItemType);
        Assert.Equal(15, merged.Count);
        Assert.Null(remainder);
    }

    [Fact]
    public void TryMerge_SameTypeWithOverflow_ReturnsRemainder()
    {
        var stack1 = new ItemStack(Ore, 15);
        var stack2 = new ItemStack(Ore, 10);

        var (merged, remainder) = stack1.TryMerge(stack2);

        Assert.Equal(Ore, merged.ItemType);
        Assert.Equal(20, merged.Count);
        Assert.NotNull(remainder);
        Assert.Equal(Ore, remainder.ItemType);
        Assert.Equal(5, remainder.Count);
    }

    [Fact]
    public void TryMerge_SameTypeBothAtOne_MergesToTwo()
    {
        var stack1 = new ItemStack(Ore, 1);
        var stack2 = new ItemStack(Ore, 1);

        var (merged, remainder) = stack1.TryMerge(stack2);

        Assert.Equal(Ore, merged.ItemType);
        Assert.Equal(2, merged.Count);
        Assert.Null(remainder);
    }

    [Fact]
    public void TryMerge_DifferentType_ReturnsUnchanged()
    {
        var stack1 = new ItemStack(Ore, 10);
        var stack2 = new ItemStack(Gem, 5);

        var (merged, remainder) = stack1.TryMerge(stack2);

        Assert.Equal(stack1, merged);
        Assert.NotNull(remainder);
        Assert.Equal(stack2, remainder);
    }

    [Fact]
    public void TryMerge_UniqueItems_CannotMerge()
    {
        var stack1 = new ItemStack(Sword, 1);
        var stack2 = new ItemStack(Sword, 1);

        var (merged, remainder) = stack1.TryMerge(stack2);

        Assert.Equal(Sword, merged.ItemType);
        Assert.Equal(1, merged.Count);
        Assert.NotNull(remainder);
        Assert.Equal(Sword, remainder.ItemType);
        Assert.Equal(1, remainder.Count);
    }

    [Fact]
    public void TryMerge_SameTypeAlreadyAtMax_ReturnsFullRemainder()
    {
        var stack1 = new ItemStack(Ore, 20);
        var stack2 = new ItemStack(Ore, 10);

        var (merged, remainder) = stack1.TryMerge(stack2);

        Assert.Equal(Ore, merged.ItemType);
        Assert.Equal(20, merged.Count);
        Assert.NotNull(remainder);
        Assert.Equal(Ore, remainder.ItemType);
        Assert.Equal(10, remainder.Count);
    }

    // Split tests

    [Fact]
    public void Split_DefaultSplitEvenCount_SplitsInHalf()
    {
        var stack = new ItemStack(Ore, 10);

        var result = stack.Split();

        Assert.NotNull(result);
        var (left, right) = result.Value;
        Assert.Equal(Ore, left.ItemType);
        Assert.Equal(5, left.Count);
        Assert.NotNull(right);
        Assert.Equal(Ore, right.ItemType);
        Assert.Equal(5, right.Count);
    }

    [Fact]
    public void Split_DefaultSplitOddCount_UseCeilingDivision()
    {
        var stack = new ItemStack(Ore, 31);

        var result = stack.Split();

        Assert.NotNull(result);
        var (left, right) = result.Value;
        Assert.Equal(Ore, left.ItemType);
        Assert.Equal(16, left.Count);
        Assert.NotNull(right);
        Assert.Equal(Ore, right.ItemType);
        Assert.Equal(15, right.Count);
    }

    [Fact]
    public void Split_ExplicitLeftCount_SplitsAsSpecified()
    {
        var stack = new ItemStack(Ore, 10);

        var result = stack.Split(3);

        Assert.NotNull(result);
        var (left, right) = result.Value;
        Assert.Equal(Ore, left.ItemType);
        Assert.Equal(3, left.Count);
        Assert.NotNull(right);
        Assert.Equal(Ore, right.ItemType);
        Assert.Equal(7, right.Count);
    }

    [Fact]
    public void Split_CountIsOne_ReturnsNull()
    {
        var stack = new ItemStack(Ore, 1);

        var result = stack.Split();

        Assert.Null(result);
    }

    [Fact]
    public void Split_LeftCountIsZero_ReturnsNull()
    {
        var stack = new ItemStack(Ore, 10);

        var result = stack.Split(0);

        Assert.Null(result);
    }

    [Fact]
    public void Split_LeftCountEqualsCount_ReturnsNull()
    {
        var stack = new ItemStack(Ore, 10);

        var result = stack.Split(10);

        Assert.Null(result);
    }

    [Fact]
    public void Split_CountOfTwo_SplitsToOneAndOne()
    {
        var stack = new ItemStack(Ore, 2);

        var result = stack.Split();

        Assert.NotNull(result);
        var (left, right) = result.Value;
        Assert.Equal(Ore, left.ItemType);
        Assert.Equal(1, left.Count);
        Assert.NotNull(right);
        Assert.Equal(Ore, right.ItemType);
        Assert.Equal(1, right.Count);
    }

    [Fact]
    public void Split_UniqueItem_CannotSplit()
    {
        var stack = new ItemStack(Sword, 1);

        var result = stack.Split();

        Assert.Null(result);
    }
}
