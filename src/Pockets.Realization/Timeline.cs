using Pockets.Core.Models;
using Pockets.Core.Sim;

namespace Pockets.Realization;

/// <summary>
/// A scene plus the tracks that animate towards it (slice 2.2). Tracks are relative to the commit's 0 ms; a player
/// commit drops all old tracks (D7 snap); a clock commit keeps them rebased (P2-D13). Facings persist across commits.
/// </summary>
/// <param name="Scene">The rest scene the tracks land on.</param>
/// <param name="Tracks">The live tracks.</param>
/// <param name="Facings">The last known facing of each body.</param>
public sealed record Timeline(Scene Scene, ImmutableArray<Track> Tracks, ImmutableDictionary<LocationId, Facing> Facings)
{
    /// <summary>A timeline at rest on <paramref name="scene"/>: no tracks, no facings.</summary>
    public static Timeline Rest(Scene scene) =>
        new(scene, ImmutableArray<Track>.Empty, ImmutableDictionary<LocationId, Facing>.Empty);

    /// <summary>The end of the last track, or 0 when there are none.</summary>
    public int SpanMs => Tracks.Select(t => t.EndMs).DefaultIfEmpty(0).Max();

    // ==================== Commit ====================

    /// <summary>
    /// Commits <paramref name="events"/> leading to <paramref name="next"/> and updates the facings. A player commit
    /// snaps (D7): all old tracks are dropped. A clock commit (only Tick/SetElapsed envelopes and TimeAdvanced runs,
    /// P2-D13) keeps the live tracks that end after <paramref name="atMs"/> and still have a column layout, rebased by
    /// <paramref name="atMs"/>. New tracks are staged by phase: Player tracks play at 0 ms, Reaction and Ambient tracks
    /// start at the policy's overlap percent of the Player phase's span.
    /// </summary>
    public Timeline Commit(IEnumerable<SimEvent> events, Scene next, RealizationPolicy policy, int atMs = 0)
    {
        var list = events.ToList();
        var snap = !Choreography.IsClock(list);
        var facings = Facings;
        var applied = next with
        {
            Poses = next.Poses.Select(p =>
                p.Entity is EntityKey.Body b && facings.TryGetValue(b.Loc, out var f) ? p with { Facing = f } : p)
                .ToImmutableArray()
        };
        var built = new List<List<Track>>();
        foreach (var ev in list)
        {
            var (added, loc, facing) = Build(ev, applied, Scene, policy);
            if (loc is { } l && facing is { } fc)
            {
                facings = facings.SetItem(l, fc);
                applied = applied with
                {
                    Poses = applied.Poses.Select(p => p.Entity is EntityKey.Body b && b.Loc == l ? p with { Facing = fc } : p)
                        .ToImmutableArray()
                };
            }
            built.Add(added);
        }

        var tracks = snap
            ? new List<Track>()
            : Tracks.Where(t => t.EndMs > atMs && next.Columns.ContainsKey(t.To.Bag))
                .Select(t => t with { StartMs = t.StartMs - atMs }).ToList();
        var keptCount = tracks.Count;
        var phases = Choreography.Phases(list);
        var lastRewind = list.FindLastIndex(e => e is SimEvent.Rewound);
        var order = Enumerable.Range(0, list.Count).Where(i => i > lastRewind)
            .OrderBy(i => (int)phases[i]).ThenBy(i => i).ToList();

        var offset = 0;
        var offsetSet = false;
        foreach (var i in order)
        {
            if (!offsetSet && phases[i] != TurnPhase.Player)
            {
                var span0 = tracks.Skip(keptCount).Select(t => t.EndMs).DefaultIfEmpty(0).Max();
                offset = span0 * policy.ReactionOverlapPercent / 100;
                offsetSet = true;
            }
            foreach (var t0 in built[i])
            {
                var t = phases[i] == TurnPhase.Player ? t0 : t0 with { StartMs = t0.StartMs + offset };
                if (list[i] is SimEvent.BagRearranged) tracks.Add(t); else Add(tracks, t);
            }
        }
        return new Timeline(applied, tracks.ToImmutableArray(), facings);
    }

    /// <summary>Adds a track, chaining it after the latest track on the same entity and channel when one exists.</summary>
    private static void Add(List<Track> tracks, Track t)
    {
        if (t.Channel == Channel.Fade) { tracks.Add(t); return; }
        var i = tracks.FindLastIndex(e => e.Channel == t.Channel && e.Overlay == t.Overlay
                                          && e.To.Entity == t.Entity);
        if (i < 0) { tracks.Add(t); return; }
        var e = tracks[i];
        tracks[i] = e with { To = e.To with { Entity = t.To.Entity } };
        tracks.Add(t with { Entity = e.Entity, StartMs = e.EndMs > t.StartMs ? e.EndMs : t.StartMs });
    }

    /// <summary>The tracks (and any facing change) one event produces.</summary>
    private static (List<Track> Tracks, LocationId? Loc, Facing? Facing) Build(
        SimEvent ev, Scene next, Scene prev, RealizationPolicy policy)
    {
        var none = new List<Track>();
        var curve = policy.Ease == EaseKind.Linear ? Curve.Linear : Curve.Smooth;
        switch (ev)
        {
            case SimEvent.CursorMoved m:
            {
                var dc = m.To.Col - m.From.Col;
                var dr = m.To.Row - m.From.Row;
                var jump = Abs(dc) + Abs(dr) > 1;
                var body = BodyPose(next, m.Loc, m.Bag);
                if (body is { } b)
                {
                    var face = FacingOf(Norm(dc), Norm(dr));
                    if (jump) return (none, m.Loc, face);
                    var origin0 = b with { CellCol = m.From.Col, CellRow = m.From.Row, SubX = 0, SubY = 0, Facing = face };
                    var to = b with { CellCol = m.To.Col, CellRow = m.To.Row, SubX = 0, SubY = 0, Facing = face };
                    return (new List<Track> { new(b.Entity, origin0, to, 0, policy.WalkMs, curve, Channel.Motion) }, m.Loc, face);
                }
                if (next.Columns.ContainsKey(m.Bag) && !jump)
                {
                    var key = new EntityKey.Cursor(m.Loc);
                    var origin0 = new Pose(key, m.Bag, m.From.Col, m.From.Row, 0, 0, null, null);
                    var to = new Pose(key, m.Bag, m.To.Col, m.To.Row, 0, 0, null, null);
                    return (new List<Track>
                    {
                        new Track(key, origin0, to, 0, policy.CursorGlideMs, Curve.Linear, Channel.Motion) { Overlay = true }
                    }, null, null);
                }
                return (none, null, null);
            }
            case SimEvent.Bumped { Reason: BumpReason.Unnavigable, Dir: { } dir } bump:
            {
                if (BodyPose(next, bump.Loc, bump.Bag) is not { } b) return (none, null, null);
                var (ux, uy) = dir switch
                {
                    Direction.Right => (4, 0),
                    Direction.Left => (-4, 0),
                    Direction.Down => (0, 4),
                    _ => (0, -4)
                };
                var face = dir switch
                {
                    Direction.Up => Facing.N,
                    Direction.Down => Facing.S,
                    Direction.Left => Facing.W,
                    _ => Facing.E
                };
                var origin0 = b with { SubX = 0, SubY = 0, Facing = face };
                var to = origin0 with { SubX = origin0.SubX + ux, SubY = origin0.SubY + uy };
                return (new List<Track> { new(b.Entity, origin0, to, 0, policy.BumpMs, Curve.PingPong, Channel.Motion) },
                    bump.Loc, face);
            }
            case SimEvent.Bumped { Reason: BumpReason.FailedPeek } peek:
            {
                var rest = next.Poses.Where(p => p.Entity is EntityKey.Stack && p.Bag == peek.Bag
                        && p.CellCol == peek.At.Col && p.CellRow == peek.At.Row)
                    .Cast<Pose?>().FirstOrDefault();
                if (rest is not { } r) return (none, null, null);
                return (new List<Track>
                {
                    new(r.Entity, r, r with { SubX = r.SubX + 2 }, 0, policy.ShakeMs, Curve.Shake, Channel.Motion)
                }, null, null);
            }
            case SimEvent.StackMoved s: return (Stack(s.From, s.To, false, next, prev, policy, curve), null, null);
            case SimEvent.StackSplit s: return (Stack(s.From, s.To, false, next, prev, policy, curve), null, null);
            case SimEvent.StackMerged s: return (Stack(s.From, s.To, true, next, prev, policy, curve), null, null);
            case SimEvent.BagRearranged r:
                return (Rearrange(r, next, prev, policy, curve), null, null);
            case SimEvent.Acquired a:
                return (Pop(new[] { a.Cell }, next, policy), null, null);
            case SimEvent.Harvested h:
                return (Pop(new[] { h.Cell }.Concat(h.Yields.Select(y => y.Cell)).Distinct(), next, policy), null, null);
            default: return (none, null, null);
        }
    }

    /// <summary>The L2a offset the stack in <paramref name="cell"/> rested at in <paramref name="prev"/>, (0, 0) when none: a slide starts from it.</summary>
    private static (int SubX, int SubY) PrevSub(Scene prev, CellRef cell) =>
        RestStack(prev, cell) is { } p ? (p.SubX, p.SubY) : (0, 0);

    /// <summary>The tracks of a stack leaving <paramref name="from"/> for <paramref name="to"/>, starting at its offset in <paramref name="prev"/>.</summary>
    private static List<Track> Stack(CellRef from, CellRef to, bool overlay, Scene next, Scene prev, RealizationPolicy policy, Curve curve)
    {
        var rest = RestStack(next, to);
        if (from.Bag == to.Bag)
        {
            if (!next.Columns.TryGetValue(from.Bag, out var cols)) return new List<Track>();
            var r = rest ?? new Pose(new EntityKey.Stack(to), to.Bag, to.Index % cols, to.Index / cols, 0, 0, null, null);
            var key = new EntityKey.Stack(from);
            var (sx, sy) = PrevSub(prev, from);
            var start = new Pose(key, from.Bag, from.Index % cols, from.Index / cols, sx, sy, null, null);
            return new List<Track>
            {
                new Track(key, start, r, 0, policy.SlideMs, curve, Channel.Motion) { Overlay = overlay }
            };
        }
        var toKey = new EntityKey.Stack(to);
        Pose? target = rest ?? (next.Columns.TryGetValue(to.Bag, out var tc)
            ? new Pose(toKey, to.Bag, to.Index % tc, to.Index / tc, 0, 0, null, null)
            : null);
        if (target is not { } tp) return new List<Track>();
        var (ox, oy) = PrevSub(prev, from);
        var origin = next.Columns.TryGetValue(from.Bag, out var fc)
            ? new Pose(new EntityKey.Stack(from), from.Bag, from.Index % fc, from.Index / fc, ox, oy, null, null)
            : tp;
        return new List<Track> { new(toKey, origin, tp, 0, policy.SlideMs, Curve.Linear, Channel.Fade) };
    }

    /// <summary>The parallel slides of a sort, each starting at its stack's offset in <paramref name="prev"/>.</summary>
    private static List<Track> Rearrange(SimEvent.BagRearranged r, Scene next, Scene prev, RealizationPolicy policy, Curve curve)
    {
        if (!next.Columns.TryGetValue(r.Bag, out var cols)) return new List<Track>();
        return r.Permutation
            .Select((old, i) => (old, i))
            .Where(t => t.old >= 0 && t.old != t.i)
            .Select(t => (t.old, t.i, rest: RestStack(next, new CellRef(r.Bag, t.i))))
            .Where(t => t.rest is not null)
            .Select(t =>
            {
                var key = new EntityKey.Stack(new CellRef(r.Bag, t.old));
                var (sx, sy) = PrevSub(prev, new CellRef(r.Bag, t.old));
                var origin0 = new Pose(key, r.Bag, t.old % cols, t.old / cols, sx, sy, null, null);
                return new Track(key, origin0, t.rest!.Value, 0, policy.SortMs, curve, Channel.Motion);
            })
            .ToList();
    }

    /// <summary>The height pops of the stacks resting in <paramref name="cells"/>.</summary>
    private static List<Track> Pop(IEnumerable<CellRef> cells, Scene next, RealizationPolicy policy) =>
        cells.Select(c => RestStack(next, c))
            .Where(r => r is not null)
            .Select(r => new Track(r!.Value.Entity, r.Value with { Height = 0 }, r.Value with { Height = 6 }, 0,
                policy.PopMs, Curve.PingPong, Channel.Height))
            .ToList();

    /// <summary>The rest pose of the stack in <paramref name="cell"/>, if any.</summary>
    private static Pose? RestStack(Scene scene, CellRef cell) => scene.Find(new EntityKey.Stack(cell));

    /// <summary>The rest body pose of <paramref name="loc"/> when it stands in world bag <paramref name="bag"/>.</summary>
    private static Pose? BodyPose(Scene scene, LocationId loc, Guid bag) =>
        scene.Find(new EntityKey.Body(loc)) is { } p && p.Bag == bag ? p : null;

    /// <summary>The absolute value.</summary>
    private static int Abs(int n) => n < 0 ? -n : n;

    /// <summary>A delta component with a wrap (|d| &gt; 1) turned into the opposite unit step.</summary>
    private static int Norm(int d) => d > 1 ? -1 : d < -1 ? 1 : d;

    /// <summary>The facing of a normalized (dc, dr) delta; S for no movement.</summary>
    private static Facing FacingOf(int dc, int dr) => (dc, dr) switch
    {
        (0, < 0) => Facing.N,
        (> 0, < 0) => Facing.NE,
        (> 0, 0) => Facing.E,
        (> 0, > 0) => Facing.SE,
        (0, > 0) => Facing.S,
        (< 0, > 0) => Facing.SW,
        (< 0, 0) => Facing.W,
        (< 0, < 0) => Facing.NW,
        _ => Facing.S
    };

    // ==================== Sample ====================

    /// <summary>The poses drawn <paramref name="ms"/> after the commit.</summary>
    public ImmutableArray<Pose> Sample(int ms)
    {
        var live = Tracks.Where(t => t.Channel != Channel.Fade).ToList();
        Track? Active(EntityKey rest, Channel ch, bool overlay) => Pick(live, rest, ch, overlay, ms);

        var overlays = live.Where(t => t.Overlay)
            .GroupBy(t => (t.To.Entity, t.Channel))
            .Select(g => (g.Key.Entity, Track: Pick(live, g.Key.Entity, g.Key.Channel, true, ms)))
            .Where(x => x.Track is not null)
            .ToList();
        var restKeys = Scene.Poses.Select(p => p.Entity).ToHashSet();

        var main = Scene.Poses.SelectMany(r =>
        {
            var p = r;
            if (Active(r.Entity, Channel.Motion, false) is { } m)
            {
                var (c, rw, sx, sy, _) = At(m, ms);
                p = p with { Entity = m.Entity, Bag = m.To.Bag, CellCol = c, CellRow = rw, SubX = sx, SubY = sy,
                    Facing = m.To.Facing ?? p.Facing };
            }
            if (Active(r.Entity, Channel.Height, false) is { } h) p = p with { Height = At(h, ms).Height };
            return new[] { p }.Concat(overlays.Where(o => o.Entity == r.Entity).Select(o => Extra(o.Track!, ms)));
        });
        var extra = overlays.Where(o => !restKeys.Contains(o.Entity)).Select(o => Extra(o.Track!, ms));
        return main.Concat(extra).ToImmutableArray();
    }

    /// <summary>The overlay pose of an active track.</summary>
    private static Pose Extra(Track t, int ms)
    {
        var (c, r, sx, sy, h) = At(t, ms);
        return new Pose(t.Entity, t.To.Bag, c, r, sx, sy, t.To.Height, t.To.Facing);
    }

    /// <summary>The track of group (<paramref name="rest"/>, channel, overlay) active at <paramref name="ms"/>, or null at rest.</summary>
    private static Track? Pick(List<Track> live, EntityKey rest, Channel ch, bool overlay, int ms)
    {
        var group = live.Where(t => t.To.Entity == rest && t.Channel == ch && t.Overlay == overlay)
            .OrderBy(t => t.StartMs).ToList();
        if (group.Count == 0) return null;
        var pick = group.LastOrDefault(t => t.StartMs <= ms) ?? group[0];
        return ms >= pick.EndMs ? null : pick;
    }

    /// <summary>The interpolated cell, sub-offset and height of <paramref name="t"/> at <paramref name="ms"/>.</summary>
    private static (int Col, int Row, int SubX, int SubY, int Height) At(Track t, int ms)
    {
        var f = t.ProgressAt(ms);
        var ax = Lerp(t.From.CellCol * Pose.SubUnits + t.From.SubX, t.To.CellCol * Pose.SubUnits + t.To.SubX, f);
        var ay = Lerp(t.From.CellRow * Pose.SubUnits + t.From.SubY, t.To.CellRow * Pose.SubUnits + t.To.SubY, f);
        var col = FloorDiv(ax + 8, Pose.SubUnits);
        var row = FloorDiv(ay + 8, Pose.SubUnits);
        return (col, row, ax - col * Pose.SubUnits, ay - row * Pose.SubUnits,
            Lerp(t.From.Height ?? 0, t.To.Height ?? 0, f));
    }

    /// <summary>Fixed-point interpolation <c>a + ((b − a)·f ≫ 16)</c>.</summary>
    private static int Lerp(int a, int b, int f) => a + (int)(((long)(b - a) * f) >> 16);

    /// <summary>Floor division for a positive divisor.</summary>
    private static int FloorDiv(int a, int d) => a >= 0 ? a / d : -((-a + d - 1) / d);
}
