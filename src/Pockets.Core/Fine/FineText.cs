using System.Collections.Immutable;
using System.Globalization;
using Pockets.Core.Sim;

namespace Pockets.Core.Fine;

/// <summary>The canonical text of a <see cref="FineState"/>, for the debug protocol and goldens.</summary>
public static class FineText
{
    /// <summary>
    /// The state as lines, bodies first then offsets, bags named through <paramref name="names"/>. Bags not yet
    /// named are named in a deterministic pass (bodies by location then bag id, then offsets by bag id then index);
    /// the lines are then ordered by location and numeric bag name (bodies) or numeric bag name and index (offsets).
    /// </summary>
    public static ImmutableArray<string> Lines(FineState fine, BagNames names)
    {
        foreach (var k in fine.Bodies.Keys.OrderBy(k => k.Loc).ThenBy(k => k.Bag))
            names.Name(k.Bag);
        foreach (var c in fine.Offsets.Keys.OrderBy(c => c.Bag).ThenBy(c => c.Index))
            names.Name(c.Bag);

        var bodies = fine.Bodies
            .Select(kv => (kv.Key, Body: kv.Value, Name: names.Name(kv.Key.Bag)))
            .OrderBy(t => t.Key.Loc)
            .ThenBy(t => t.Name.Length)
            .ThenBy(t => t.Name, StringComparer.Ordinal)
            .Select(t => string.Create(CultureInfo.InvariantCulture,
                $"body loc={t.Key.Loc} bag={t.Name} px={t.Body.Px} py={t.Body.Py} vx={t.Body.Vx} vy={t.Body.Vy} held={(t.Body.Held is { } h ? h.ToString() : "-")}"));
        var offsets = fine.Offsets
            .Select(kv => (Cell: kv.Key, Off: kv.Value, Name: names.Name(kv.Key.Bag)))
            .OrderBy(t => t.Name.Length)
            .ThenBy(t => t.Name, StringComparer.Ordinal)
            .ThenBy(t => t.Cell.Index)
            .Select(t => string.Create(CultureInfo.InvariantCulture,
                $"offset cell={t.Name}[{t.Cell.Index}] dx={t.Off.Dx} dy={t.Off.Dy}"));
        return bodies.Concat(offsets).ToImmutableArray();
    }
}
