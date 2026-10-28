using System.Globalization;
using Pockets.Core.Models;
using Pockets.Core.Rendering;

namespace Pockets.Core.Sim;

/// <summary>
/// The serialized form of the event golden (Slice 1.7). <c>(Step, Seq)</c> is not unique across a journey
/// step — untimed intents share a step number with the next timed one and every <see cref="Simulation.Step"/>
/// restarts <c>Seq</c> at 0 — so each golden line is prefixed with the journey step and the 0-based ordinal of
/// the Simulation.Step within it: <c>{journeyStep}:{k} {canonical event line}</c>. Step and Seq themselves are
/// never renumbered (contract). The prefix is derived from the lines alone, so every driver (including a live
/// Godot over the wire) produces it identically.
/// </summary>
public static class EventGolden
{
    /// <summary>
    /// Prefixes each canonical <see cref="EventText"/> line with <c>{journeyStep}:{k}</c>, where k starts at
    /// -1 and advances whenever a line's seq (its second space-separated token) is "0", clamped so the first
    /// line is k = 0 even if its seq is not 0. Invariant culture.
    /// </summary>
    public static IReadOnlyList<string> Lines(int journeyStep, IEnumerable<string> eventLines)
    {
        var k = -1;
        var prefix = journeyStep.ToString(CultureInfo.InvariantCulture);
        return eventLines.Select(line =>
        {
            var seq = line.Split(' ', 3).ElementAtOrDefault(1);
            if (seq == "0" || k < 0)
                k++;
            return $"{prefix}:{k.ToString(CultureInfo.InvariantCulture)} {line}";
        }).ToList();
    }
}

/// <summary>
/// One recorded observation: journey step index (-1 = start), <see cref="GameSession.IntentLog"/> count at that
/// moment, the view-model JSON text, and the raw (unprefixed) EventText lines emitted since the previous boundary.
/// </summary>
public sealed record ReplayBoundary(int JourneyStep, int IntentCount, string ViewModel, IReadOnlyList<string> Events);

/// <summary>
/// The replay gate (Slice 1.7; A6/G6 in its Phase 1 form): re-applies a recorded intent log to a fresh session
/// and checks that the view-model and the event stream at every recorded boundary are identical.
/// </summary>
public static class ReplayCheck
{
    /// <summary>
    /// Replays <paramref name="log"/> from <paramref name="fresh"/> via <see cref="GameSession.ApplyTraced"/>
    /// (one <see cref="EventText"/> names the whole replay) and returns the failures, empty when all is
    /// identical: per boundary, a view-model mismatch or a differing event stream since the previous
    /// boundary; plus a differing replayed IntentLog, and any boundary past the end of the log.
    /// </summary>
    public static IReadOnlyList<string> Run(GameSession fresh,
        IReadOnlyList<(int Step, Intent Intent)> log, IReadOnlyList<ReplayBoundary> boundaries)
    {
        var failures = new List<string>();
        var text = new EventText();
        var session = fresh;
        var pending = new List<string>();
        var next = 0;

        void CheckBoundaries(int applied)
        {
            while (next < boundaries.Count && boundaries[next].IntentCount == applied)
            {
                var b = boundaries[next++];
                var vm = ViewModelSerializer.Serialize(session).ToJsonString();
                if (vm != b.ViewModel)
                    failures.Add($"journey step {b.JourneyStep}: view-model differs; {Diff(b.ViewModel, vm)}");
                var events = pending.ToList();
                pending.Clear();
                var i = Enumerable.Range(0, Math.Max(events.Count, b.Events.Count))
                    .Cast<int?>()
                    .FirstOrDefault(n => n >= events.Count || n >= b.Events.Count || events[n!.Value] != b.Events[n.Value]);
                if (i is { } at)
                {
                    var exp = at < b.Events.Count ? b.Events[at] : "<none>";
                    var got = at < events.Count ? events[at] : "<none>";
                    failures.Add($"journey step {b.JourneyStep}: event #{at} differs ({b.Events.Count} recorded, {events.Count} replayed)\n    recorded: {exp}\n    replayed: {got}");
                }
            }
        }

        CheckBoundaries(0);
        for (var n = 0; n < log.Count; n++)
        {
            var (s, _, events) = session.ApplyTraced(log[n].Intent);
            session = s;
            pending.AddRange(events.Select(text.Line));
            CheckBoundaries(n + 1);
        }

        failures.AddRange(boundaries.Skip(next)
            .Select(b => $"journey step {b.JourneyStep}: boundary at intent {b.IntentCount} but the log has only {log.Count}"));

        if (session.IntentLog.Count != log.Count)
            failures.Add($"IntentLog count differs: recorded {log.Count}, replayed {session.IntentLog.Count}");
        else
            failures.AddRange(Enumerable.Range(0, log.Count)
                .Where(k => log[k].Step != session.IntentLog[k].Step || !Equals(log[k].Intent, session.IntentLog[k].Intent))
                .Select(k => $"IntentLog #{k}: recorded ({log[k].Step}, {log[k].Intent}) vs replayed ({session.IntentLog[k].Step}, {session.IntentLog[k].Intent})"));

        return failures;
    }

    /// <summary>A compact excerpt of both strings around the first differing character.</summary>
    private static string Diff(string expected, string actual)
    {
        var n = Math.Min(expected.Length, actual.Length);
        var idx = Enumerable.Range(0, n).FirstOrDefault(i => expected[i] != actual[i], n);
        var start = Math.Max(0, idx - 30);
        string Slice(string s) => s.Substring(Math.Min(start, s.Length), Math.Min(70, Math.Max(0, s.Length - start)));
        return $"at char {idx}: recorded ...{Slice(expected)}... replayed ...{Slice(actual)}...";
    }
}
