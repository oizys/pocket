using Pockets.Core.Models;
using Pockets.Core.Rendering;
using System.Collections.Immutable;

namespace Pockets.Core.Tests.Rendering;

public class GridDiagramTests
{
    // --- Shared item types for tests ---
    static readonly ItemType Rock = new("Rock", Category.Material, true);
    static readonly ItemType Grass = new("Grass", Category.Material, true);
    static readonly ItemType Sword = new("Sword", Category.Weapon, false);
    static readonly ItemType Bow = new("Bow", Category.Weapon, false);

    static readonly Dictionary<string, ItemType> Types = new()
    {
        ["Rck"] = Rock,
        ["Grs"] = Grass,
        ["Swd"] = Sword,
        ["Bow"] = Bow,
    };

    // ==================== Rendering ====================

    [Fact]
    public void Render_EmptyGrid_AllBlankCells()
    {
        var grid = Grid.Create(3, 2);
        var result = GridDiagram.Render(grid);
        // 3 columns, 2 rows, all empty
        Assert.Contains("[    ]", result);
        // Should have 6 empty cells total (3x2)
        Assert.Equal(6, result.Split("[    ]").Length - 1);
    }

    [Fact]
    public void Render_CellWith4CharFormat()
    {
        // 3 char abbreviation + 1 digit count
        var grid = Grid.Create(2, 1)
            .SetCell(0, new Cell(new ItemStack(Rock, 5)));
        var result = GridDiagram.Render(grid, types: Types);
        Assert.Contains("[Rck5]", result);
    }

    [Fact]
    public void Render_UniqueItem_ShowsDash()
    {
        var grid = Grid.Create(2, 1)
            .SetCell(0, new Cell(new ItemStack(Sword, 1)));
        var result = GridDiagram.Render(grid, types: Types);
        Assert.Contains("[Swd-]", result);
    }

    [Fact]
    public void Render_CursorPosition_ShowsAsterisk()
    {
        var grid = Grid.Create(3, 1);
        var cursor = new Position(0, 1);
        var result = GridDiagram.Render(grid, cursor: cursor);
        // Cell at col 1 should have * after it
        Assert.Contains("]*", result);
    }

    [Fact]
    public void Render_HandContents_ShownBelow()
    {
        var grid = Grid.Create(2, 1);
        var hand = ImmutableArray.Create(new ItemStack(Rock, 3));
        var result = GridDiagram.Render(grid, hand: hand, types: Types);
        Assert.Contains("Hand: (Rck3)", result);
    }

    [Fact]
    public void Render_EmptyHand_ShowsEmptyParens()
    {
        var grid = Grid.Create(2, 1);
        var hand = ImmutableArray<ItemStack>.Empty;
        var result = GridDiagram.Render(grid, hand: hand);
        Assert.Contains("Hand: ()", result);
    }

    [Fact]
    public void Render_MultipleHandItems_CommaSeparated()
    {
        var grid = Grid.Create(2, 1);
        var hand = ImmutableArray.Create(
            new ItemStack(Rock, 3),
            new ItemStack(Sword, 1));
        var result = GridDiagram.Render(grid, hand: hand, types: Types);
        Assert.Contains("Hand: (Rck3, Swd-)", result);
    }

    [Fact]
    public void Render_MultipleRows_SeparateLines()
    {
        var grid = Grid.Create(2, 2)
            .SetCell(0, new Cell(new ItemStack(Rock, 1)))
            .SetCell(3, new Cell(new ItemStack(Grass, 9)));
        var result = GridDiagram.Render(grid, types: Types);
        var lines = result.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.True(lines.Length >= 2);
    }

    // ==================== Parsing ====================

    [Fact]
    public void Parse_EmptyCells_CreatesEmptyGrid()
    {
        var result = GridDiagram.Parse("[    ] [    ]\n[    ] [    ]");
        Assert.Equal(2, result.Grid.Columns);
        Assert.Equal(2, result.Grid.Rows);
        Assert.True(result.Grid.GetCell(0).IsEmpty);
        Assert.True(result.Grid.GetCell(3).IsEmpty);
    }

    [Fact]
    public void Parse_StackableItem_CorrectTypeAndCount()
    {
        var result = GridDiagram.Parse("[Rck5] [    ]", Types);
        var cell = result.Grid.GetCell(0);
        Assert.False(cell.IsEmpty);
        Assert.Equal(Rock, cell.Stack!.ItemType);
        Assert.Equal(5, cell.Stack.Count);
    }

    [Fact]
    public void Parse_UniqueItem_Count1()
    {
        var result = GridDiagram.Parse("[Swd-]", Types);
        var cell = result.Grid.GetCell(0);
        Assert.Equal(Sword, cell.Stack!.ItemType);
        Assert.Equal(1, cell.Stack.Count);
    }

    [Fact]
    public void Parse_CursorMarker_SetsPosition()
    {
        var result = GridDiagram.Parse("[    ] [Rck5]*[    ]", Types);
        Assert.NotNull(result.Cursor);
        Assert.Equal(new Position(0, 1), result.Cursor.Value);
    }

    [Fact]
    public void Parse_HandLine_ParsesContents()
    {
        var result = GridDiagram.Parse(
            "[    ] [    ]\nHand: (Rck3, Swd-)", Types);
        Assert.Equal(2, result.Hand.Length);
        Assert.Equal(Rock, result.Hand[0].ItemType);
        Assert.Equal(3, result.Hand[0].Count);
        Assert.Equal(Sword, result.Hand[1].ItemType);
        Assert.Equal(1, result.Hand[1].Count);
    }

    [Fact]
    public void Parse_EmptyHand_EmptyArray()
    {
        var result = GridDiagram.Parse("[    ]\nHand: ()", Types);
        Assert.True(result.Hand.IsEmpty);
    }

    [Fact]
    public void Parse_FlexibleWhitespace_InEmptyCells()
    {
        // Parser should handle varying whitespace inside empty brackets
        var result = GridDiagram.Parse("[  ] [     ] [    ]");
        Assert.Equal(3, result.Grid.Columns);
        Assert.True(result.Grid.GetCell(0).IsEmpty);
        Assert.True(result.Grid.GetCell(1).IsEmpty);
        Assert.True(result.Grid.GetCell(2).IsEmpty);
    }

    [Fact]
    public void Parse_ExplicitGridSize_PadsWithEmptyCells()
    {
        // Show only 2x1 but actual grid is 4x3
        var result = GridDiagram.Parse("[Rck5] [Swd-]", Types,
            gridColumns: 4, gridRows: 3);
        Assert.Equal(4, result.Grid.Columns);
        Assert.Equal(3, result.Grid.Rows);
        Assert.Equal(Rock, result.Grid.GetCell(0).Stack!.ItemType);
        Assert.Equal(Sword, result.Grid.GetCell(1).Stack!.ItemType);
        Assert.True(result.Grid.GetCell(2).IsEmpty);
        Assert.True(result.Grid.GetCell(new Position(2, 0)).IsEmpty);
    }

    [Fact]
    public void Parse_UnknownAbbreviation_AutoCreatesType()
    {
        // No type registry — parser should create a minimal ItemType
        var result = GridDiagram.Parse("[Xyz7]");
        var cell = result.Grid.GetCell(0);
        Assert.False(cell.IsEmpty);
        Assert.Equal("Xyz", cell.Stack!.ItemType.Name);
        Assert.Equal(7, cell.Stack.Count);
        Assert.True(cell.Stack.ItemType.IsStackable);
    }

    [Fact]
    public void Parse_UnknownUniqueAbbreviation_AutoCreatesUnique()
    {
        var result = GridDiagram.Parse("[Xyz-]");
        var cell = result.Grid.GetCell(0);
        Assert.Equal("Xyz", cell.Stack!.ItemType.Name);
        Assert.Equal(1, cell.Stack.Count);
        Assert.False(cell.Stack.ItemType.IsStackable);
    }

    // ==================== Round-trip ====================

    [Fact]
    public void RoundTrip_ParseThenRender_Stable()
    {
        var input = "[Rck5] [Swd-] [    ] [    ]\n[Grs3] [    ] [    ]*[    ]\nHand: (Bow-)";
        var parsed = GridDiagram.Parse(input, Types);
        var rendered = GridDiagram.Render(parsed.Grid,
            cursor: parsed.Cursor, hand: parsed.Hand, types: Types);
        var reparsed = GridDiagram.Parse(rendered, Types);

        Assert.Equal(parsed.Grid.Columns, reparsed.Grid.Columns);
        Assert.Equal(parsed.Grid.Rows, reparsed.Grid.Rows);
        Assert.Equal(parsed.Cursor, reparsed.Cursor);
        Assert.Equal(parsed.Hand.Length, reparsed.Hand.Length);

        for (int i = 0; i < parsed.Grid.Columns * parsed.Grid.Rows; i++)
        {
            var pCell = parsed.Grid.GetCell(i);
            var rCell = reparsed.Grid.GetCell(i);
            Assert.Equal(pCell.IsEmpty, rCell.IsEmpty);
            if (!pCell.IsEmpty)
            {
                Assert.Equal(pCell.Stack!.ItemType, rCell.Stack!.ItemType);
                Assert.Equal(pCell.Stack.Count, rCell.Stack.Count);
            }
        }
    }

    // ==================== AssertGridMatches ====================

    [Fact]
    public void AssertGridMatches_CorrectState_NoException()
    {
        var grid = Grid.Create(3, 1)
            .SetCell(0, new Cell(new ItemStack(Rock, 5)))
            .SetCell(2, new Cell(new ItemStack(Sword, 1)));

        // Should not throw
        GridDiagram.AssertGridMatches(grid, Types, "[Rck5] [    ] [Swd-]");
    }

    [Fact]
    public void AssertGridMatches_WrongState_Throws()
    {
        var grid = Grid.Create(2, 1)
            .SetCell(0, new Cell(new ItemStack(Rock, 5)));

        Assert.ThrowsAny<Exception>(() =>
            GridDiagram.AssertGridMatches(grid, Types, "[Rck3] [    ]"));
    }
}
