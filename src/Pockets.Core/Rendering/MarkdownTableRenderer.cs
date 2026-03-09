using System.Text;
using Pockets.Core.Models;

namespace Pockets.Core.Rendering;

/// <summary>
/// Renders GameState as a markdown table with column headers and row indices.
/// Cursor cell bolded, hand cells marked with #.
/// </summary>
public class MarkdownTableRenderer : IStateRenderer
{
    public string Render(GameState state)
    {
        var grid = state.RootBag.Grid;
        var sb = new StringBuilder();

        // Header row
        sb.Append('|');
        for (var col = 0; col < grid.Columns; col++)
            sb.Append($" {col} |");
        sb.AppendLine();

        // Separator
        sb.Append('|');
        for (var col = 0; col < grid.Columns; col++)
            sb.Append("---|");
        sb.AppendLine();

        // Data rows
        for (var row = 0; row < grid.Rows; row++)
        {
            sb.Append('|');
            for (var col = 0; col < grid.Columns; col++)
            {
                var pos = new Position(row, col);
                var cell = grid.GetCell(pos);
                var isCursor = state.Cursor.Position == pos;
                var isHand = state.ActiveHand.Contains(pos);
                var content = RenderHelpers.FormatStack(cell.Stack);

                if (isCursor && content.Length > 0) content = $"**>{content}**";
                else if (isCursor) content = "**>**";
                else if (isHand && content.Length > 0) content = $"#{content}";

                sb.Append($" {content} |");
            }
            sb.AppendLine();
        }

        sb.AppendLine();
        sb.AppendLine(RenderHelpers.FormatHandSummary(state));
        sb.Append($"> {RenderHelpers.DescribeCursorItem(state)}");

        return sb.ToString();
    }
}
