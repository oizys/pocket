using Pockets.Core.Models;

namespace Pockets.Core.Tests.Models;

public class CursorTests
{
    private const int Rows = 4;
    private const int Cols = 8;

    [Fact]
    public void Move_Right_IncrementsCol()
    {
        var cursor = new Cursor(new Position(0, 0));
        var moved = cursor.Move(Direction.Right, Rows, Cols);
        Assert.Equal(new Position(0, 1), moved.Position);
    }

    [Fact]
    public void Move_Left_DecrementsCol()
    {
        var cursor = new Cursor(new Position(0, 3));
        var moved = cursor.Move(Direction.Left, Rows, Cols);
        Assert.Equal(new Position(0, 2), moved.Position);
    }

    [Fact]
    public void Move_Down_IncrementsRow()
    {
        var cursor = new Cursor(new Position(0, 0));
        var moved = cursor.Move(Direction.Down, Rows, Cols);
        Assert.Equal(new Position(1, 0), moved.Position);
    }

    [Fact]
    public void Move_Up_DecrementsRow()
    {
        var cursor = new Cursor(new Position(2, 0));
        var moved = cursor.Move(Direction.Up, Rows, Cols);
        Assert.Equal(new Position(1, 0), moved.Position);
    }

    [Fact]
    public void Move_Right_WrapsAround()
    {
        var cursor = new Cursor(new Position(0, Cols - 1));
        var moved = cursor.Move(Direction.Right, Rows, Cols);
        Assert.Equal(new Position(0, 0), moved.Position);
    }

    [Fact]
    public void Move_Left_WrapsAround()
    {
        var cursor = new Cursor(new Position(0, 0));
        var moved = cursor.Move(Direction.Left, Rows, Cols);
        Assert.Equal(new Position(0, Cols - 1), moved.Position);
    }

    [Fact]
    public void Move_Down_WrapsAround()
    {
        var cursor = new Cursor(new Position(Rows - 1, 0));
        var moved = cursor.Move(Direction.Down, Rows, Cols);
        Assert.Equal(new Position(0, 0), moved.Position);
    }

    [Fact]
    public void Move_Up_WrapsAround()
    {
        var cursor = new Cursor(new Position(0, 0));
        var moved = cursor.Move(Direction.Up, Rows, Cols);
        Assert.Equal(new Position(Rows - 1, 0), moved.Position);
    }

    [Fact]
    public void Move_ReturnsNewInstance_OriginalUnchanged()
    {
        var cursor = new Cursor(new Position(1, 1));
        var moved = cursor.Move(Direction.Right, Rows, Cols);
        Assert.Equal(new Position(1, 1), cursor.Position);
        Assert.Equal(new Position(1, 2), moved.Position);
    }
}
