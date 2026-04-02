namespace Pockets.Core.Tests.Models;

using System.Collections.Immutable;
using Pockets.Core.Models;
using Xunit;

public class PropertyValueTests
{
    private static readonly ItemType Sword = new("Iron Sword", Category.Weapon, IsStackable: false);
    private static readonly ItemType Ore = new("Iron Ore", Category.Material, IsStackable: true);

    // PropertyValue DU basics

    [Fact]
    public void IntValue_StoresAndPatternMatches()
    {
        PropertyValue val = new IntValue(42);
        Assert.True(val is IntValue(var v) && v == 42);
    }

    [Fact]
    public void StringValue_StoresAndPatternMatches()
    {
        PropertyValue val = new StringValue("Ol' Reliable");
        Assert.True(val is StringValue(var v) && v == "Ol' Reliable");
    }

    [Fact]
    public void PropertyValue_EqualityByValue()
    {
        Assert.Equal(new IntValue(10), new IntValue(10));
        Assert.NotEqual(new IntValue(10), new IntValue(20));
        Assert.Equal(new StringValue("a"), new StringValue("a"));
        Assert.NotEqual<PropertyValue>(new IntValue(1), new StringValue("1"));
    }

    // ItemStack.WithProperty — unique items

    [Fact]
    public void WithProperty_UniqueItem_AddsProperty()
    {
        var stack = new ItemStack(Sword, 1);

        var updated = stack.WithProperty("Durability", new IntValue(100));

        Assert.Equal(100, updated.GetInt("Durability"));
    }

    [Fact]
    public void WithProperty_UniqueItem_OverwritesExisting()
    {
        var stack = new ItemStack(Sword, 1)
            .WithProperty("Durability", new IntValue(100));

        var updated = stack.WithProperty("Durability", new IntValue(47));

        Assert.Equal(47, updated.GetInt("Durability"));
    }

    [Fact]
    public void WithProperty_UniqueItem_MultipleProperties()
    {
        var stack = new ItemStack(Sword, 1)
            .WithProperty("Durability", new IntValue(100))
            .WithProperty("CustomName", new StringValue("Stabby"));

        Assert.Equal(100, stack.GetInt("Durability"));
        Assert.Equal("Stabby", stack.GetString("CustomName"));
    }

    // Stackable items cannot have properties

    [Fact]
    public void WithProperty_StackableItem_ReturnsUnchanged()
    {
        var stack = new ItemStack(Ore, 10);

        var updated = stack.WithProperty("Quality", new IntValue(5));

        Assert.Null(updated.Properties);
        Assert.Same(stack, updated);
    }

    // WithoutProperty

    [Fact]
    public void WithoutProperty_RemovesExisting()
    {
        var stack = new ItemStack(Sword, 1)
            .WithProperty("Durability", new IntValue(100))
            .WithProperty("CustomName", new StringValue("Stabby"));

        var updated = stack.WithoutProperty("Durability");

        Assert.Null(updated.GetInt("Durability"));
        Assert.Equal("Stabby", updated.GetString("CustomName"));
    }

    [Fact]
    public void WithoutProperty_NonExistent_ReturnsUnchanged()
    {
        var stack = new ItemStack(Sword, 1)
            .WithProperty("Durability", new IntValue(100));

        var updated = stack.WithoutProperty("Missing");

        Assert.Equal(100, updated.GetInt("Durability"));
    }

    [Fact]
    public void WithoutProperty_NoProperties_ReturnsUnchanged()
    {
        var stack = new ItemStack(Sword, 1);

        var updated = stack.WithoutProperty("Missing");

        Assert.Same(stack, updated);
    }

    // GetInt / GetString edge cases

    [Fact]
    public void GetInt_WrongType_ReturnsNull()
    {
        var stack = new ItemStack(Sword, 1)
            .WithProperty("CustomName", new StringValue("Stabby"));

        Assert.Null(stack.GetInt("CustomName"));
    }

    [Fact]
    public void GetString_WrongType_ReturnsNull()
    {
        var stack = new ItemStack(Sword, 1)
            .WithProperty("Durability", new IntValue(100));

        Assert.Null(stack.GetString("Durability"));
    }

    [Fact]
    public void GetInt_NoProperties_ReturnsNull()
    {
        var stack = new ItemStack(Sword, 1);

        Assert.Null(stack.GetInt("Durability"));
    }

    // HasProperties

    [Fact]
    public void HasProperties_NullProperties_ReturnsFalse()
    {
        var stack = new ItemStack(Sword, 1);
        Assert.False(stack.HasProperties);
    }

    [Fact]
    public void HasProperties_WithProperties_ReturnsTrue()
    {
        var stack = new ItemStack(Sword, 1)
            .WithProperty("Durability", new IntValue(100));
        Assert.True(stack.HasProperties);
    }

    [Fact]
    public void HasProperties_EmptyDict_ReturnsFalse()
    {
        var stack = new ItemStack(Sword, 1, Properties: ImmutableDictionary<string, PropertyValue>.Empty);
        Assert.False(stack.HasProperties);
    }

    // Properties survive with-expressions

    [Fact]
    public void Properties_SurviveWithExpression()
    {
        var stack = new ItemStack(Sword, 1)
            .WithProperty("Durability", new IntValue(100));

        var copy = stack with { ContainedBagId = null };

        Assert.Equal(100, copy.GetInt("Durability"));
    }

    // Properties and constructor with explicit dict

    [Fact]
    public void Constructor_WithExplicitProperties()
    {
        var props = ImmutableDictionary<string, PropertyValue>.Empty
            .Add("Progress", new IntValue(3))
            .Add("Duration", new IntValue(6));

        var stack = new ItemStack(Sword, 1, Properties: props);

        Assert.Equal(3, stack.GetInt("Progress"));
        Assert.Equal(6, stack.GetInt("Duration"));
    }
}
