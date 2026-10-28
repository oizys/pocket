using System.Collections.Immutable;
using Pockets.Core.Data;
using Pockets.Core.Models;
using Pockets.Core.Sim;

namespace Pockets.Core.Tests.Sim;

/// <summary>
/// Slice 1.8 acceptance 2: ten minutes of simulated realtime on the demo profile keep the event log under
/// its 8 MB cap, and the log adds next to nothing to the steady-state allocation rate (so gen0 GC stays at
/// today's level). The demo is driven as the TUI drives it — a clock sync every 100 ms — with a player
/// action every few seconds, and runs with a crafting Workbench so every second is a real step.
/// </summary>
public class EventLogSoakTests(Xunit.Abstractions.ITestOutputHelper output)
{
    private static readonly TimeSpan Frame = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan SoakLength = TimeSpan.FromMinutes(10);

    /// <summary>The demo profile in realtime mode, as a fresh controller on a virtual clock.</summary>
    private static GameController DemoRealtime()
    {
        var registry = ContentLoader.LoadFromDirectory(TestPaths.DataDir);
        var session = GameInitializer.CreateDemoProfile(registry).NewSession() with { TickMode = TickMode.Realtime };
        return new GameController(session);
    }

    /// <summary>Runs <paramref name="frames"/> frames: a clock sync each, a cursor move and a primary every 3 s.</summary>
    private static void Drive(GameController controller, int frames)
    {
        var keys = new[] { GameKey.Right, GameKey.Primary, GameKey.Down, GameKey.Primary, GameKey.Left };
        for (var f = 0; f < frames; f++)
        {
            controller.AdvanceClock(Frame);
            if (f % 30 == 0)
                controller.HandleKey(keys[f / 30 % keys.Length]);
        }
    }

    [Fact]
    public void TenMinutesOfRealtime_StaysUnderTheCap()
    {
        var controller = DemoRealtime();
        var frames = (int)(SoakLength / Frame);

        Drive(controller, frames);

        var session = controller.Session;
        var log = session.EventLog;
        output.WriteLine($"steps={session.TickCount} intents={session.IntentLog.Count} events={log.End} retained={log.Count} " +
                         $"bytes={log.Bytes} checkpoints={session.Checkpoints.Count} undo={session.UndoDepth}");
        Assert.Equal(frames / 10, (int)session.Elapsed.TotalSeconds);
        Assert.True(log.End > frames, "every frame's clock sync should have logged an event");
        Assert.True(log.Bytes <= EventLogPolicy.DefaultMaxBytes);
        Assert.Null(log.Truncated);
    }

    /// <summary>
    /// The log's own steady-state cost: appending a step's events allocates only its share of a chunk (two
    /// words a slot) and one small view object, never a copy. A step today already allocates its events and
    /// several session copies, so this keeps the allocation rate — and gen0 count — at today's level.
    /// </summary>
    [Fact]
    public void Append_SteadyState_AllocatesOnlyChunkSlotsAndAView()
    {
        var step = ImmutableArray.Create<SimEvent>(
            new SimEvent.IntentApplied(1, 0, new Intent.Tick()), new SimEvent.TimeAdvanced(1, 1),
            new SimEvent.CraftProgressed(1, 2, Guid.Empty, "r", 3, null));
        var policy = EventLogPolicy.Default;
        var log = EventLog.Empty;
        for (var i = 0; i < 5_000; i++)
            log = log.Append(step, policy);

        const int steps = 50_000;
        var gen0 = GC.CollectionCount(0);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < steps; i++)
            log = log.Append(step, policy);
        var perStep = (GC.GetAllocatedBytesForCurrentThread() - before) / (double)steps;

        output.WriteLine($"bytes/step={perStep:F1} gen0={GC.CollectionCount(0) - gen0} retained={log.Count}");
        // Per event: an 8-byte slot and an 8-byte size word (+ amortized chunk headers); per step: one view.
        Assert.True(perStep <= step.Length * 16 + 64 + 8, $"log append allocated {perStep:F1} bytes/step");
    }
}
