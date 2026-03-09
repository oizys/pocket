using System.Text;
using Pockets.Core.Models;

namespace Pockets.Core.Rendering;

/// <summary>
/// Renders GameState as a flat list of occupied cells. No grid layout — just
/// one line per occupied cell with position, markers, and item details.
/// </summary>
public class ListRenderer : IStateRenderer
{
    public string Render(GameState state)
    {
        var grid = state.RootBag.Grid;
        var sb = new StringBuilder();

        sb.AppendLine($"Grid: {grid.Columns}×{grid.Rows} | Cursor: ({state.Cursor.Position.Row},{state.Cursor.Position.Col})");

        var occupied = Enumerable.Range(0, grid.Cells.Length)
            .Select(i => (pos: Position.FromIndex(i, grid.Columns), cell: grid.Cells[i]))
            .Where(x => !x.cell.IsEmpty)
            .ToList();

        if (occupied.Count == 0)
        {
            sb.AppendLine("(no items)");
        }
        else
        {
            foreach (var (pos, cell) in occupied)
            {
                var isCursor = state.Cursor.Position == pos;
                var marker = isCursor ? ">" : " ";
                var stack = cell.Stack!;
                var sym = RenderHelpers.CategorySymbol(stack.ItemType.Category);
                var kind = stack.ItemType.IsStackable
                    ? $"×{stack.Count}/{stack.ItemType.EffectiveMaxStackSize}"
                    : "Unique";
                sb.AppendLine($"  ({pos.Row},{pos.Col}) {marker} {sym} {stack.ItemType.Name} {kind}");
            }
        }

        sb.AppendLine(RenderHelpers.FormatHandSummary(state));
        sb.Append($"> {RenderHelpers.DescribeCursorItem(state)}");
        return sb.ToString();
    }
}
