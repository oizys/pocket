namespace Pockets.Core.Cosmology.Recipes;

/// <summary>
/// The within-zone chain for one quadrant: a single linear ladder of depths
/// 1..(<see cref="PositiveDepths"/> + <see cref="NegativeDepths"/>). The first
/// <see cref="PositiveDepths"/> belong to the quadrant's "+" zone; the remainder
/// belong to its "−" zone, <b>continuing the numbering</b> — this is the cosmology's
/// "negatives extend their + sibling's chain" encoded exactly. With Quiet+ = 10, the
/// negative zone (Bloom) starts at depth 11, whose recipe draws from Quiet 10.
///
/// <para>Depth counts are TUNABLE knobs; only the Quiet+ = 10 boundary is pinned to
/// the cosmology's cited Bloom-11 ← Quiet-10 example.</para>
/// </summary>
public sealed record QuadrantChain(Quadrant Quadrant, int PositiveDepths, int NegativeDepths)
{
    /// <summary>The "+" zone of this quadrant (owns depths 1..<see cref="PositiveDepths"/>).</summary>
    public Zone PositiveZone => EntropyMatrix.Positive(Quadrant).Zone;

    /// <summary>The "−" zone (owns depths <see cref="PositiveDepths"/>+1 upward).</summary>
    public Zone NegativeZone => EntropyMatrix.Negative(Quadrant).Zone;

    /// <summary>Total chain length across both aspects.</summary>
    public int TotalDepths => PositiveDepths + NegativeDepths;

    /// <summary>The combined chain depth (1-based) at which the "−" zone begins.</summary>
    public int NegativeStartDepth => PositiveDepths + 1;

    /// <summary>The node at a given combined chain depth, routed to the "+" or "−" zone.</summary>
    public ZoneDepth NodeAt(int chainDepth)
    {
        if (chainDepth < 1 || chainDepth > TotalDepths)
            throw new ArgumentOutOfRangeException(nameof(chainDepth), chainDepth,
                $"Chain depth must be 1..{TotalDepths} for the {Quadrant} quadrant.");
        return chainDepth <= PositiveDepths
            ? new ZoneDepth(PositiveZone, chainDepth)
            : new ZoneDepth(NegativeZone, chainDepth);
    }

    /// <summary>All nodes of the chain, in ascending depth order.</summary>
    public IEnumerable<ZoneDepth> Nodes =>
        Enumerable.Range(1, TotalDepths).Select(NodeAt);

    /// <summary>The signature material harvested at one node (flavor noun + provenance).</summary>
    public static Material MaterialAt(ZoneDepth node)
    {
        var info = EntropyMatrix.Info(node.Zone);
        var parts = info.Flavor.Split('/');
        string noun = info.Aspect == Aspect.Positive ? parts[0] : parts[^1];
        return new Material($"{node.Key}", $"{noun} (d{node.Depth})", node);
    }
}
