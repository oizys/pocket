using Pockets.Core.Models;

namespace Pockets.Realization;

/// <summary>
/// Quantizes raw steering input to the eight held directions (D2-B), integers only. A frontend feeds the result to
/// the controller's steer call whenever it changes.
/// </summary>
public static class SteerInput
{
    /// <summary>The sign of <paramref name="v"/>: -1, 0 or 1.</summary>
    private static int Sign(int v) => v > 0 ? 1 : v < 0 ? -1 : 0;

    /// <summary>The magnitude of <paramref name="v"/>, widened so the extreme int is safe.</summary>
    private static long Abs(long v) => v < 0 ? -v : v;

    /// <summary>
    /// The direction whose screen-axis components (y down) are the signs of <paramref name="x"/> and <paramref name="y"/>;
    /// null when both are zero.
    /// </summary>
    public static Direction8? FromComponents(int x, int y) => (Sign(x), Sign(y)) switch
    {
        (0, -1) => Direction8.N,
        (1, -1) => Direction8.NE,
        (1, 0) => Direction8.E,
        (1, 1) => Direction8.SE,
        (0, 1) => Direction8.S,
        (-1, 1) => Direction8.SW,
        (-1, 0) => Direction8.W,
        (-1, -1) => Direction8.NW,
        _ => null
    };

    /// <summary>The direction held by four arrow keys; opposite keys cancel.</summary>
    public static Direction8? FromArrows(bool up, bool down, bool left, bool right) =>
        FromComponents((right ? 1 : 0) - (left ? 1 : 0), (down ? 1 : 0) - (up ? 1 : 0));

    /// <summary>
    /// The direction of a stick at (<paramref name="x"/>, <paramref name="y"/>), y down, in any integer unit (Godot passes
    /// milli-units); null inside the dead zone. An axis counts only when it exceeds tan 22.5 degrees (414/1000) of the
    /// other, so the plane splits into eight equal sectors.
    /// </summary>
    public static Direction8? FromStick(int x, int y, int deadZone)
    {
        long lx = x;
        long ly = y;
        if (lx * lx + ly * ly < (long)deadZone * deadZone) return null;
        var ax = Abs(lx);
        var ay = Abs(ly);
        if (ay * 1000 <= ax * 414) return FromComponents(Sign(x), 0);
        if (ax * 1000 <= ay * 414) return FromComponents(0, Sign(y));
        return FromComponents(Sign(x), Sign(y));
    }
}
