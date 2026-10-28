namespace Pockets.Core.Models;

/// <summary>
/// How a world bag's body moves (P2-D12): the per-bag D2-A / D2-B flag. L1 never reads it; only the session
/// shell's fine path honours it, and only in realtime on a world bag. No zero member.
/// </summary>
public enum MovementModel
{
    /// <summary>D2-A: the body steps cell to cell with the arrow keys (the default).</summary>
    GridLocked = 1,

    /// <summary>D2-B: the body glides between cells under a held direction, integrated by the shell.</summary>
    Free
}
