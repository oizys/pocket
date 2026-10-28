namespace Pockets.Core.Sim;

/// <summary>
/// A player or scheduler input to <c>Sim.Step</c> (two-layer #23, Step contract: intents are the only
/// input). A closed hierarchy: the constructor is private, so every case is a sealed record nested
/// here. Slice 1.0 declares only <see cref="Tick"/>; Slice 1.3 adds one case per controller path,
/// each carrying its target <c>LocationId</c> so replay never depends on panel focus (P1-D5).
/// </summary>
public abstract record Intent
{
    private Intent() { }

    /// <summary>
    /// Advance simulated time by one step with no player action (realtime clock tick,
    /// <c>TickLengthMs = 1000</c> per P1-D4).
    /// </summary>
    public sealed record Tick : Intent;
}
