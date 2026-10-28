using Pockets.Core.Models;
using Pockets.Core.Sim;

namespace Pockets.Realization.Tests;

/// <summary>Exact-string tests of the zoomed-ASCII harness.</summary>
public class ZoomAsciiTests
{
    private static readonly ItemType Iron = new("Iron Ore", Category.Material, true);

    /// <summary>A 3×1 bag with iron ore in the middle cell.</summary>
    private static Bag Strip() =>
        new Bag(Grid.Create(3, 1).SetCell(1, new Cell(new ItemStack(Iron, 3))));

    /// <summary>A pose of a body in <paramref name="bag"/>.</summary>
    private static Pose Body(Bag bag, int col, int row, int sx = 0, int sy = 0) =>
        new(new EntityKey.Body(LocationId.B), bag.Id, col, row, sx, sy, null, Facing.S);

    /// <summary>An empty bag draws only background and separators.</summary>
    [Fact]
    public void EmptyBag_DrawsDots()
    {
        var bag = new Bag(Grid.Create(2, 1));
        Assert.Equal("....|....\n....|....\n....|....\n....|....", ZoomAscii.Render([], bag));
    }

    /// <summary>Two grid rows are separated by a dash line.</summary>
    [Fact]
    public void TwoRows_AreSeparated()
    {
        var bag = new Bag(Grid.Create(2, 2));
        Assert.Equal("..|..\n..|..\n--+--\n..|..\n..|..", ZoomAscii.Render([], bag, 2));
    }

    /// <summary>A centred body lands on sub index 2 at n = 4.</summary>
    [Fact]
    public void Body_Alone_IsAtSubIndexTwo()
    {
        var bag = new Bag(Grid.Create(2, 1));
        Assert.Equal("....|....\n....|....\n..@.|....\n....|....",
            ZoomAscii.Render([Body(bag, 0, 0)], bag));
    }

    /// <summary>A centred stack draws its item glyph.</summary>
    [Fact]
    public void Stack_DrawsItemGlyph()
    {
        var bag = Strip();
        var pose = new Pose(new EntityKey.Stack(new CellRef(bag.Id, 1)), bag.Id, 1, 0, 0, 0, null, null);
        Assert.Equal("....|....|....\n....|....|....\n....|..I.|....\n....|....|....",
            ZoomAscii.Render([pose], bag));
    }

    /// <summary>SubX = +8 at col 1 lands on the first sub-column of col 2 (the two-layer.md boundary example).</summary>
    [Fact]
    public void Body_AtSubXPlusEight_SitsOnNextCellBoundary()
    {
        var bag = Strip();
        Assert.Equal("....|....|....\n....|....|....\n....|....|@...\n....|....|....",
            ZoomAscii.Render([Body(bag, 1, 0, 8, 0)], bag));
    }

    /// <summary>A body draws on top of a stack in the same sub-cell; other bags' poses and off-grid poses are skipped.</summary>
    [Fact]
    public void Body_OverStack_AndForeignAndOffGrid_Skipped()
    {
        var bag = Strip();
        var stack = new Pose(new EntityKey.Stack(new CellRef(bag.Id, 1)), bag.Id, 1, 0, 0, 0, null, null);
        var foreign = Body(new Bag(Grid.Create(1, 1)), 0, 0);
        var off = Body(bag, 5, 0);
        Assert.Equal("....|....|....\n....|....|....\n....|..@.|....\n....|....|....",
            ZoomAscii.Render([Body(bag, 1, 0), stack, foreign, off], bag));
    }
}
