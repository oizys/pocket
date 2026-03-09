using System.Text;
using Pockets.Core.Models;

namespace Pockets.Core.Rendering;

/// <summary>
/// Renders GameState as a tight Unicode box-drawing grid. Each cell is a fixed width
/// with category symbols, abbreviations, and count. Optimized for horizontal brevity.
/// </summary>
public class CompactGridRenderer : IStateRenderer
{
    private readonly int _cellWidth;

    public CompactGridRenderer(int cellWidth = 8) => _cellWidth = cellWidth;

    public string Render(GameState state)
    {
        var grid = state.RootBag.Grid;
        var sb = new StringBuilder();

        sb.AppendLine(RenderBorder('┌', '┬', '┐', grid.Columns));

        for (var row = 0; row < grid.Rows; row++)
        {
            if (row > 0) sb.AppendLine(RenderBorder('├', '┼', '┤', grid.Columns));
            sb.AppendLine(RenderRow(grid, row, state));
        }

        sb.AppendLine(RenderBorder('└', '┴', '┘', grid.Columns));
        sb.AppendLine(RenderHelpers.FormatHandSummary(state));
        sb.Append($"> {RenderHelpers.DescribeCursorItem(state)}");

        return sb.ToString();
    }

    private string RenderBorder(char left, char mid, char right, int cols)
    {
        var seg = new string('─', _cellWidth);
        return left + string.Join(mid.ToString(), Enumerable.Repeat(seg, cols)) + right;
    }

    private string RenderRow(Grid grid, int row, GameState state)
    {
        var cells = Enumerable.Range(0, grid.Columns)
            .Select(col =>
            {
                var pos = new Position(row, col);
                var cell = grid.GetCell(pos);
                var isCursor = state.Cursor.Position == pos;
                var content = FormatCompactCell(cell.Stack, isCursor);
                return PadOrTruncate(content, _cellWidth);
            });

        return "│" + string.Join("│", cells) + "│";
    }

    /// <summary>
    /// Width-aware cell formatting: abbreviation length adapts to fit within cell width,
    /// accounting for marker, category symbol, ×, and count digits.
    /// </summary>
    private string FormatCompactCell(ItemStack? stack, bool isCursor)
    {
        if (stack is null)
            return isCursor ? ">" : "";

        var marker = isCursor ? ">" : "";
        var sym = RenderHelpers.CategorySymbol(stack.ItemType.Category);

        if (!stack.ItemType.IsStackable)
        {
            var budget = _cellWidth - marker.Length - 1; // -1 for symbol
            var abbr = RenderHelpers.AbbreviateName(stack.ItemType.Name, budget);
            return $"{marker}{sym}{abbr}";
        }

        var countStr = $"×{stack.Count}";
        var abbrBudget = _cellWidth - marker.Length - 1 - countStr.Length; // marker + sym + ×N
        var name = RenderHelpers.AbbreviateName(stack.ItemType.Name, Math.Max(1, abbrBudget));
        return $"{marker}{sym}{name}{countStr}";
    }

    private static string PadOrTruncate(string s, int width) =>
        s.Length <= width ? s.PadRight(width) : s[..width];
}
