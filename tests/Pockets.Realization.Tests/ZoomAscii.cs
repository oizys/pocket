using Pockets.Core.Models;
using Pockets.Core.Rendering;

namespace Pockets.Realization.Tests;

/// <summary>Test helper: draws a bag's poses as zoomed ASCII, each cell n×n sub-cells.</summary>
public static class ZoomAscii
{
    /// <summary>Floor division for a positive divisor.</summary>
    private static int FloorDiv(int a, int b) => a >= 0 ? a / b : -((-a + b - 1) / b);

    /// <summary>
    /// Renders <paramref name="bag"/>'s grid at zoom <paramref name="n"/>: '.' background, cells joined by '|',
    /// grid-row bands separated by '-' lines joined by '+'. Stacks draw their item glyph ('*' for an empty cell),
    /// bodies draw '@' on top. Poses of other bags and off-grid positions are not drawn.
    /// </summary>
    public static string Render(IEnumerable<Pose> poses, Bag bag, int n = 4)
    {
        var grid = bag.Grid;
        var width = grid.Columns * n;
        var height = grid.Rows * n;
        var canvas = Enumerable.Range(0, height).Select(_ => Enumerable.Repeat('.', width).ToArray()).ToArray();

        var mine = poses.Where(p => p.Bag == bag.Id).ToList();
        var ordered = mine.Where(p => p.Entity is EntityKey.Stack).Concat(mine.Where(p => p.Entity is EntityKey.Body));
        foreach (var p in ordered)
        {
            var x = p.CellCol * n + FloorDiv((p.SubX + 8) * n, Pose.SubUnits);
            var y = p.CellRow * n + FloorDiv((p.SubY + 8) * n, Pose.SubUnits);
            if (x < 0 || y < 0 || x >= width || y >= height) continue;
            canvas[y][x] = p.Entity switch
            {
                EntityKey.Body => '@',
                EntityKey.Stack s when s.At.Index >= 0 && s.At.Index < grid.Cells.Length && !grid.Cells[s.At.Index].IsEmpty
                    => GlyphRenderer.GlyphFor(grid.Cells[s.At.Index].Stack!.ItemType)[0],
                _ => '*'
            };
        }

        var separator = string.Join("+", Enumerable.Repeat(new string('-', n), grid.Columns));
        var bands = Enumerable.Range(0, grid.Rows).Select(r =>
            string.Join("\n", Enumerable.Range(0, n).Select(line =>
                string.Join("|", Enumerable.Range(0, grid.Columns)
                    .Select(c => new string(canvas[r * n + line], c * n, n))))));
        return string.Join("\n" + separator + "\n", bands);
    }
}
