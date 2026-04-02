using System.Collections.Immutable;
using Pockets.Core.Models;
using Pockets.Core.Rendering;

namespace Pockets.Core.Tests.Models;

public class ModalSplitTests
{
    private static readonly ItemType Rck = new("Rck", Category.Material, IsStackable: true, MaxStackSize: 9);
    private static readonly ItemType Swd = new("Swd", Category.Weapon, IsStackable: false);
    private static readonly ItemType Grs = new("Grs", Category.Material, IsStackable: true, MaxStackSize: 9);

    private static readonly Dictionary<string, ItemType> DiagramTypes = new()
    {
        ["Rck"] = Rck, ["Grs"] = Grs, ["Swd"] = Swd,
    };

    private static GameState FromDiagram(string diagram)
    {
        var parsed = GridDiagram.Parse(diagram, DiagramTypes, gridColumns: 4, gridRows: 3);
        var allTypes = DiagramTypes.Values.Distinct().ToImmutableArray();
        var handBag = parsed.Hand.Length > 0
            ? new Bag(Grid.Create(1, 1)).AcquireItems(parsed.Hand).UpdatedBag
            : GameState.CreateHandBag();
        var rootBag = new Bag(parsed.Grid);
        var store = BagStore.Empty.Add(rootBag).Add(handBag);
        return new GameState(
            store,
            LocationMap.Create(handBag.Id, rootBag.Id, new Cursor(parsed.Cursor ?? new Position(0, 0))),
            allTypes);
    }

    [Fact]
    public void ModalSplit_CustomAmount_LeftStaysRightToHand()
    {
        // Split 8 as 2/6
        var state = FromDiagram("[Rck8]*[    ] [    ] [    ]");
        var result = state.ToolModalSplit(2);

        Assert.True(result.Success);
        var cursorCell = result.State.RootBag.Grid.GetCell(new Position(0, 0));
        Assert.Equal(2, cursorCell.Stack!.Count);
        Assert.Single(result.State.HandItems);
        Assert.Equal(6, result.State.HandItems[0].Count);
    }

    [Fact]
    public void ModalSplit_TakeOne_Leaves7()
    {
        // Split 8 as 7/1
        var state = FromDiagram("[Rck8]*[    ] [    ] [    ]");
        var result = state.ToolModalSplit(7);

        Assert.True(result.Success);
        GridDiagram.AssertGridMatches(result.State.RootBag.Grid,
            DiagramTypes, "[Rck7] [    ] [    ] [    ]");
        Assert.Equal(1, result.State.HandItems[0].Count);
    }

    [Fact]
    public void ModalSplit_LeaveOne_Takes7()
    {
        // Split 8 as 1/7
        var state = FromDiagram("[Rck8]*[    ] [    ] [    ]");
        var result = state.ToolModalSplit(1);

        Assert.True(result.Success);
        GridDiagram.AssertGridMatches(result.State.RootBag.Grid,
            DiagramTypes, "[Rck1] [    ] [    ] [    ]");
        Assert.Equal(7, result.State.HandItems[0].Count);
    }

    [Fact]
    public void ModalSplit_InvalidAmount_Zero_Fails()
    {
        var state = FromDiagram("[Rck8]*[    ] [    ] [    ]");
        var result = state.ToolModalSplit(0);

        Assert.False(result.Success);
        Assert.Contains("Invalid", result.Error);
    }

    [Fact]
    public void ModalSplit_InvalidAmount_EqualToCount_Fails()
    {
        var state = FromDiagram("[Rck8]*[    ] [    ] [    ]");
        var result = state.ToolModalSplit(8); // left=8 means right=0, invalid

        Assert.False(result.Success);
        Assert.Contains("Invalid", result.Error);
    }

    [Fact]
    public void ModalSplit_EmptyCell_NoOp()
    {
        var state = FromDiagram("[    ]*[    ] [    ] [    ]");
        var result = state.ToolModalSplit(3);
        Assert.True(result.Success);
        Assert.Equal(state, result.State);
    }

    [Fact]
    public void ModalSplit_CountOne_NoOp()
    {
        var state = FromDiagram("[Rck1]*[    ] [    ] [    ]");
        var result = state.ToolModalSplit(1);
        Assert.True(result.Success);
    }

    [Fact]
    public void ModalSplit_HandFull_Fails()
    {
        var state = FromDiagram("[Rck8]*[    ] [    ] [    ]\nHand: (Grs3)");
        var result = state.ToolModalSplit(2);

        Assert.False(result.Success);
        Assert.Contains("Hand is full", result.Error);
    }

    [Fact]
    public void ModalSplit_ViaSession_Undoable()
    {
        var state = FromDiagram("[Rck8]*[    ] [    ] [    ]");
        var session = GameSession.New(state);
        session = session.ExecuteModalSplit(3);

        Assert.Equal(3, session.Current.RootBag.Grid.GetCell(0).Stack!.Count);
        Assert.Equal(1, session.UndoDepth);

        // Undo restores original
        session = session.Undo()!;
        Assert.Equal(8, session.Current.RootBag.Grid.GetCell(0).Stack!.Count);
        Assert.False(session.Current.HasItemsInHand);
    }
}
