namespace Pockets.Core.Cosmology;

/// <summary>
/// A cardinal reading direction on the glyph plane. The screen convention is
/// x-right / y-down (matching SVG), so <see cref="Down"/> is +y and
/// <see cref="Up"/> is -y.
/// </summary>
public enum Direction
{
    Right,
    Left,
    Down,
    Up
}

/// <summary>
/// Helpers mapping <see cref="Direction"/> to and from unit vectors in the
/// x-right / y-down plane. Used to derive a glyph's reading label from its
/// <see cref="Orientation"/> and vice-versa.
/// </summary>
public static class Directions
{
    /// <summary>Unit vector for a direction, in x-right / y-down screen space.</summary>
    public static (int X, int Y) ToVector(this Direction d) => d switch
    {
        Direction.Right => (1, 0),
        Direction.Left => (-1, 0),
        Direction.Down => (0, 1),
        Direction.Up => (0, -1),
        _ => throw new ArgumentOutOfRangeException(nameof(d))
    };

    /// <summary>The direction of an axis-aligned unit vector.</summary>
    public static Direction FromVector(int x, int y) => (x, y) switch
    {
        (1, 0) => Direction.Right,
        (-1, 0) => Direction.Left,
        (0, 1) => Direction.Down,
        (0, -1) => Direction.Up,
        _ => throw new ArgumentException($"Not an axis-aligned unit vector: ({x},{y})")
    };
}
