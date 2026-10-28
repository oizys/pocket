using System.Collections.Immutable;
using Pockets.Core.Models;
using Pockets.Core.Sim;

namespace Pockets.Realization;

/// <summary>
/// The 2D projection of A2 for a grid panel (Slice 2.5), in integer pixels: a pose is drawn as a cell-sized tile at
/// <c>cell + Sub/16 · cellSize</c>, lifted by <c>Height/16 · cellHeight</c> (−y). A 3D view would map the same pose.
/// Also picks what one panel draws, so the Godot grid stays a pure function of (grid, poses).
/// </summary>
public static class PoseProjection
{
    /// <summary>The top-left pixel of <paramref name="pose"/>'s tile in a grid of <paramref name="cellW"/>×<paramref name="cellH"/> cells.</summary>
    public static (int X, int Y) TileOrigin(Pose pose, int cellW, int cellH) =>
        (pose.CellCol * cellW + pose.SubX * cellW / Pose.SubUnits,
         pose.CellRow * cellH + pose.SubY * cellH / Pose.SubUnits - (pose.Height ?? 0) * cellH / Pose.SubUnits);

    /// <summary>
    /// The poses a panel showing <paramref name="bag"/> draws, in draw order: stationary ones first (at their rest
    /// cell, no sub offset, no height), then the moving ones on top. Stable within each group.
    /// </summary>
    public static ImmutableArray<DrawnPose> ForPanel(IEnumerable<DrawnPose> poses, Guid bag, int columns) =>
        poses.Where(d => d.Pose.Bag == bag)
            .OrderBy(d => IsStill(d, columns) ? 0 : 1)
            .ToImmutableArray();

    /// <summary>The grid index whose stack a drawn pose shows (its rest key's cell), or null for a body or cursor.</summary>
    public static int? ContentCell(DrawnPose drawn) => drawn.Rest is EntityKey.Stack s ? s.At.Index : null;

    /// <summary>
    /// Where the cursor highlight of <paramref name="loc"/> goes in <paramref name="bag"/>: the body pose in a world
    /// bag, else a live cursor glide; null means neither, so draw the grid cursor cell.
    /// </summary>
    public static Pose? CursorOf(IEnumerable<DrawnPose> poses, LocationId loc, Guid bag)
    {
        var mine = poses.Select(d => d.Pose).Where(p => p.Bag == bag).ToList();
        return mine.Where(p => p.Entity == new EntityKey.Body(loc)).Cast<Pose?>().FirstOrDefault()
               ?? mine.Where(p => p.Entity == new EntityKey.Cursor(loc)).Cast<Pose?>().FirstOrDefault();
    }

    /// <summary>The rest poses of <paramref name="grid"/> as bag <paramref name="bag"/>: each non-empty cell centred (before any commit).</summary>
    public static ImmutableArray<DrawnPose> Rest(Grid grid, Guid bag) =>
        grid.Cells
            .Select((cell, i) => (cell, i))
            .Where(t => !t.cell.IsEmpty)
            .Select(t =>
            {
                var key = new EntityKey.Stack(new CellRef(bag, t.i));
                var pos = Position.FromIndex(t.i, grid.Columns);
                return new DrawnPose(new Pose(key, bag, pos.Col, pos.Row, 0, 0, null, null), key);
            })
            .ToImmutableArray();

    /// <summary>True when a stack pose sits on its rest cell with no sub offset and no lift.</summary>
    private static bool IsStill(DrawnPose d, int columns) =>
        d.Pose is { SubX: 0, SubY: 0 } p && (p.Height ?? 0) == 0
        && (ContentCell(d) is not { } i || (columns > 0 && i % columns == p.CellCol && i / columns == p.CellRow));
}
