using System.Collections.Immutable;
using Pockets.Core.Models;
using Pockets.Core.Rendering;

namespace Pockets.Core.Tests.Rendering;

public class GlyphRendererTests
{
    private static readonly ItemType Rock =
        new("Rock", Category.Material, IsStackable: true, MaxStackSize: 99);
    private static readonly ItemType Sword =
        new("Sword", Category.Weapon, IsStackable: false);

    [Fact]
    public void Row1_Empty_Returns3Spaces()
    {
        Assert.Equal("   ", GlyphRenderer.Row1(new Cell()));
    }

    [Fact]
    public void Row1_UniqueItem_GlyphPlus2Spaces()
    {
        var cell = new Cell(new ItemStack(Sword, 1));
        Assert.Equal("S  ", GlyphRenderer.Row1(cell));
    }

    [Fact]
    public void Row1_StackableCountSingleDigit_GlyphSpaceDigit()
    {
        var cell = new Cell(new ItemStack(Rock, 5));
        Assert.Equal("R 5", GlyphRenderer.Row1(cell));
    }

    [Fact]
    public void Row1_StackableCountTwoDigit_GlyphPlusDigits()
    {
        var cell = new Cell(new ItemStack(Rock, 20));
        Assert.Equal("R20", GlyphRenderer.Row1(cell));
    }

    [Fact]
    public void Row1_StackableOverflow_ShowsGlyphAnd9Plus()
    {
        // Counts >= 100 collapse to "9+" to fit in 2 chars.
        var bigRock = Rock with { MaxStackSize = 200 };
        var cell = new Cell(new ItemStack(bigRock, 150));
        Assert.Equal("R9+", GlyphRenderer.Row1(cell));
    }

    [Fact]
    public void Row2_NoFrame_Returns3Spaces()
    {
        Assert.Equal("   ", GlyphRenderer.Row2(new Cell()));
    }

    [Fact]
    public void Row2_InputSlot_TriangleDown()
    {
        var cell = new Cell() with { Frame = new InputSlotFrame("in1") };
        Assert.Equal("▼▼▼", GlyphRenderer.Row2(cell));
    }

    [Fact]
    public void Row2_OutputSlot_TriangleUp()
    {
        var cell = new Cell() with { Frame = new OutputSlotFrame("out1") };
        Assert.Equal("▲▲▲", GlyphRenderer.Row2(cell));
    }

    [Fact]
    public void GlyphFor_FirstLetterUppercase()
    {
        Assert.Equal("R", GlyphRenderer.GlyphFor(Rock));
        var herb = new ItemType("herb", Category.Medicine, IsStackable: true, MaxStackSize: 9);
        Assert.Equal("H", GlyphRenderer.GlyphFor(herb));
    }

    [Fact]
    public void GlyphFor_NonLetterFirstChar_FallsBackToQuestionMark()
    {
        var weird = new ItemType("42-Magnum", Category.Weapon, IsStackable: false);
        Assert.Equal("?", GlyphRenderer.GlyphFor(weird));
    }

    [Fact]
    public void Constants_3x2()
    {
        Assert.Equal(3, GlyphRenderer.CellWidth);
        Assert.Equal(2, GlyphRenderer.CellHeight);
    }
}
