namespace Pockets.Realization;

/// <summary>
/// Fixed-point easing, integers only: every curve maps progress <c>num/den</c> to 0…<see cref="One"/>.
/// Endpoints are exact; progress outside 0…den is clamped; a non-positive <c>den</c> is a finished track.
/// </summary>
public static class Ease
{
    /// <summary>Full progress in fixed point (16 fractional bits).</summary>
    public const int One = 65536;

    /// <summary>
    /// The smoothstep 3u²−2u³ sampled at 65 points, <c>(192i²−2i³+2)/4</c> with floor division
    /// (round-half-up of <c>One·s(i/64)</c>), pinned here as literals.
    /// </summary>
    private static readonly int[] Table =
    {
        0, 48, 188, 419, 736, 1138, 1620, 2181,
        2816, 3524, 4300, 5143, 6048, 7014, 8036, 9113,
        10240, 11416, 12636, 13899, 15200, 16538, 17908, 19309,
        20736, 22188, 23660, 25151, 26656, 28174, 29700, 31233,
        32768, 34304, 35836, 37363, 38880, 40386, 41876, 43349,
        44800, 46228, 47628, 48999, 50336, 51638, 52900, 54121,
        55296, 56424, 57500, 58523, 59488, 60394, 61236, 62013,
        62720, 63356, 63916, 64399, 64800, 65118, 65348, 65489,
        65536
    };

    /// <summary>Linear progress: <c>num/den</c> scaled to 0…<see cref="One"/>, floored.</summary>
    public static int Linear(int num, int den)
    {
        if (den <= 0) return One;
        var n = num < 0 ? 0 : num > den ? den : num;
        return (int)((long)n * One / den);
    }

    /// <summary>Smooth progress: the pinned smoothstep table, linearly interpolated between samples.</summary>
    public static int Smooth(int num, int den)
    {
        if (den <= 0) return One;
        var n = num < 0 ? 0 : num > den ? den : num;
        var x = (long)n * 64 * One / den;
        var i = (int)(x >> 16);
        var f = x & 0xFFFF;
        if (i >= 64) return One;
        return Table[i] + (int)(((long)(Table[i + 1] - Table[i]) * f) >> 16);
    }

    /// <summary>Progress along the curve <paramref name="kind"/>.</summary>
    public static int Apply(EaseKind kind, int num, int den) => kind switch
    {
        EaseKind.Linear => Linear(num, den),
        EaseKind.Smooth => Smooth(num, den),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Undefined ease kind.")
    };
}
