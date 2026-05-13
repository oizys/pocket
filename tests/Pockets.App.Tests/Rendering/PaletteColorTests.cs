using Pockets.App.Rendering;

namespace Pockets.App.Tests.Rendering;

public class PaletteColorTests
{
    [Fact]
    public void Lerp_AtZero_ReturnsStart()
    {
        var a = new PaletteColor(10, 20, 30);
        var b = new PaletteColor(200, 100, 50);
        Assert.Equal(a, PaletteColor.Lerp(a, b, 0f));
    }

    [Fact]
    public void Lerp_AtOne_ReturnsEnd()
    {
        var a = new PaletteColor(10, 20, 30);
        var b = new PaletteColor(200, 100, 50);
        Assert.Equal(b, PaletteColor.Lerp(a, b, 1f));
    }

    [Fact]
    public void Lerp_AtHalf_IsMidpoint()
    {
        var a = new PaletteColor(0, 0, 0);
        var b = new PaletteColor(100, 200, 50);
        var mid = PaletteColor.Lerp(a, b, 0.5f);
        Assert.Equal((byte)50, mid.R);
        Assert.Equal((byte)100, mid.G);
        Assert.Equal((byte)25, mid.B);
    }

    [Fact]
    public void Lerp_ClampsOutsideZeroToOne()
    {
        var a = new PaletteColor(10, 10, 10);
        var b = new PaletteColor(200, 200, 200);
        Assert.Equal(a, PaletteColor.Lerp(a, b, -1f));
        Assert.Equal(b, PaletteColor.Lerp(a, b, 5f));
    }

    [Fact]
    public void Lerp_AcrossManySteps_StaysMonotonic()
    {
        var a = new PaletteColor(0, 0, 0);
        var b = new PaletteColor(255, 255, 255);
        byte prev = 0;
        for (int i = 0; i <= 10; i++)
        {
            var t = i / 10f;
            var c = PaletteColor.Lerp(a, b, t);
            Assert.True(c.R >= prev, $"R should be non-decreasing at t={t}: prev={prev}, got {c.R}");
            Assert.Equal(c.R, c.G);
            Assert.Equal(c.R, c.B);
            prev = c.R;
        }
    }
}
