using Pockets.Core.Models;
using Pockets.Core.Sim;

namespace Pockets.Realization;

/// <summary>
/// What a <see cref="Pose"/> belongs to (P2-D2): a body (a location's avatar), a stack (keyed by the cell
/// it occupies) or a transient inventory cursor. Realization-local: it never enters L1. A stack in flight keeps its departure key until its
/// track ends, then is re-keyed to the cell it rests in.
/// </summary>
public abstract record EntityKey
{
    private EntityKey() { }

    /// <summary>The body: the cursor of a location whose active bag is a world bag (P2-D6).</summary>
    /// <param name="Loc">The location the body belongs to.</param>
    public sealed record Body(LocationId Loc) : EntityKey;

    /// <summary>A stack, keyed by the cell it occupies (or, in flight, the cell it left).</summary>
    /// <param name="At">The keying cell.</param>
    public sealed record Stack(CellRef At) : EntityKey;

    /// <summary>An inventory-bag cursor glide of a location; only drawn while its track is live.</summary>
    /// <param name="Loc">The location whose cursor glides.</param>
    public sealed record Cursor(LocationId Loc) : EntityKey;
}
