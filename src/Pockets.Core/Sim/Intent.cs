using Pockets.Core.Models;

namespace Pockets.Core.Sim;

/// <summary>
/// A player or scheduler input to <c>Sim.Step</c> (two-layer #23, Step contract: intents are the only
/// input). A closed hierarchy: the constructor is private, so every case is a sealed record nested
/// here. Slice 1.0 declared <see cref="Tick"/>; Slice 1.2 adds the transitional <see cref="Tool"/>;
/// Slice 1.1 adds <see cref="AcquireRandom"/>, the first data-only player intent;
/// Slice 1.3 replaces <see cref="Tool"/> with one data-only case per controller path, each carrying
/// its target <c>LocationId</c> so replay never depends on panel focus (P1-D5).
/// </summary>
public abstract record Intent
{
    private Intent() { }

    /// <summary>
    /// Advance simulated time by one step with no player action (realtime clock tick,
    /// <c>TickLengthMs = 1000</c> per P1-D4).
    /// </summary>
    public sealed record Tick : Intent;

    /// <summary>
    /// <b>Transitional (Slice 1.2; removed in 1.3).</b> A player tool action as the session shell runs
    /// it today: <paramref name="Apply"/> is the tool (or a precomputed <see cref="ToolResult"/>)
    /// applied to the pre-step state, and <paramref name="AdvancesTime"/> is the explicit time flag
    /// (true in rogue mode, false for realtime player actions, P1-D2). It carries a closure, so it is
    /// not replayable data and compares by delegate reference; 1.3's per-path intents are.
    /// </summary>
    public sealed record Tool(Func<GameState, ToolResult> Apply, bool AdvancesTime) : Intent;

    /// <summary>
    /// The debug AcquireRandom tool (key 5): acquire one item of a random type into the active bag. The
    /// type is drawn inside <c>Step</c> from the state's <see cref="SimRng"/> (<see cref="SimStream.Acquire"/>),
    /// so the intent is plain data and replays exactly (Slice 1.1). <paramref name="AdvancesTime"/> is the
    /// same explicit time flag as <see cref="Tool"/> (P1-D2). Slice 1.3 adds the target location.
    /// </summary>
    public sealed record AcquireRandom(bool AdvancesTime) : Intent;
}
