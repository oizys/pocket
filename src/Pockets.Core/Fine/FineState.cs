using System.Collections.Immutable;
using Pockets.Core.Models;
using Pockets.Core.Sim;

namespace Pockets.Core.Fine;

/// <summary>
/// The L2a fine state (P2-D9): the bodies' sub-cell positions and velocities and the stacks' offsets, in 1/16-cell
/// units, ints only. Sparse: a body at rest and a zero offset are not stored. Compares by content, regardless of
/// insertion order. Lives on the session shell, outside L1 (the wall).
/// </summary>
public sealed record FineState
{
    /// <summary>The largest magnitude of a stack offset on either axis.</summary>
    public const int MaxOffset = 6;

    /// <summary>The state with no bodies in motion and no offsets.</summary>
    public static FineState Empty { get; } = new();

    /// <summary>The non-rest bodies, by location and bag.</summary>
    public ImmutableDictionary<(LocationId Loc, Guid Bag), FineBody> Bodies { get; init; } =
        ImmutableDictionary<(LocationId Loc, Guid Bag), FineBody>.Empty;

    /// <summary>The non-zero stack offsets, by cell.</summary>
    public ImmutableDictionary<CellRef, (int Dx, int Dy)> Offsets { get; init; } =
        ImmutableDictionary<CellRef, (int Dx, int Dy)>.Empty;

    /// <summary>True when nothing is stored.</summary>
    public bool IsEmpty => Bodies.IsEmpty && Offsets.IsEmpty;

    /// <summary>The offset of the stack at <paramref name="cell"/>; (0, 0) when none is stored.</summary>
    public (int Dx, int Dy) OffsetAt(CellRef cell) => Offsets.TryGetValue(cell, out var o) ? o : (0, 0);

    /// <summary>The body of <paramref name="loc"/> in <paramref name="bag"/>; <see cref="FineBody.Rest"/> when none is stored.</summary>
    public FineBody BodyAt(LocationId loc, Guid bag) => Bodies.TryGetValue((loc, bag), out var b) ? b : FineBody.Rest;

    /// <summary>
    /// This state with the offset of <paramref name="cell"/> set, each axis clamped to ±<see cref="MaxOffset"/>;
    /// (0, 0) removes it. Returns this same instance when nothing changes.
    /// </summary>
    public FineState WithOffset(CellRef cell, int dx, int dy)
    {
        var o = (Math.Clamp(dx, -MaxOffset, MaxOffset), Math.Clamp(dy, -MaxOffset, MaxOffset));
        if (OffsetAt(cell) == o) return this;
        return this with { Offsets = o == (0, 0) ? Offsets.Remove(cell) : Offsets.SetItem(cell, o) };
    }

    /// <summary>
    /// This state with the body of <paramref name="loc"/> in <paramref name="bag"/> set; a rest body removes the
    /// entry. Returns this same instance when nothing changes.
    /// </summary>
    public FineState WithBody(LocationId loc, Guid bag, FineBody body)
    {
        if (BodyAt(loc, bag) == body) return this;
        return this with { Bodies = body.IsRest ? Bodies.Remove((loc, bag)) : Bodies.SetItem((loc, bag), body) };
    }

    /// <summary>Content equality: the same bodies and offsets, in any insertion order.</summary>
    public bool Equals(FineState? other) =>
        other is not null
        && (ReferenceEquals(this, other)
            || (Bodies.Count == other.Bodies.Count
                && Offsets.Count == other.Offsets.Count
                && Bodies.All(kv => other.Bodies.TryGetValue(kv.Key, out var b) && b == kv.Value)
                && Offsets.All(kv => other.Offsets.TryGetValue(kv.Key, out var o) && o == kv.Value)));

    /// <summary>An order-insensitive hash of the content.</summary>
    public override int GetHashCode() =>
        Bodies.Aggregate(0, (h, kv) => h ^ HashCode.Combine(kv.Key, kv.Value))
        ^ Offsets.Aggregate(0, (h, kv) => h ^ HashCode.Combine(kv.Key, kv.Value) * 31);
}
