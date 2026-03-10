using System.Collections.Immutable;
using Pockets.Core.Models;
using Pockets.Core.Rendering;

namespace Pockets.Core.Tests.Models;

public class PrimarySecondaryTests
{
    private static readonly ItemType Rck = new("Rck", Category.Material, IsStackable: true, MaxStackSize: 9);
    private static readonly ItemType Swd = new("Swd", Category.Weapon, IsStackable: false);
    private static readonly ItemType Grs = new("Grs", Category.Material, IsStackable: true, MaxStackSize: 9);
    private static readonly ItemType SmallBag = new("Small Bag", Category.Bag, IsStackable: false);

    private static readonly Dictionary<string, ItemType> DiagramTypes = new()
    {
        ["Rck"] = Rck, ["Grs"] = Grs, ["Swd"] = Swd,
    };

    private static readonly ImmutableArray<ItemType> AllTypes =
        ImmutableArray.Create(Rck, Swd, Grs, SmallBag);

    private static GameState FromDiagram(string diagram)
    {
        var parsed = GridDiagram.Parse(diagram, DiagramTypes, gridColumns: 4, gridRows: 3);
        var handBag = parsed.Hand.Length > 0
            ? new Bag(Grid.Create(1, 1)).AcquireItems(parsed.Hand).UpdatedBag
            : GameState.CreateHandBag();
        return new GameState(
            new Bag(parsed.Grid),
            new Cursor(parsed.Cursor ?? new Position(0, 0)),
            AllTypes,
            handBag);
    }

    // ==================== Primary: Grab ====================

    [Fact]
    public void Primary_EmptyHand_OccupiedCell_Grabs()
    {
        var state = FromDiagram("[Rck5]*[    ] [    ] [    ]");
        var result = state.ToolPrimary();

        Assert.True(result.Success);
        Assert.True(result.State.HasItemsInHand);
        Assert.Equal(5, result.State.HandItems[0].Count);
        Assert.True(result.State.ActiveBag.Grid.GetCell(0).IsEmpty);
    }

    [Fact]
    public void Primary_EmptyHand_EmptyCell_NoOp()
    {
        var state = FromDiagram("[    ]*[    ] [    ] [    ]");
        var result = state.ToolPrimary();

        Assert.True(result.Success);
        Assert.Equal(state, result.State);
    }

    // ==================== Primary: Drop ====================

    [Fact]
    public void Primary_FullHand_EmptyCell_Drops()
    {
        var state = FromDiagram("[    ]*[    ] [    ] [    ]\nHand: (Rck5)");
        var result = state.ToolPrimary();

        Assert.True(result.Success);
        Assert.False(result.State.HasItemsInHand);
        Assert.Equal(5, result.State.ActiveBag.Grid.GetCell(0).Stack!.Count);
    }

    // ==================== Primary: Merge ====================

    [Fact]
    public void Primary_FullHand_SameType_Merges()
    {
        var state = FromDiagram("[Rck3]*[    ] [    ] [    ]\nHand: (Rck2)");
        var result = state.ToolPrimary();

        Assert.True(result.Success);
        Assert.False(result.State.HasItemsInHand);
        Assert.Equal(5, result.State.ActiveBag.Grid.GetCell(0).Stack!.Count);
    }

    [Fact]
    public void Primary_FullHand_SameType_OverflowAcquired()
    {
        var state = FromDiagram("[Rck7]*[    ] [    ] [    ]\nHand: (Rck5)");
        var result = state.ToolPrimary();

        Assert.True(result.Success);
        // Max is 9, so 7+5=12, cell gets 9, overflow (3) acquired into grid
        Assert.Equal(9, result.State.ActiveBag.Grid.GetCell(0).Stack!.Count);
        Assert.False(result.State.HasItemsInHand);
        // Overflow placed in next available cell
        Assert.Equal(3, result.State.ActiveBag.Grid.GetCell(1).Stack!.Count);
    }

    // ==================== Primary: Swap ====================

    [Fact]
    public void Primary_FullHand_DifferentType_Swaps()
    {
        var state = FromDiagram("[Rck3]*[    ] [    ] [    ]\nHand: (Swd-)");
        var result = state.ToolPrimary();

        Assert.True(result.Success);
        // Sword should now be in cell, rock in hand
        Assert.Equal(Swd, result.State.ActiveBag.Grid.GetCell(0).Stack!.ItemType);
        Assert.Equal(Rck, result.State.HandItems[0].ItemType);
        Assert.Equal(3, result.State.HandItems[0].Count);
    }

    // ==================== Primary: Enter Bag ====================

    [Fact]
    public void Primary_OnBag_EntersBag()
    {
        var innerBag = new Bag(Grid.Create(3, 2), "Cave");
        var rootGrid = Grid.Create(4, 3);
        rootGrid = rootGrid.SetCell(0, new Cell(new ItemStack(SmallBag, 1, ContainedBag: innerBag)));
        var state = new GameState(new Bag(rootGrid), new Cursor(new Position(0, 0)), AllTypes, GameState.CreateHandBag());

        var result = state.ToolPrimary();

        Assert.True(result.Success);
        Assert.True(result.State.IsNested);
        Assert.Equal(3, result.State.ActiveBag.Grid.Columns);
    }

    [Fact]
    public void Primary_OnBag_WithFullHand_StillEnters()
    {
        var innerBag = new Bag(Grid.Create(3, 2), "Cave");
        var rootGrid = Grid.Create(4, 3);
        rootGrid = rootGrid.SetCell(0, new Cell(new ItemStack(SmallBag, 1, ContainedBag: innerBag)));
        var rootBag = new Bag(rootGrid);
        var handBag = new Bag(Grid.Create(1, 1)).AcquireItems(new[] { new ItemStack(Rck, 3) }).UpdatedBag;
        var state = new GameState(rootBag, new Cursor(new Position(0, 0)), AllTypes, handBag);

        var result = state.ToolPrimary();

        Assert.True(result.Success);
        Assert.True(result.State.IsNested);
        // Hand should still have items
        Assert.True(result.State.HasItemsInHand);
    }

    // ==================== Primary: Harvest (nested) ====================

    [Fact]
    public void Primary_Nested_EmptyHand_Harvests()
    {
        var innerItems = new[] { new ItemStack(Rck, 3) };
        var innerBag = new Bag(Grid.Create(3, 2), "Cave");
        var (filledInner, _) = innerBag.AcquireItems(innerItems);
        var rootGrid = Grid.Create(4, 3);
        rootGrid = rootGrid.SetCell(0, new Cell(new ItemStack(SmallBag, 1, ContainedBag: filledInner)));
        var state = new GameState(new Bag(rootGrid), new Cursor(new Position(0, 0)), AllTypes, GameState.CreateHandBag());

        // Enter bag
        var entered = state.ToolPrimary().State;
        Assert.True(entered.IsNested);

        // Primary on item = harvest
        var harvested = entered.ToolPrimary();
        Assert.True(harvested.Success);
        Assert.True(harvested.State.ActiveBag.Grid.GetCell(0).IsEmpty);
        // Item should be in parent bag
        var rckInRoot = harvested.State.RootBag.Grid.Cells
            .Where(c => !c.IsEmpty && c.Stack!.ItemType == Rck)
            .ToList();
        Assert.NotEmpty(rckInRoot);
    }

    // ==================== Secondary: Grab Half ====================

    [Fact]
    public void Secondary_EmptyHand_GrabsHalf()
    {
        var state = FromDiagram("[Rck8]*[    ] [    ] [    ]");
        var result = state.ToolSecondary();

        Assert.True(result.Success);
        // 8 split: (8+1)/2 = 4 left stays, 4 right to hand
        Assert.Equal(4, result.State.ActiveBag.Grid.GetCell(0).Stack!.Count);
        Assert.Equal(4, result.State.HandItems[0].Count);
    }

    [Fact]
    public void Secondary_EmptyHand_CountOne_NoOp()
    {
        var state = FromDiagram("[Rck1]*[    ] [    ] [    ]");
        var result = state.ToolSecondary();

        Assert.True(result.Success);
        Assert.Equal(state, result.State);
    }

    // ==================== Secondary: Place One ====================

    [Fact]
    public void Secondary_FullHand_EmptyCell_PlacesOne()
    {
        var state = FromDiagram("[    ]*[    ] [    ] [    ]\nHand: (Rck5)");
        var result = state.ToolSecondary();

        Assert.True(result.Success);
        Assert.Equal(1, result.State.ActiveBag.Grid.GetCell(0).Stack!.Count);
        Assert.Equal(4, result.State.HandItems[0].Count);
    }

    [Fact]
    public void Secondary_FullHand_SameType_PlacesOne()
    {
        var state = FromDiagram("[Rck3]*[    ] [    ] [    ]\nHand: (Rck5)");
        var result = state.ToolSecondary();

        Assert.True(result.Success);
        Assert.Equal(4, result.State.ActiveBag.Grid.GetCell(0).Stack!.Count);
        Assert.Equal(4, result.State.HandItems[0].Count);
    }

    [Fact]
    public void Secondary_FullHand_DifferentType_NoOp()
    {
        var state = FromDiagram("[Rck3]*[    ] [    ] [    ]\nHand: (Swd-)");
        var result = state.ToolSecondary();

        Assert.True(result.Success);
        Assert.Equal(state, result.State);
    }

    [Fact]
    public void Secondary_PlaceOne_EmptiesHand_WhenLastItem()
    {
        var state = FromDiagram("[    ]*[    ] [    ] [    ]\nHand: (Rck1)");
        var result = state.ToolSecondary();

        Assert.True(result.Success);
        Assert.Equal(1, result.State.ActiveBag.Grid.GetCell(0).Stack!.Count);
        Assert.False(result.State.HasItemsInHand);
    }

    // ==================== Session: Undo ====================

    [Fact]
    public void Session_PrimaryGrabThenDrop_Undoable()
    {
        var state = FromDiagram("[Rck5]*[    ] [    ] [    ]");
        var session = GameSession.New(state);

        // Primary on Rck = grab
        session = session.ExecutePrimary();
        Assert.True(session.Current.HasItemsInHand);
        Assert.Equal(1, session.UndoDepth);

        // Move to empty cell, primary = drop
        session = session.MoveCursor(Direction.Right);
        session = session.ExecutePrimary();
        Assert.False(session.Current.HasItemsInHand);
        Assert.Equal(2, session.UndoDepth);

        // Undo drop
        session = session.Undo()!;
        Assert.True(session.Current.HasItemsInHand);

        // Undo grab
        session = session.Undo()!;
        Assert.False(session.Current.HasItemsInHand);
        Assert.Equal(5, session.Current.ActiveBag.Grid.GetCell(0).Stack!.Count);
    }

    [Fact]
    public void Session_Swap_Undoable()
    {
        var state = FromDiagram("[Rck3]*[    ] [    ] [    ]\nHand: (Swd-)");
        var session = GameSession.New(state);

        session = session.ExecutePrimary();
        Assert.Equal(Swd, session.Current.ActiveBag.Grid.GetCell(0).Stack!.ItemType);
        Assert.Equal(Rck, session.Current.HandItems[0].ItemType);

        session = session.Undo()!;
        Assert.Equal(Rck, session.Current.ActiveBag.Grid.GetCell(0).Stack!.ItemType);
        Assert.Equal(Swd, session.Current.HandItems[0].ItemType);
    }
}
