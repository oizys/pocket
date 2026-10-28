using System.Collections.Immutable;
using System.Globalization;
using Pockets.Core.Models;
using Pockets.Core.Sim;

namespace Pockets.Realization;

/// <summary>
/// The pose stream of one run (Slice 2.4): one <see cref="Timeline"/> committed per command, and the pose lines of
/// each commit, named through the run's bag names so they continue the event stream's <c>bag#N</c>.
/// </summary>
public sealed class PoseStream
{
    private readonly PoseText _text;
    private readonly RealizationPolicy _policy;

    /// <summary>The locations whose bodies and stacks the scene shows.</summary>
    public static ImmutableArray<LocationId> Shown { get; } = [LocationId.B, LocationId.H, LocationId.T];

    /// <summary>
    /// Starts at the rest timeline of <paramref name="initial"/>, its poses offset by <paramref name="fine"/> (none when
    /// null); <paramref name="policy"/> defaults to <see cref="RealizationPolicy.Default"/>.
    /// </summary>
    public PoseStream(GameState initial, BagNames names, RealizationPolicy? policy = null, IFineView? fine = null)
    {
        _text = new PoseText(names);
        _policy = policy ?? RealizationPolicy.Default;
        Timeline = Timeline.Rest(Scene.FromState(initial, Shown, fine));
        Last = [];
    }

    /// <summary>The current timeline.</summary>
    public Timeline Timeline { get; private set; }

    /// <summary>The last commit's pose lines (empty before the first commit).</summary>
    public ImmutableArray<string> Last { get; private set; }

    /// <summary>
    /// Commits <paramref name="events"/> leading to <paramref name="next"/> at clock delta <paramref name="atMs"/>
    /// and returns (and remembers) the commit's pose lines: the sampled lines, then the <see cref="DrivenLines"/>.
    /// <paramref name="fine"/> is the fine view after the commit (the session's L2a state); null means none.
    /// </summary>
    public ImmutableArray<string> Commit(IEnumerable<SimEvent> events, GameState next, int atMs, IFineView? fine = null)
    {
        var before = Timeline.Scene;
        Timeline = Timeline.Commit(events, Scene.FromState(next, Shown, fine, Timeline.Facings), _policy, atMs);
        Last = Lines(Timeline, _text).AddRange(DrivenLines(before, Timeline.Scene, _text));
        return Last;
    }

    /// <summary>
    /// Slice 2.8, pinning the glide: a D2-B body has no tracks, so <see cref="Lines"/> never shows its sub-cell walk or the
    /// stacks it nudges. A commit is <b>driven</b> when a shown location's body is driven before or after it. A driven
    /// commit adds one line <c>fine {pose line}</c> of the new rest pose for each driven body whose rest pose changed
    /// (in <see cref="Shown"/> order), then for each stack that stayed in its cell but whose offset changed (the nudge;
    /// in scene order). A commit with no driven body, which is every commit of a grid-locked journey, adds nothing, so older
    /// pose goldens are unchanged.
    /// </summary>
    public static IEnumerable<string> DrivenLines(Scene before, Scene after, PoseText text)
    {
        var driven = Shown.Where(loc => before.Driven.Contains(loc) || after.Driven.Contains(loc)).ToList();
        if (driven.Count == 0) return [];
        var bodies = driven
            .Select(loc => (Was: before.Find(new EntityKey.Body(loc)), Now: after.Find(new EntityKey.Body(loc))))
            .Where(p => p.Now is not null && p.Now != p.Was)
            .Select(p => p.Now!.Value);
        var nudged = after.Poses
            .Where(p => p.Entity is EntityKey.Stack)
            .Where(p => before.Find(p.Entity) is { } was && was.CellCol == p.CellCol && was.CellRow == p.CellRow
                        && (was.SubX, was.SubY) != (p.SubX, p.SubY));
        return bodies.Concat(nudged).Select(p => $"fine {text.Line(p)}").ToList();
    }

    /// <summary>
    /// The pose lines of a timeline: for q in 0..4 the poses of the moving entities (any track's entity or target
    /// entity) sampled at SpanMs·q/4, each as <c>u=q/4 t=ms {pose line}</c>. Only what <see cref="Timeline.Sample"/> draws
    /// is written: an overlay (a cursor glide) that has finished by a sample is not drawn, so it has no line there.
    /// No tracks gives no lines.
    /// </summary>
    public static ImmutableArray<string> Lines(Timeline t, PoseText text)
    {
        var moving = t.Tracks.SelectMany(k => new[] { k.Entity, k.To.Entity }).ToHashSet();
        return Enumerable.Range(0, 5)
            .Where(_ => moving.Count > 0)
            .SelectMany(q =>
            {
                var ms = t.SpanMs * q / 4;
                return t.Sample(ms).Where(p => moving.Contains(p.Entity))
                    .Select(p => $"u={q.ToString(CultureInfo.InvariantCulture)}/4 t={ms.ToString(CultureInfo.InvariantCulture)} {text.Line(p)}");
            })
            .ToImmutableArray();
    }
}
