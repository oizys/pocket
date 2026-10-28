namespace Pockets.Realization.Tests;

/// <summary>Fixed-point easing: exact endpoints, clamping, monotonicity, pinned table.</summary>
public class EaseTests
{
    private static readonly int[] Dens = { 1, 7, 16, 125, 200, 1000 };

    /// <summary>0 maps to 0 and den maps to One for both kinds.</summary>
    [Theory]
    [InlineData(EaseKind.Linear)]
    [InlineData(EaseKind.Smooth)]
    public void Endpoints_AreExact(EaseKind kind)
    {
        foreach (var d in Dens)
        {
            Assert.Equal(0, Ease.Apply(kind, 0, d));
            Assert.Equal(Ease.One, Ease.Apply(kind, d, d));
        }
    }

    /// <summary>Progress outside 0…den clamps.</summary>
    [Theory]
    [InlineData(EaseKind.Linear)]
    [InlineData(EaseKind.Smooth)]
    public void OutOfRange_Clamps(EaseKind kind)
    {
        Assert.Equal(0, Ease.Apply(kind, -5, 10));
        Assert.Equal(Ease.One, Ease.Apply(kind, 15, 10));
    }

    /// <summary>A zero-length (or negative) track is already finished.</summary>
    [Theory]
    [InlineData(EaseKind.Linear)]
    [InlineData(EaseKind.Smooth)]
    public void NonPositiveDen_IsOne(EaseKind kind)
    {
        Assert.Equal(Ease.One, Ease.Apply(kind, 0, 0));
        Assert.Equal(Ease.One, Ease.Apply(kind, 3, -4));
    }

    /// <summary>Both curves never decrease as num grows.</summary>
    [Theory]
    [InlineData(EaseKind.Linear)]
    [InlineData(EaseKind.Smooth)]
    public void Monotone_NonDecreasing(EaseKind kind)
    {
        foreach (var d in Dens)
        {
            var prev = -1;
            for (var n = 0; n <= d; n++)
            {
                var v = Ease.Apply(kind, n, d);
                Assert.True(v >= prev, $"{kind} {n}/{d}: {v} < {prev}");
                prev = v;
            }
        }
    }

    /// <summary>The midpoint of an even-length track is exactly half.</summary>
    [Fact]
    public void Smooth_Midpoint_IsHalf()
    {
        foreach (var d in new[] { 2, 16, 64, 200, 1000 })
            Assert.Equal(Ease.One / 2, Ease.Smooth(d / 2, d));
    }

    /// <summary>The smoothstep table is pinned to its closed form and a few literals.</summary>
    [Fact]
    public void Smooth_Table_IsPinned()
    {
        for (var i = 0; i <= 64; i++)
            Assert.Equal((192 * i * i - 2 * i * i * i + 2) / 4, Ease.Smooth(i, 64));
        Assert.Equal(32768, Ease.Smooth(32, 64));
        Assert.Equal(8036, Ease.Smooth(14, 64));
        Assert.Equal(57500, Ease.Smooth(50, 64));
    }

    /// <summary>Linear is a floored ratio.</summary>
    [Fact]
    public void Linear_Ratio()
    {
        Assert.Equal(16384, Ease.Linear(1, 4));
        Assert.Equal(21845, Ease.Linear(1, 3));
    }

    /// <summary>Apply dispatches on the kind and rejects an undefined one.</summary>
    [Fact]
    public void Apply_Dispatches()
    {
        Assert.Equal(Ease.Linear(3, 10), Ease.Apply(EaseKind.Linear, 3, 10));
        Assert.Equal(Ease.Smooth(3, 10), Ease.Apply(EaseKind.Smooth, 3, 10));
        Assert.Throws<ArgumentOutOfRangeException>(() => Ease.Apply((EaseKind)0, 1, 2));
    }
}
