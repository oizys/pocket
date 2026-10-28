using Pockets.Core.Models;

namespace Pockets.Core.Fine;

/// <summary>
/// A body's L2a state (P2-D9): position and velocity within its cell, centre-relative, in 1/16-cell units,
/// plus the direction it is held in. Ints only.
/// </summary>
public sealed record FineBody(int Px, int Py, int Vx, int Vy, Direction8? Held)
{
    /// <summary>A body at its cell centre, not moving, not held.</summary>
    public static FineBody Rest { get; } = new(0, 0, 0, 0, null);

    /// <summary>True when this body is at <see cref="Rest"/> (such a body is not stored).</summary>
    public bool IsRest => this == Rest;
}
