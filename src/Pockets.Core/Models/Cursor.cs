namespace Pockets.Core.Models;

/// <summary>
/// Points to the active cell within a grid. Movement wraps around edges.
/// </summary>
public record Cursor(Position Position)
{
    /// <summary>
    /// Returns a new Cursor moved one step in the given direction, wrapping at grid boundaries.
    /// </summary>
    public Cursor Move(Direction direction, int rows, int cols)
    {
        var (row, col) = Position;
        var newPos = direction switch
        {
            Direction.Up    => new Position((row - 1 + rows) % rows, col),
            Direction.Down  => new Position((row + 1) % rows, col),
            Direction.Left  => new Position(row, (col - 1 + cols) % cols),
            Direction.Right => new Position(row, (col + 1) % cols),
            _ => Position
        };
        return this with { Position = newPos };
    }
}
