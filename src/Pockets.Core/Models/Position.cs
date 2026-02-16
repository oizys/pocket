namespace Pockets.Core.Models;

/// <summary>
/// A row/column position within a grid. Value type for efficient cursor operations.
/// Row-major: on a 10-column grid, index 12 = row 1, col 2.
/// </summary>
public readonly record struct Position(int Row, int Col)
{
    /// <summary>
    /// Converts this position to a flat cell index given the grid's column count.
    /// </summary>
    public int ToIndex(int columns) => Row * columns + Col;

    /// <summary>
    /// Creates a Position from a flat cell index and the grid's column count.
    /// </summary>
    public static Position FromIndex(int index, int columns) =>
        new(index / columns, index % columns);
}
