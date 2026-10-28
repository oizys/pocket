using Pockets.Core.Models;
using Pockets.Core.Sim;

namespace Pockets.Core.Fine;

/// <summary>
/// Seeded L2a noise for the fuzz gate (Slice 2.8, "fuzz L2a ⇒ identical L1"): a fine state that L1 must be blind to.
/// Ints only and counter-hashed (no <see cref="Random"/>), so a seed gives the same noise on every driver and machine.
/// Test and gate plumbing: nothing in a game path calls it.
/// </summary>
public static class FineFuzz
{
    /// <summary>The largest fuzzed body position on either axis: inside the cell, short of a crossing.</summary>
    public const int MaxBody = 7;

    /// <summary>
    /// A random fine state over <paramref name="game"/>, determined by <paramref name="seed"/> and
    /// <paramref name="salt"/> (the runner passes the journey step). For each location present (in
    /// <see cref="LocationId"/> order), about half the occupied cells of its active bag get an offset in
    /// ±<see cref="FineState.MaxOffset"/> per axis, and its body gets a resting position in ±<see cref="MaxBody"/>
    /// (no velocity, nothing held). Zero draws store nothing, as the sparse store requires.
    /// </summary>
    public static FineState Generate(GameState game, int seed, int salt)
    {
        var draw = 0;
        int Next(int range) => (int)(Hash((uint)seed, (uint)salt, (uint)draw++) % (uint)range);
        int Signed(int max) => Next(2 * max + 1) - max;

        return Enum.GetValues<LocationId>()
            .Where(loc => game.Locations.TryGet(loc) is not null)
            .Aggregate(FineState.Empty, (fine, loc) =>
            {
                var bag = game.ActiveBagAt(loc);
                var withOffsets = Enumerable.Range(0, bag.Grid.Cells.Length)
                    .Where(i => !bag.Grid.Cells[i].IsEmpty)
                    .Aggregate(fine, (f, i) => Next(2) == 0
                        ? f.WithOffset(new CellRef(bag.Id, i), Signed(FineState.MaxOffset), Signed(FineState.MaxOffset))
                        : f);
                return withOffsets.WithBody(loc, bag.Id, new FineBody(Signed(MaxBody), Signed(MaxBody), 0, 0, null));
            });
    }

    /// <summary>A 32-bit avalanche hash of three counters (a murmur3-style finalizer over a mixed key).</summary>
    private static uint Hash(uint a, uint b, uint c)
    {
        var h = a * 0x9E3779B1u ^ (b + 0x7F4A7C15u) * 0x85EBCA77u ^ (c + 0x165667B1u) * 0xC2B2AE3Du;
        h ^= h >> 16;
        h *= 0x85EBCA6Bu;
        h ^= h >> 13;
        h *= 0xC2B2AE35u;
        h ^= h >> 16;
        return h;
    }
}
