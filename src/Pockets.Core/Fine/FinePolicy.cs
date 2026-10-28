namespace Pockets.Core.Fine;

/// <summary>
/// The integrator's tuning (P2-D8): speeds in 1/16-cell units per tick and the tick rate. Ints only.
/// </summary>
/// <param name="OrthogonalSpeed">Speed per tick on the axis of a held N, E, S or W.</param>
/// <param name="DiagonalSpeed">Speed per tick on each axis of a held diagonal.</param>
/// <param name="SettleSpeed">Speed per tick at which a released axis settles back to the cell centre.</param>
/// <param name="TicksPerSecond">Integrator ticks per second.</param>
public sealed record FinePolicy(int OrthogonalSpeed, int DiagonalSpeed, int SettleSpeed, int TicksPerSecond)
{
    /// <summary>A cell crossing happens when a position would pass this magnitude.</summary>
    public const int Half = 8;

    /// <summary>The magnitude a position clamps to at a refusal or the outer grid edge.</summary>
    public const int Clamp = 7;

    /// <summary>The ratified table: 4 orthogonal, 3 diagonal, 4 settle, 8 ticks per second.</summary>
    public static FinePolicy Default { get; } = new(4, 3, 4, 8);

    /// <summary>The length of one tick in whole milliseconds.</summary>
    public int TickMs => 1000 / TicksPerSecond;
}
