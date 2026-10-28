namespace Pockets.Core.Sim;

/// <summary>
/// How the scheduler steps a bag (two-layer #23, §Time). No zero member, like <see cref="TimeClass"/>.
/// </summary>
public enum BagClockMode
{
    /// <summary>On screen or the player is inside: every rule at full rate.</summary>
    Observed = 1,

    /// <summary>Explored but not visible: rate 1/k, sub-stepped.</summary>
    Background = 2,

    /// <summary>Closed or far: nothing runs; catches up on observe.</summary>
    Dormant = 3,

    /// <summary>A stasis bag: nothing runs, ever.</summary>
    Frozen = 4
}

/// <summary>
/// A bag's scheduler clock: its <see cref="Rate"/> and <see cref="Mode"/> (Slice 1.4). Every bag is
/// <see cref="Observed"/> at rate 1 in Phase 1 and nothing reads it yet; the Phase 4 scheduler does.
/// </summary>
public sealed record BagClock(int Rate, BagClockMode Mode)
{
    /// <summary>The Phase 1 clock of every bag: rate 1, observed.</summary>
    public static BagClock Observed { get; } = new(1, BagClockMode.Observed);
}
