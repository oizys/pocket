using Pockets.Core.Models;
using System.Collections.Immutable;
using System.Text;
using System.Text.RegularExpressions;

namespace Pockets.Core.Rendering;

/// <summary>
/// Result of parsing a grid diagram string.
/// </summary>
public record GridDiagramResult(
    Grid Grid,
    Position? Cursor,
    ImmutableArray<ItemStack> Hand);

/// <summary>
/// Compact ASCII grid diagrams for tests and documentation.
/// Cell format: [Xxx#] where Xxx = 3-char abbreviation, # = count digit 1-9 or '-' for unique.
/// Empty cell: [    ] (4 spaces). Cursor: * after cell bracket. Hand: Hand: (Xxx#, Yyy-) below grid.
/// </summary>
public static class GridDiagram
{
    private static readonly Regex CellPattern = new(@"\[([^\]]*)\](\*?)", RegexOptions.Compiled);
    private static readonly Regex HandPattern = new(@"Hand:\s*\(([^)]*)\)", RegexOptions.Compiled);
    private static readonly Regex ItemPattern = new(@"([A-Za-z]{2,3})([\d\-])", RegexOptions.Compiled);

    /// <summary>
    /// Renders a grid to diagram notation. Optionally shows cursor position and hand contents.
    /// If types dictionary is provided, uses its keys as abbreviations; otherwise auto-abbreviates.
    /// </summary>
    public static string Render(Grid grid, Position? cursor = null,
        ImmutableArray<ItemStack>? hand = null,
        Dictionary<string, ItemType>? types = null)
    {
        var reverseTypes = BuildReverseMap(types);
        var sb = new StringBuilder();

        for (int row = 0; row < grid.Rows; row++)
        {
            if (row > 0) sb.Append('\n');
            for (int col = 0; col < grid.Columns; col++)
            {
                if (col > 0) sb.Append(' ');
                var pos = new Position(row, col);
                var cell = grid.GetCell(pos);
                sb.Append('[');
                sb.Append(FormatCellContent(cell.Stack, reverseTypes));
                sb.Append(']');
                if (cursor.HasValue && cursor.Value == pos)
                    sb.Append('*');
            }
        }

        if (hand.HasValue)
        {
            sb.Append('\n');
            sb.Append("Hand: (");
            sb.Append(string.Join(", ",
                hand.Value.Select(s => FormatItemShort(s, reverseTypes))));
            sb.Append(')');
        }

        return sb.ToString();
    }

    /// <summary>
    /// Parses a diagram string into a Grid, optional cursor, and optional hand contents.
    /// Types dictionary maps 3-char abbreviations to ItemTypes. Unknown abbreviations auto-create minimal types.
    /// If gridColumns/gridRows are specified, the grid is padded with empty cells beyond what the diagram shows.
    /// </summary>
    public static GridDiagramResult Parse(string diagram,
        Dictionary<string, ItemType>? types = null,
        int? gridColumns = null, int? gridRows = null)
    {
        types ??= new Dictionary<string, ItemType>();
        var autoTypes = new Dictionary<string, ItemType>(types);

        var lines = diagram.Split('\n');
        var gridLines = new List<List<(Cell Cell, bool IsCursor)>>();
        Position? cursor = null;
        var hand = ImmutableArray<ItemStack>.Empty;

        foreach (var line in lines)
        {
            var handMatch = HandPattern.Match(line);
            if (handMatch.Success)
            {
                hand = ParseHandContents(handMatch.Groups[1].Value, autoTypes);
                continue;
            }

            var cells = CellPattern.Matches(line);
            if (cells.Count == 0) continue;

            var row = new List<(Cell, bool)>();
            foreach (Match m in cells)
            {
                var content = m.Groups[1].Value.Trim();
                var isCursor = m.Groups[2].Value == "*";
                var cell = ParseCellContent(content, autoTypes);
                row.Add((cell, isCursor));
            }
            gridLines.Add(row);
        }

        int diagramRows = gridLines.Count;
        int diagramCols = gridLines.Count > 0 ? gridLines.Max(r => r.Count) : 0;
        int finalCols = gridColumns ?? diagramCols;
        int finalRows = gridRows ?? diagramRows;

        var emptyCells = Enumerable.Repeat(new Cell(), finalCols * finalRows).ToArray();
        var cellArray = emptyCells.ToArray();

        for (int row = 0; row < diagramRows && row < finalRows; row++)
        {
            for (int col = 0; col < gridLines[row].Count && col < finalCols; col++)
            {
                var (cell, isCursor) = gridLines[row][col];
                int index = new Position(row, col).ToIndex(finalCols);
                cellArray[index] = cell;
                if (isCursor)
                    cursor = new Position(row, col);
            }
        }

        var grid = new Grid(finalCols, finalRows, cellArray.ToImmutableArray());
        return new GridDiagramResult(grid, cursor, hand);
    }

    /// <summary>
    /// Asserts that a Grid matches the expected diagram. Throws if any cell differs.
    /// </summary>
    public static void AssertGridMatches(Grid actual,
        Dictionary<string, ItemType>? types, string expected)
    {
        var parsed = Parse(expected, types,
            gridColumns: actual.Columns, gridRows: actual.Rows);

        for (int i = 0; i < actual.Columns * actual.Rows; i++)
        {
            var actualCell = actual.GetCell(i);
            var expectedCell = parsed.Grid.GetCell(i);

            if (expectedCell.IsEmpty && actualCell.IsEmpty) continue;

            var pos = Position.FromIndex(i, actual.Columns);
            if (expectedCell.IsEmpty != actualCell.IsEmpty)
                throw new Exception(
                    $"Cell ({pos.Row},{pos.Col}): expected {(expectedCell.IsEmpty ? "empty" : "occupied")}, " +
                    $"got {(actualCell.IsEmpty ? "empty" : FormatItemShort(actualCell.Stack!, null))}");

            if (expectedCell.Stack!.ItemType != actualCell.Stack!.ItemType)
                throw new Exception(
                    $"Cell ({pos.Row},{pos.Col}): expected type {expectedCell.Stack.ItemType.Name}, " +
                    $"got {actualCell.Stack.ItemType.Name}");

            if (expectedCell.Stack.Count != actualCell.Stack.Count)
                throw new Exception(
                    $"Cell ({pos.Row},{pos.Col}): expected count {expectedCell.Stack.Count}, " +
                    $"got {actualCell.Stack.Count}");
        }
    }

    // --- Private helpers ---

    /// <summary>
    /// Formats a cell's content as 4 chars: 3-char abbreviation + count digit or '-'.
    /// Empty cells return 4 spaces.
    /// </summary>
    private static string FormatCellContent(ItemStack? stack,
        Dictionary<ItemType, string>? reverseTypes)
    {
        if (stack == null) return "    ";
        string abbr = GetAbbreviation(stack.ItemType, reverseTypes);
        string count = stack.ItemType.IsStackable ? stack.Count.ToString() : "-";
        return $"{abbr}{count}";
    }

    /// <summary>
    /// Formats an item stack as abbreviated notation (e.g. "Rck5", "Swd-").
    /// </summary>
    private static string FormatItemShort(ItemStack stack,
        Dictionary<ItemType, string>? reverseTypes)
    {
        string abbr = GetAbbreviation(stack.ItemType, reverseTypes);
        string count = stack.ItemType.IsStackable ? stack.Count.ToString() : "-";
        return $"{abbr}{count}";
    }

    /// <summary>
    /// Gets a 3-char abbreviation for an ItemType, using the reverse map if available,
    /// otherwise taking the first 3 characters of the name.
    /// </summary>
    private static string GetAbbreviation(ItemType type,
        Dictionary<ItemType, string>? reverseTypes)
    {
        if (reverseTypes != null && reverseTypes.TryGetValue(type, out var abbr))
            return abbr;
        return type.Name.Length >= 3 ? type.Name[..3] : type.Name.PadRight(3);
    }

    /// <summary>
    /// Builds a reverse lookup (ItemType -> abbreviation) from the types dictionary.
    /// </summary>
    private static Dictionary<ItemType, string>? BuildReverseMap(
        Dictionary<string, ItemType>? types)
    {
        if (types == null) return null;
        return types.ToDictionary(kv => kv.Value, kv => kv.Key);
    }

    /// <summary>
    /// Parses trimmed cell content into a Cell. Empty/whitespace = empty cell.
    /// Otherwise expects 3-char abbreviation + digit or '-'.
    /// </summary>
    private static Cell ParseCellContent(string content,
        Dictionary<string, ItemType> types)
    {
        if (string.IsNullOrWhiteSpace(content)) return new Cell();

        var match = ItemPattern.Match(content);
        if (!match.Success) return new Cell();

        string abbr = match.Groups[1].Value;
        string countStr = match.Groups[2].Value;

        var itemType = ResolveType(abbr, countStr == "-", types);
        int count = countStr == "-" ? 1 : int.Parse(countStr);

        return new Cell(new ItemStack(itemType, count));
    }

    /// <summary>
    /// Parses the hand contents string (between parentheses) into ItemStacks.
    /// </summary>
    private static ImmutableArray<ItemStack> ParseHandContents(string content,
        Dictionary<string, ItemType> types)
    {
        content = content.Trim();
        if (string.IsNullOrEmpty(content))
            return ImmutableArray<ItemStack>.Empty;

        var items = content.Split(',', StringSplitOptions.TrimEntries);
        var builder = ImmutableArray.CreateBuilder<ItemStack>();

        foreach (var item in items)
        {
            var match = ItemPattern.Match(item);
            if (!match.Success) continue;

            string abbr = match.Groups[1].Value;
            string countStr = match.Groups[2].Value;
            bool isUnique = countStr == "-";

            var itemType = ResolveType(abbr, isUnique, types);
            int count = isUnique ? 1 : int.Parse(countStr);
            builder.Add(new ItemStack(itemType, count));
        }

        return builder.ToImmutable();
    }

    /// <summary>
    /// Resolves an abbreviation to an ItemType from the registry, or auto-creates one.
    /// </summary>
    private static ItemType ResolveType(string abbr, bool isUnique,
        Dictionary<string, ItemType> types)
    {
        if (types.TryGetValue(abbr, out var existing))
            return existing;

        var autoType = new ItemType(abbr, Category.Misc, IsStackable: !isUnique);
        types[abbr] = autoType;
        return autoType;
    }
}
