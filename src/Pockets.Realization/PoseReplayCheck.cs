using Pockets.Core.Models;
using Pockets.Core.Sim;

namespace Pockets.Realization;

/// <summary>
/// One recorded commit: the journey step, the session's <see cref="GameSession.IntentLog"/> count right after it,
/// its clock delta, and its unprefixed pose lines.
/// </summary>
public sealed record PoseCommit(int JourneyStep, int IntentCount, int AtMs, IReadOnlyList<string> Lines);

/// <summary>
/// The pose replay gate (Slice 2.4): re-applies a recorded intent log to a fresh session and checks that every
/// recorded commit's pose lines are reproduced.
/// </summary>
public static class PoseReplayCheck
{
    /// <summary>
    /// Replays <paramref name="log"/> from <paramref name="fresh"/> (one <see cref="EventText"/> names the events, then
    /// the poses, as the live run did) and returns the failures, empty when every commit matches: one per differing
    /// commit, plus any commit whose boundary is past the end of the log.
    /// </summary>
    public static IReadOnlyList<string> Run(GameSession fresh,
        IReadOnlyList<(int Step, Intent Intent)> log, IReadOnlyList<PoseCommit> commits)
    {
        var failures = new List<string>();
        var text = new EventText();
        var stream = new PoseStream(fresh.Current, text.Names);
        var session = fresh;
        var pending = new List<SimEvent>();
        var next = 0;

        void Flush(int applied)
        {
            while (next < commits.Count && commits[next].IntentCount == applied)
            {
                var c = commits[next++];
                var lines = stream.Commit(pending.ToList(), session.Current, c.AtMs);
                pending.Clear();
                var i = Enumerable.Range(0, c.Lines.Count > lines.Length ? c.Lines.Count : lines.Length)
                    .Cast<int?>()
                    .FirstOrDefault(n => n >= lines.Length || n >= c.Lines.Count || lines[n!.Value] != c.Lines[n.Value]);
                if (i is { } at)
                {
                    var exp = at < c.Lines.Count ? c.Lines[at] : "<none>";
                    var got = at < lines.Length ? lines[at] : "<none>";
                    failures.Add($"journey step {c.JourneyStep}: pose line #{at} differs ({c.Lines.Count} recorded, {lines.Length} replayed)\n    recorded: {exp}\n    replayed: {got}");
                }
            }
        }

        Flush(0);
        foreach (var (applied, s, events) in ReplayCheck.Replay(fresh, log))
        {
            session = s;
            _ = events.Select(text.Line).ToList();
            pending.AddRange(events);
            Flush(applied);
        }

        failures.AddRange(commits.Skip(next)
            .Select(c => $"journey step {c.JourneyStep}: boundary at intent {c.IntentCount} but the log has only {log.Count}"));
        return failures;
    }
}
