using System.Collections.Immutable;
using Pockets.Core.Models;
using Pockets.Core.Sim;

namespace Pockets.Realization.Tests;

/// <summary>
/// Slice 2.5: the 2D projection of A2 that the Godot grid draws, in integer pixels, and the selection of what a
/// grid panel draws (the poses in its bag, in draw order, and where its cursor highlight goes).
/// </summary>
public class PoseProjectionTests
{
    private static readonly Guid BagA = Guid.NewGuid();
    private static readonly Guid BagB = Guid.NewGuid();

    private static Pose StackPose(Guid bag, int index, int col, int row, int sx = 0, int sy = 0, int? h = null) =>
        new(new EntityKey.Stack(new CellRef(bag, index)), bag, col, row, sx, sy, h, null);

    private static DrawnPose AtRest(Pose p) => new(p, p.Entity);

    // ==================== TileOrigin ====================

    /// <summary>At rest a tile sits exactly on its cell: (col·w, row·h).</summary>
    [Fact]
    public void TileOrigin_AtRest_IsTheCellCorner()
    {
        Assert.Equal((240, 112), PoseProjection.TileOrigin(StackPose(BagA, 0, 3, 2), 80, 56));
    }

    /// <summary>Sub-units move the tile by Sub/16 of a cell, both signs.</summary>
    [Fact]
    public void TileOrigin_SubOffsets_AreSixteenthsOfACell()
    {
        Assert.Equal((80 + 40, 56 - 14), PoseProjection.TileOrigin(StackPose(BagA, 0, 1, 1, 8, -4), 80, 56));
        Assert.Equal((80 - 5, 56 + 3), PoseProjection.TileOrigin(StackPose(BagA, 0, 1, 1, -1, 1), 80, 56));
    }

    /// <summary>Height lifts the tile: a −y offset of Height/16 cell heights. Null height is no lift.</summary>
    [Fact]
    public void TileOrigin_Height_IsAMinusYOffset()
    {
        Assert.Equal((0, -21), PoseProjection.TileOrigin(StackPose(BagA, 0, 0, 0, h: 6), 80, 56));
        Assert.Equal((0, 0), PoseProjection.TileOrigin(StackPose(BagA, 0, 0, 0, h: null), 80, 56));
    }

    // ==================== InBag / draw order ====================

    /// <summary>A panel draws only the poses in its bag; the stationary ones first, then the moving ones on top.</summary>
    [Fact]
    public void ForPanel_FiltersByBag_StationaryFirst()
    {
        var moving = new DrawnPose(StackPose(BagA, 0, 0, 0, sx: 5), new EntityKey.Stack(new CellRef(BagA, 1)));
        var still = AtRest(StackPose(BagA, 2, 2, 0));
        var other = AtRest(StackPose(BagB, 0, 0, 0));
        var lifted = AtRest(StackPose(BagA, 3, 3, 0, h: 2));

        var drawn = PoseProjection.ForPanel([moving, other, still, lifted], BagA, 4);

        Assert.Equal(new[] { still, moving, lifted }, drawn);
    }

    /// <summary>The content of a drawn stack is the stack in the rest key's cell; non-stacks have none.</summary>
    [Fact]
    public void ContentCell_IsTheRestKeysIndex()
    {
        var flying = new DrawnPose(StackPose(BagA, 1, 1, 0, sx: 7), new EntityKey.Stack(new CellRef(BagA, 6)));
        Assert.Equal(6, PoseProjection.ContentCell(flying));
        var body = AtRest(new Pose(new EntityKey.Body(LocationId.B), BagA, 0, 0, 0, 0, null, Facing.S));
        Assert.Null(PoseProjection.ContentCell(body));
    }

    // ==================== Cursor ====================

    /// <summary>In a world bag the cursor highlight follows the body pose of the location.</summary>
    [Fact]
    public void CursorOf_WorldBag_FollowsTheBody()
    {
        var body = new Pose(new EntityKey.Body(LocationId.B), BagA, 1, 0, 6, 0, null, Facing.E);
        var other = new Pose(new EntityKey.Body(LocationId.B), BagB, 4, 4, 0, 0, null, Facing.E);
        Assert.Equal(body, PoseProjection.CursorOf([AtRest(other), AtRest(body)], LocationId.B, BagA));
    }

    /// <summary>In an inventory bag the highlight follows a live cursor glide; with none it is null (draw the grid cursor).</summary>
    [Fact]
    public void CursorOf_InventoryBag_FollowsTheGlideElseNull()
    {
        var glide = new Pose(new EntityKey.Cursor(LocationId.B), BagA, 2, 1, -3, 0, null, null);
        Assert.Equal(glide, PoseProjection.CursorOf([AtRest(StackPose(BagA, 0, 0, 0)), AtRest(glide)], LocationId.B, BagA));
        Assert.Null(PoseProjection.CursorOf([AtRest(StackPose(BagA, 0, 0, 0))], LocationId.B, BagA));
        Assert.Null(PoseProjection.CursorOf([AtRest(glide)], LocationId.H, BagA));
    }

    /// <summary>The rest poses of a grid, used before the first commit: every non-empty cell at its centre.</summary>
    [Fact]
    public void Rest_IsOneCentredPosePerNonEmptyCell()
    {
        var rock = new ItemType("Plain Rock", Category.Material, true);
        var grid = Grid.Create(4, 2).SetCell(5, new Cell(new ItemStack(rock, 3)));
        var rest = PoseProjection.Rest(grid, BagA);
        var only = Assert.Single(rest);
        Assert.Equal(StackPose(BagA, 5, 1, 1), only.Pose);
        Assert.Equal(5, PoseProjection.ContentCell(only));
    }
}
