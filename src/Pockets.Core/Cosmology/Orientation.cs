namespace Pockets.Core.Cosmology;

/// <summary>
/// One of the 8 chiralities of the entropy cosmology, encoded as the 3-bit enum
/// from the entropy design context (§2.4 "Chirality / Orientation"):
/// <c>(Transpose, XInvert, YInvert)</c>. This is the single geometric source of
/// truth shared by three consumers — asset generation (glyphs), narrative, and
/// game mechanics (bag orientation, cell-0 corner, plaque flow).
///
/// <para>
/// As a linear map on the plane an orientation is the signed permutation matrix
/// <c>M = T^Transpose · X^XInvert · Y^YInvert</c>, where
/// <c>X = diag(-1, 1)</c>, <c>Y = diag(1, -1)</c>, and <c>T</c> swaps the axes.
/// The 8 combinations are exactly the dihedral group D4 (the symmetries of the
/// square), so the 3-bit encoding is a bijection onto the 8 distinct chiralities.
/// </para>
/// </summary>
public readonly record struct Orientation(bool Transpose, bool XInvert, bool YInvert)
{
    /// <summary>The identity orientation — no transpose, no inversion (Right-Down).</summary>
    public static readonly Orientation Identity = new(false, false, false);

    /// <summary>The 3-bit code packed as Transpose(4) | XInvert(2) | YInvert(1), 0..7.</summary>
    public int Code => (Transpose ? 4 : 0) | (XInvert ? 2 : 0) | (YInvert ? 1 : 0);

    /// <summary>Build an orientation from its 3-bit code (0..7).</summary>
    public static Orientation FromCode(int code)
    {
        if (code is < 0 or > 7)
            throw new ArgumentOutOfRangeException(nameof(code), code, "Orientation code must be 0..7.");
        return new Orientation((code & 4) != 0, (code & 2) != 0, (code & 1) != 0);
    }

    /// <summary>All 8 orientations, ordered by <see cref="Code"/>.</summary>
    public static IEnumerable<Orientation> All => Enumerable.Range(0, 8).Select(FromCode);

    /// <summary>
    /// Apply this orientation to a plane vector, as the linear map
    /// <c>M = T · X · Y</c> (invert first, transpose last). Integer overload used
    /// for reading-direction math on unit vectors.
    /// </summary>
    public (int X, int Y) Apply(int x, int y)
    {
        if (YInvert) y = -y;
        if (XInvert) x = -x;
        return Transpose ? (y, x) : (x, y);
    }

    /// <summary>Apply this orientation to a floating-point vector (same map as the integer overload).</summary>
    public (double X, double Y) Apply(double x, double y)
    {
        if (YInvert) y = -y;
        if (XInvert) x = -x;
        return Transpose ? (y, x) : (x, y);
    }

    /// <summary>
    /// The chirality's reading direction: primary = the direction the glyph's
    /// strokes point (image of +x), secondary = the direction of stroke-length
    /// progression / line stacking (image of +y). E.g. the identity reads
    /// Right-Down ("go right, then down").
    /// </summary>
    public (Direction Primary, Direction Secondary) Reading()
    {
        var (px, py) = Apply(1, 0);
        var (sx, sy) = Apply(0, 1);
        return (Directions.FromVector(px, py), Directions.FromVector(sx, sy));
    }

    /// <summary>The reading label, e.g. "Right-Down". Matches the cosmology chirality column.</summary>
    public string ReadingLabel()
    {
        var (primary, secondary) = Reading();
        return $"{primary}-{secondary}";
    }

    /// <summary>
    /// The determinant of the linear map: +1 for a rotation (proper), -1 for a
    /// reflection (improper). A reflection flips arc sweep direction.
    /// </summary>
    public int Determinant =>
        (Transpose ? -1 : 1) * (XInvert ? -1 : 1) * (YInvert ? -1 : 1);

    /// <summary>True when this orientation is a reflection (odd number of set bits).</summary>
    public bool IsReflection => Determinant < 0;

    /// <summary>
    /// The "negative aspect" partner of this chirality: the reading pair reversed
    /// (primary and secondary swapped). Geometrically this post-composes the axis
    /// swap, i.e. <c>M · T</c>. The cosmology's negative sub-zones transpose the
    /// chirality of their positive sibling exactly this way.
    /// </summary>
    public Orientation Transposed()
    {
        // Columns of M are [strokeDir | progressionDir]; swapping the reading pair
        // swaps those columns, which is the map M·T. Find the unique D4 element
        // whose basis images equal the swapped columns.
        var stroke = Apply(0, 1);      // new +x image (old progression -> stroke)
        var progression = Apply(1, 0); // new +y image (old stroke -> progression)
        return All.First(o => o.Apply(1, 0) == stroke && o.Apply(0, 1) == progression);
    }
}
