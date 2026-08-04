using System.Diagnostics;

namespace Pockets.Core.Models;

/// <summary>
/// The injectable game clock (Slice 5): a virtualized time source that decouples in-game elapsed
/// time from the wall clock. The demo started in "realtime mode" — the clock was always running —
/// but for the parity harness that would be nondeterministic, so time is a <b>dependency</b>:
/// <list type="bullet">
///   <item>Live frontends inject a <see cref="SystemGameClock"/> and periodically sync it, so the
///   readout advances against the wall clock.</item>
///   <item>The parity harness (and every controller by default) uses a <see cref="VirtualGameClock"/>
///   advanced ONLY by scripted steps (the journey's <c>advanceTime</c>), so checkpoints are
///   deterministic — parity NEVER reads the wall clock.</item>
/// </list>
/// The controller syncs <see cref="Elapsed"/> onto <see cref="GameSession.Elapsed"/>, the value the
/// view-model serializes for the clock readout.
/// </summary>
public interface IGameClock
{
    /// <summary>Accumulated in-game time so far.</summary>
    TimeSpan Elapsed { get; }

    /// <summary>Moves the clock forward by <paramref name="delta"/> (scripted advance).</summary>
    void Advance(TimeSpan delta);
}

/// <summary>
/// A fully-scripted clock: <see cref="Elapsed"/> moves only when <see cref="Advance"/> is called.
/// The default for every controller and the ONLY clock the parity harness uses, so harness time is
/// never taken from the wall clock. Not thread-safe (single-threaded game loop).
/// </summary>
public sealed class VirtualGameClock : IGameClock
{
    public TimeSpan Elapsed { get; private set; }

    public VirtualGameClock(TimeSpan? start = null) => Elapsed = start ?? TimeSpan.Zero;

    /// <summary>Advances by a non-negative delta (negative deltas are ignored — time is monotonic).</summary>
    public void Advance(TimeSpan delta)
    {
        if (delta > TimeSpan.Zero)
            Elapsed += delta;
    }
}

/// <summary>
/// A wall-clock-backed clock for live frontends: <see cref="Elapsed"/> tracks real time since
/// construction. Live builds inject this and sync it into the session each refresh, so the demo's
/// clock readout ticks in real time. Never used by the parity harness (it would be nondeterministic).
/// <see cref="Advance"/> is a no-op — real time drives this clock, not scripted steps.
/// </summary>
public sealed class SystemGameClock : IGameClock
{
    private readonly Stopwatch _stopwatch = Stopwatch.StartNew();

    public TimeSpan Elapsed => _stopwatch.Elapsed;

    public void Advance(TimeSpan delta) { /* wall-driven: scripted advance does not apply */ }
}
