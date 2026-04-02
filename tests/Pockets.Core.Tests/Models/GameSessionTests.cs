using System.Collections.Immutable;
using Pockets.Core.Models;
using Pockets.Core.Rendering;

namespace Pockets.Core.Tests.Models;

public class GameSessionTests
{
    private static readonly ItemType Rck = new("Rck", Category.Material, IsStackable: true, MaxStackSize: 9);
    private static readonly ItemType Grs = new("Grs", Category.Material, IsStackable: true, MaxStackSize: 9);
    private static readonly ItemType Swd = new("Swd", Category.Weapon, IsStackable: false);

    private static readonly Dictionary<string, ItemType> DiagramTypes = new()
    {
        ["Rck"] = Rck, ["Grs"] = Grs, ["Swd"] = Swd,
    };

    private static GameSession FromDiagram(string diagram, int handSize = 1)
    {
        var parsed = GridDiagram.Parse(diagram, DiagramTypes, gridColumns: 4, gridRows: 3);
        var allTypes = DiagramTypes.Values.Distinct().ToImmutableArray();
        var handBag = parsed.Hand.Length > 0
            ? new Bag(Grid.Create(handSize, 1)).AcquireItems(parsed.Hand).UpdatedBag
            : GameState.CreateHandBag(handSize);
        var rootBag = new Bag(parsed.Grid);
        var store = BagStore.Empty.Add(rootBag).Add(handBag);
        var state = new GameState(
            store,
            LocationMap.Create(handBag.Id, rootBag.Id, new Cursor(parsed.Cursor ?? new Position(0, 0))),
            allTypes);
        return GameSession.New(state);
    }

    // ==================== Basic Undo ====================

    [Fact]
    public void New_EmptyUndoStack()
    {
        var session = FromDiagram("[Rck5]*[    ] [    ] [    ]");
        Assert.False(session.CanUndo);
        Assert.Equal(0, session.UndoDepth);
    }

    [Fact]
    public void ExecuteTool_PushesUndoState()
    {
        var session = FromDiagram("[Rck5]*[    ] [    ] [    ]");
        session = session.ExecuteGrab();
        Assert.True(session.CanUndo);
        Assert.Equal(1, session.UndoDepth);
    }

    [Fact]
    public void Undo_RestoresPreviousState()
    {
        var session = FromDiagram("[Rck5]*[Swd-] [    ] [    ]");
        var afterGrab = session.ExecuteGrab();

        // Verify grab happened
        Assert.True(afterGrab.Current.RootBag.Grid.GetCell(0).IsEmpty);

        // Undo
        var undone = afterGrab.Undo();
        Assert.NotNull(undone);

        // State restored
        GridDiagram.AssertGridMatches(undone!.Current.RootBag.Grid,
            DiagramTypes, "[Rck5] [Swd-] [    ] [    ]");
        Assert.False(undone.Current.HasItemsInHand);
    }

    [Fact]
    public void Undo_EmptyStack_ReturnsNull()
    {
        var session = FromDiagram("[Rck5]*[    ] [    ] [    ]");
        Assert.Null(session.Undo());
    }

    [Fact]
    public void Undo_MultipleSteps()
    {
        var session = FromDiagram("[Rck5]*[    ] [    ] [    ]");

        // Grab, move right (to empty cell), drop = 2 undoable actions
        session = session.ExecuteGrab();
        session = session.MoveCursor(Direction.Right);
        session = session.ExecuteDrop();

        Assert.Equal(2, session.UndoDepth);

        // Undo drop — item back in hand
        var undo1 = session.Undo()!;
        Assert.True(undo1.Current.HasItemsInHand);
        Assert.Equal(1, undo1.UndoDepth);

        // Undo grab — item back at original cell
        var undo2 = undo1.Undo()!;
        Assert.False(undo2.Current.HasItemsInHand);
        GridDiagram.AssertGridMatches(undo2.Current.RootBag.Grid,
            DiagramTypes, "[Rck5] [    ] [    ] [    ]");
    }

    [Fact]
    public void Undo_MaxDepth_OldestDropped()
    {
        var session = FromDiagram("[    ]*[    ] [    ] [    ]");
        // Set max depth to 3 for testing
        session = session with { MaxUndoDepth = 3 };

        // Do 5 acquire-random actions
        var rng = new Random(42);
        for (int i = 0; i < 5; i++)
            session = session.ExecuteAcquireRandom(rng);

        // Should only have 3 undo levels, not 5
        Assert.Equal(3, session.UndoDepth);
    }

    // ==================== MoveCursor not undoable ====================

    [Fact]
    public void MoveCursor_NotUndoable()
    {
        var session = FromDiagram("[Rck5]*[    ] [    ] [    ]");
        session = session.MoveCursor(Direction.Right);
        Assert.Equal(0, session.UndoDepth);
        Assert.Equal(new Position(0, 1), session.Current.Cursor.Position);
    }

    // ==================== Failed tools not pushed ====================

    [Fact]
    public void FailedTool_NotPushedToUndo()
    {
        // Grab empty cell = success no-op, not pushed
        var session = FromDiagram("[    ]*[Rck5] [    ] [    ]");
        session = session.ExecuteGrab();
        Assert.Equal(0, session.UndoDepth); // no-op success = no change = no undo entry
    }

    [Fact]
    public void FailedTool_ErrorLogged()
    {
        // Drop with different type = error
        var session = FromDiagram("[Rck5] [Swd-]*[    ] [    ]\nHand: (Grs3)");
        session = session.ExecuteDrop();
        Assert.Contains(session.ActionLog, log => log.Contains("FAILED"));
    }

    // ==================== Action Log ====================

    [Fact]
    public void ActionLog_RecordsSuccessfulActions()
    {
        var session = FromDiagram("[Rck5]*[    ] [    ] [    ]");
        session = session.ExecuteGrab();
        Assert.Single(session.ActionLog);
        Assert.Contains("Grab", session.ActionLog[0]);
    }

    [Fact]
    public void ActionLog_RecordsUndo()
    {
        var session = FromDiagram("[Rck5]*[    ] [    ] [    ]");
        session = session.ExecuteGrab();
        session = session.Undo()!;
        Assert.Equal(2, session.ActionLog.Count);
        Assert.Contains("Undo", session.ActionLog[1]);
    }

    [Fact]
    public void ActionLog_IncludesItemDetails()
    {
        var session = FromDiagram("[Rck5]*[    ] [    ] [    ]");
        session = session.ExecuteGrab();
        // Should mention the item name and count
        Assert.Contains("Rck", session.ActionLog[0]);
        Assert.Contains("5", session.ActionLog[0]);
    }

    [Fact]
    public void ActionLog_Sort()
    {
        var session = FromDiagram("[Swd-] [Rck5]*[    ] [    ]");
        session = session.ExecuteSort();
        Assert.Single(session.ActionLog);
        Assert.Contains("Sort", session.ActionLog[0]);
    }

    [Fact]
    public void ActionLog_QuickSplit()
    {
        var session = FromDiagram("[Rck8]*[    ] [    ] [    ]");
        session = session.ExecuteQuickSplit();
        Assert.Single(session.ActionLog);
        Assert.Contains("Split", session.ActionLog[0]);
    }

    // ==================== Undo restores cursor position ====================

    [Fact]
    public void Undo_RestoresCursorPosition()
    {
        // Grab at (0,0), move cursor to (0,1), undo should restore cursor to where it was at grab time
        var session = FromDiagram("[Rck5]*[    ] [    ] [    ]");
        session = session.ExecuteGrab();
        session = session.MoveCursor(Direction.Right);

        var undone = session.Undo()!;
        // Cursor should be restored to (0,0) from the undo snapshot
        Assert.Equal(new Position(0, 0), undone.Current.Cursor.Position);
    }
}
