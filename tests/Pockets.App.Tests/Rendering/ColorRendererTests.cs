using Terminal.Gui;
using Pockets.App.Rendering;

namespace Pockets.App.Tests.Rendering;

public class ColorRendererTests
{
    [Fact]
    public void ToAnsi16_PureRed_MapsToRed()
    {
        Assert.Equal(Color.Red, ColorRenderer.ToAnsi16(new PaletteColor(170, 0, 0)));
    }

    [Fact]
    public void ToAnsi16_BrightRed_MapsToBrightRed()
    {
        Assert.Equal(Color.BrightRed, ColorRenderer.ToAnsi16(new PaletteColor(255, 100, 100)));
    }

    [Fact]
    public void ToAnsi16_Black_MapsToBlack()
    {
        Assert.Equal(Color.Black, ColorRenderer.ToAnsi16(new PaletteColor(0, 0, 0)));
    }

    [Fact]
    public void ToAnsi16_White_MapsToWhite()
    {
        Assert.Equal(Color.White, ColorRenderer.ToAnsi16(new PaletteColor(240, 240, 240)));
    }

    [Fact]
    public void ToAnsi16_DarkGrayRgb_MapsToDarkGray()
    {
        Assert.Equal(Color.DarkGray, ColorRenderer.ToAnsi16(new PaletteColor(85, 85, 85)));
        Assert.Equal(Color.DarkGray, ColorRenderer.ToAnsi16(new PaletteColor(96, 96, 96)));
    }

    [Fact]
    public void ToAnsi16_ToolPalette_MapsToBlueFamily()
    {
        var result = ColorRenderer.ToAnsi16(new PaletteColor(40, 60, 200));
        Assert.True(result is Color.Blue or Color.BrightBlue,
            $"expected Blue or BrightBlue, got {result}");
    }

    [Fact]
    public void ToAnsi16_AllCategoryPalettes_QuantizeToExpectedHueFamily()
    {
        // Lock in the palette ↔ ANSI-family mapping so further palette tweaks
        // can't silently regress (e.g. Medicine drifting back into DarkGray).
        Assert.Equal(Color.Red,        ColorRenderer.ToAnsi16(Palette.CategoryBackground(Pockets.Core.Models.Category.Weapon)));
        Assert.Equal(Color.Brown,      ColorRenderer.ToAnsi16(Palette.CategoryBackground(Pockets.Core.Models.Category.Structure)));
        Assert.Equal(Color.Green,      ColorRenderer.ToAnsi16(Palette.CategoryBackground(Pockets.Core.Models.Category.Medicine)));
        Assert.Equal(Color.Magenta,    ColorRenderer.ToAnsi16(Palette.CategoryBackground(Pockets.Core.Models.Category.Bag)));
        Assert.Equal(Color.Cyan,       ColorRenderer.ToAnsi16(Palette.CategoryBackground(Pockets.Core.Models.Category.Consumable)));
        // Tool may land on Blue or BrightBlue depending on tuning; assert family.
        var tool = ColorRenderer.ToAnsi16(Palette.CategoryBackground(Pockets.Core.Models.Category.Tool));
        Assert.True(tool is Color.Blue or Color.BrightBlue);
        // Material/Misc are intentionally dim gray.
        Assert.Equal(Color.DarkGray,   ColorRenderer.ToAnsi16(Palette.CategoryBackground(Pockets.Core.Models.Category.Material)));
        Assert.Equal(Color.DarkGray,   ColorRenderer.ToAnsi16(Palette.CategoryBackground(Pockets.Core.Models.Category.Misc)));
    }
}
