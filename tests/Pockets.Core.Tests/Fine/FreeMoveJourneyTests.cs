using Pockets.Core.Models;
using Pockets.Core.Rendering;
using Pockets.Core.Sim;

namespace Pockets.Core.Tests.Fine;

/// <summary>
/// Slice 2.8 on <c>journeys/free-move.journey.json</c>. The L1-projection test ("L1 only ever sees <c>Move</c>", A1):
/// the Moves the integrator derived during the D2-B run, replayed on a plain D2-A session with no fine state at all,
/// give the identical L1 — the same view-model and the same event lines after every intent. Plus the offsets-only fuzz
/// of the journey (the bodies keep steering, so only the offsets can be fuzzed without changing what is derived).
/// </summary>
public class FreeMoveJourneyTests
{
    private const string Journey = "free-move.journey.json";

    /// <summary>The recorded intent log of the journey, driven through the debug protocol (the mock-godot path).</summary>
    private static IReadOnlyList<(int Step, Intent Intent)> RecordLog()
    {
        var controller = new GameController(JourneyCommands.NewDemoSession());
        JourneyCommands.Run(controller, Journey);
        return controller.Session.IntentLog;
    }

    /// <summary>One L1 observation per group of applied intents: the VM text and the group's event lines.</summary>
    private sealed record Group(string Origin, string ViewModel, List<string> Events);

    [Fact]
    public void TheDerivedMoves_ReplayedOnAPlainD2ASession_GiveIdenticalL1()
    {
        var log = RecordLog();

        // D2-B: replay the real log through the shell; note what each FineTick derived.
        var bText = new EventText();
        var b = new List<Group>();
        var derived = new List<IReadOnlyList<Intent>>();
        foreach (var (applied, session, events) in ReplayCheck.Replay(JourneyCommands.NewDemoSession(), log))
        {
            var intent = log[applied - 1].Intent;
            b.Add(new Group(intent.ToString()!, ViewModelSerializer.Serialize(session).ToJsonString(), events.Select(bText.Line).ToList()));
            derived.Add(intent is Intent.FineTick
                ? events.OfType<SimEvent.IntentApplied>().Select(e => e.Intent).ToList()
                : []);
        }

        // D2-A: the same log with every FineTick replaced by its derived Moves and every Steer dropped.
        var aText = new EventText();
        var a = new List<Group>();
        var plain = JourneyCommands.NewDemoSession();
        for (var k = 0; k < log.Count; k++)
        {
            var intent = log[k].Intent;
            var run = intent switch
            {
                Intent.FineTick => derived[k],
                Intent.Steer => [],
                _ => (IReadOnlyList<Intent>)[intent]
            };
            var events = new List<string>();
            foreach (var i in run)
            {
                var (next, _, ev) = plain.ApplyTraced(i);
                plain = next;
                events.AddRange(ev.Select(aText.Line));
            }
            Assert.True(plain.Fine.IsEmpty, $"intent {k}: the D2-A session grew fine state");
            a.Add(new Group(intent.ToString()!, ViewModelSerializer.Serialize(plain).ToJsonString(), events));
        }

        // Not vacuous: the run derived crossings (one tick with two, X then Y), a refused one, and steered.
        var moves = derived.SelectMany(d => d).ToList();
        Assert.All(moves, m => Assert.IsType<Intent.Move>(m));
        Assert.True(moves.Count >= 7, $"only {moves.Count} derived moves");
        Assert.Contains(derived, d => d.Count == 2);
        Assert.Contains(b, g => g.Origin.StartsWith("FineTick") && g.Events.Any(e => e.Contains(" Bumped ")));
        Assert.Contains(log, e => e.Intent is Intent.Undo);

        var diff = Enumerable.Range(0, log.Count)
            .Where(k => a[k].ViewModel != b[k].ViewModel || !a[k].Events.SequenceEqual(b[k].Events))
            .Select(k => $"intent {k} {b[k].Origin}:\n  D2-B {string.Join(" | ", b[k].Events)}\n  D2-A {string.Join(" | ", a[k].Events)}"
                         + (a[k].ViewModel != b[k].ViewModel ? "\n  (view-models differ)" : ""))
            .FirstOrDefault();
        Assert.True(diff is null, diff);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void OffsetsOnlyFuzz_AfterEveryStep_LeavesL1Identical(int seed)
    {
        var plain = JourneyCommands.Run(new GameController(JourneyCommands.NewDemoSession()), Journey);
        var fuzzed = JourneyCommands.Run(new GameController(JourneyCommands.NewDemoSession()), Journey,
            (c, next) => c.FuzzFine(seed, next, bodies: false));
        Assert.Null(JourneyCommands.FirstDifference(plain, fuzzed));
    }

    /// <summary>Why the full fuzz is not run on this journey: fuzzing a steered body changes which Moves it derives.</summary>
    [Fact]
    public void FullFuzz_OfASteeredBody_DoesChangeL1_ByDesign()
    {
        var plain = JourneyCommands.Run(new GameController(JourneyCommands.NewDemoSession()), Journey);
        var fuzzed = JourneyCommands.Run(new GameController(JourneyCommands.NewDemoSession()), Journey, (c, next) => c.FuzzFine(1, next));
        Assert.NotNull(JourneyCommands.FirstDifference(plain, fuzzed));
    }
}
