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

    /// <summary>Starts at the rest timeline of <paramref name="initial"/>; <paramref name="policy"/> defaults to <see cref="RealizationPolicy.Default"/>.</summary>
    public PoseStream(GameState initial, BagNames names, RealizationPolicy? policy = null)
    {
        _text = new PoseText(names);
        _policy = policy ?? RealizationPolicy.Default;
        Timeline = Timeline.Rest(Scene.FromState(initial, Shown));
        Last = [];
    }

    /// <summary>The current timeline.</summary>
    public Timeline Timeline { get; private set; }

    /// <summary>The last commit's pose lines (empty before the first commit).</summary>
    public ImmutableArray<string> Last { get; private set; }

    /// <summary>
    /// Commits <paramref name="events"/> leading to <paramref name="next"/> at clock delta <paramref name="atMs"/>
    /// and returns (and remembers) the commit's pose lines.
    /// </summary>
    public ImmutableArray<string> Commit(IEnumerable<SimEvent> events, GameState next, int atMs)
    {
        Timeline = Timeline.Commit(events, Scene.FromState(next, Shown, null, Timeline.Facings), _policy, atMs);
        Last = Lines(Timeline, _text);
        return Last;
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
