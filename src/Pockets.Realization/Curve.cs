namespace Pockets.Realization;

/// <summary>The shape of a track's progress over time. No zero member.</summary>
public enum Curve
{
    /// <summary>Constant speed.</summary>
    Linear = 1,
    /// <summary>Smoothstep.</summary>
    Smooth,
    /// <summary>Out and back, each half smooth.</summary>
    PingPong,
    /// <summary>Three triangle cycles, starting in the positive direction.</summary>
    Shake
}

/// <summary>What part of a pose a track drives. No zero member.</summary>
public enum Channel
{
    /// <summary>Cell and sub-cell position.</summary>
    Motion = 1,
    /// <summary>Height above the grid.</summary>
    Height,
    /// <summary>Appear/disappear bookkeeping; never affects sampling.</summary>
    Fade
}

/// <summary>
/// One animated interval of one entity. <see cref="Entity"/> is the key the sampled pose carries (the departure
/// key); <c>To.Entity</c> is the rest key the track lands on. Non-overlay tracks substitute that rest pose;
/// overlay tracks are drawn as an extra pose.
/// </summary>
/// <param name="Entity">The key the sampled pose carries.</param>
/// <param name="From">The pose at progress 0.</param>
/// <param name="To">The pose at progress 1.</param>
/// <param name="StartMs">Start, in ms from the commit.</param>
/// <param name="DurMs">Duration in ms.</param>
/// <param name="Curve">The progress curve.</param>
/// <param name="Channel">The driven channel.</param>
public sealed record Track(EntityKey Entity, Pose From, Pose To, int StartMs, int DurMs, Curve Curve, Channel Channel)
{
    /// <summary>True when the track is drawn as an extra pose instead of replacing the rest pose.</summary>
    public bool Overlay { get; init; }

    /// <summary>The end time in ms.</summary>
    public int EndMs => StartMs + DurMs;

    /// <summary>Signed fixed-point progress of this track at <paramref name="ms"/> (negative only for Shake).</summary>
    public int ProgressAt(int ms)
    {
        var den = DurMs;
        var num = ms - StartMs;
        num = num < 0 ? 0 : num > den ? den : num;
        switch (Curve)
        {
            case Curve.Linear: return Ease.Linear(num, den);
            case Curve.Smooth: return Ease.Smooth(num, den);
            case Curve.PingPong:
                return 2 * num <= den ? Ease.Smooth(2 * num, den) : Ease.Smooth(2 * (den - num), den);
            case Curve.Shake:
            {
                if (num >= den) return 0;
                var k = (long)num * 12 * Ease.One / den;
                var q = (int)(k / Ease.One) % 4;
                var r = (int)(k % Ease.One);
                return q switch { 0 => r, 1 => Ease.One - r, 2 => -r, _ => -(Ease.One - r) };
            }
            default: throw new ArgumentOutOfRangeException(nameof(Curve), Curve, "Undefined curve.");
        }
    }
}
