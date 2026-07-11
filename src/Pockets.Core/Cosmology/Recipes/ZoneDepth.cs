namespace Pockets.Core.Cosmology.Recipes;

/// <summary>
/// A single node in the progression graph: one wilderness at <see cref="Zone"/>,
/// <see cref="Depth"/>. Depth is the <b>chain depth within the node's quadrant</b>,
/// numbered continuously across the +/− sibling boundary — so the Quiet quadrant
/// runs Quiet+ depths 1..K then Quiet− (Bloom) depths K+1.. (e.g. Bloom 11 follows
/// Quiet 10). This is the atom that recipes target and that reachability projects
/// over; it is also the future radar-minimap coordinate.
///
/// <para>
/// Deeper entropy = more nested, not more distant (cosmology: "out means more
/// nested"). Depth is 1-based; depth 1 is a quadrant's entry wilderness.
/// </para>
/// </summary>
public readonly record struct ZoneDepth(Zone Zone, int Depth)
{
    /// <summary>The quadrant this node's zone belongs to.</summary>
    public Quadrant Quadrant => EntropyMatrix.Info(Zone).Quadrant;

    /// <summary>The aspect (+/−) of this node's zone.</summary>
    public Aspect Aspect => EntropyMatrix.Info(Zone).Aspect;

    /// <summary>A stable, file- and log-safe key, e.g. "quiet-negative:11".</summary>
    public string Key
    {
        get
        {
            var info = EntropyMatrix.Info(Zone);
            string aspect = info.Aspect == Aspect.Positive ? "positive" : "negative";
            return $"{info.Quadrant.ToString().ToLowerInvariant()}-{aspect}:{Depth}";
        }
    }

    /// <summary>A short human label, e.g. "Bloom 11" (flavor noun + depth).</summary>
    public override string ToString()
    {
        var info = EntropyMatrix.Info(Zone);
        string sign = info.Aspect == Aspect.Positive ? "+" : "−";
        return $"{info.Quadrant}{sign} {Depth}";
    }
}
