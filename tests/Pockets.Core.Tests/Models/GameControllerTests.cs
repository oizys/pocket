using System.Collections.Immutable;
using Pockets.Core.Models;
using Pockets.Core.Rendering;

namespace Pockets.Core.Tests.Models;

public class GameControllerTests
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

    private static GameController ControllerFor(string diagram)
    {
        var state = FromDiagram(diagram);
        var session = GameSession.New(state);
        return new GameController(session);
    }

    // ==================== Cursor Movement ====================

    [Fact]
    public void HandleKey_Up_MovesCursorUp()
    {
        var ctrl = ControllerFor(
            "[Rck5] [    ] [    ] [    ]\n" +
            "[    ] [    ]*[    ] [    ]");
        var result = ctrl.HandleKey(GameKey.Up);

        Assert.True(result.Handled);
        Assert.Equal(0, result.Session.Current.Cursor.Position.Row);
        Assert.Equal(1, result.Session.Current.Cursor.Position.Col);
    }

    [Fact]
    public void HandleKey_Down_MovesCursorDown()
    {
        var ctrl = ControllerFor("[    ]*[    ] [    ] [    ]\n[    ] [    ] [    ] [    ]");
        var result = ctrl.HandleKey(GameKey.Down);

        Assert.True(result.Handled);
        Assert.Equal(1, result.Session.Current.Cursor.Position.Row);
    }

    [Fact]
    public void HandleKey_Left_MovesCursorLeft()
    {
        var ctrl = ControllerFor("[    ] [    ]*[    ] [    ]");
        var result = ctrl.HandleKey(GameKey.Left);

        Assert.True(result.Handled);
        Assert.Equal(0, result.Session.Current.Cursor.Position.Col);
    }

    [Fact]
    public void HandleKey_Right_MovesCursorRight()
    {
        var ctrl = ControllerFor("[    ]*[    ] [    ] [    ]");
        var result = ctrl.HandleKey(GameKey.Right);

        Assert.True(result.Handled);
        Assert.Equal(1, result.Session.Current.Cursor.Position.Col);
    }

    [Fact]
    public void HandleKey_Right_WrapsAround()
    {
        var ctrl = ControllerFor("[    ] [    ] [    ] [    ]*");
        var result = ctrl.HandleKey(GameKey.Right);

        Assert.True(result.Handled);
        Assert.Equal(0, result.Session.Current.Cursor.Position.Col);
    }

    [Fact]
    public void HandleKey_Up_WrapsAround()
    {
        var ctrl = ControllerFor(
            "[    ]*[    ] [    ] [    ]\n" +
            "[    ] [    ] [    ] [    ]");
        var result = ctrl.HandleKey(GameKey.Up);

        Assert.True(result.Handled);
        // Grid is 4×3 (gridRows:3 in FromDiagram), so wraps to row 2
        Assert.Equal(2, result.Session.Current.Cursor.Position.Row);
    }

    // ==================== Grab / Drop Cycle ====================

    [Fact]
    public void HandleKey_Primary_GrabsItem()
    {
        var ctrl = ControllerFor("[Rck5]*[    ] [    ] [    ]");
        var result = ctrl.HandleKey(GameKey.Primary);

        Assert.True(result.Handled);
        Assert.True(result.Session.Current.HasItemsInHand);
        Assert.Equal(5, result.Session.Current.HandItems[0].Count);
        Assert.True(result.Session.Current.ActiveBag.Grid.GetCell(0).IsEmpty);
    }

    [Fact]
    public void HandleKey_Primary_DropsItem()
    {
        var ctrl = ControllerFor(
            "[    ]*[    ] [    ] [    ]\n" +
            "Hand: (Rck5)");
        var result = ctrl.HandleKey(GameKey.Primary);

        Assert.True(result.Handled);
        Assert.False(result.Session.Current.HasItemsInHand);
        Assert.Equal(5, result.Session.Current.ActiveBag.Grid.GetCell(0).Stack!.Count);
    }

    [Fact]
    public void HandleKey_GrabMoveDrop_RelocatesItem()
    {
        var ctrl = ControllerFor("[Rck5]*[    ] [    ] [    ]");

        // Grab
        ctrl.HandleKey(GameKey.Primary);
        Assert.True(ctrl.Session.Current.HasItemsInHand);

        // Move right
        ctrl.HandleKey(GameKey.Right);
        Assert.Equal(1, ctrl.Session.Current.Cursor.Position.Col);

        // Drop
        ctrl.HandleKey(GameKey.Primary);
        Assert.False(ctrl.Session.Current.HasItemsInHand);

        // Verify: cell 0 empty, cell 1 has Rck5
        GridDiagram.AssertGridMatches(
            ctrl.Session.Current.ActiveBag.Grid,
            DiagramTypes,
            "[    ] [Rck5] [    ] [    ]");
    }

    // ==================== Secondary (Half Grab / Place One) ====================

    [Fact]
    public void HandleKey_Secondary_GrabsHalf()
    {
        var ctrl = ControllerFor("[Rck8]*[    ] [    ] [    ]");
        var result = ctrl.HandleKey(GameKey.Secondary);

        Assert.True(result.Handled);
        Assert.True(result.Session.Current.HasItemsInHand);
        // Quick split: left stays (4), right goes to hand (4)
        Assert.Equal(4, result.Session.Current.HandItems[0].Count);
        Assert.Equal(4, result.Session.Current.ActiveBag.Grid.GetCell(0).Stack!.Count);
    }

    [Fact]
    public void HandleKey_Secondary_PlacesOne()
    {
        var ctrl = ControllerFor(
            "[    ]*[    ] [    ] [    ]\n" +
            "Hand: (Rck5)");
        var result = ctrl.HandleKey(GameKey.Secondary);

        Assert.True(result.Handled);
        // Placed 1, hand has 4
        Assert.Equal(4, result.Session.Current.HandItems[0].Count);
        Assert.Equal(1, result.Session.Current.ActiveBag.Grid.GetCell(0).Stack!.Count);
    }

    // ==================== Sort ====================

    [Fact]
    public void HandleKey_Sort_SortsBag()
    {
        var ctrl = ControllerFor("[Grs3]*[Rck5] [    ] [    ]");
        ctrl.HandleKey(GameKey.Sort);

        // Sorted by (Category, Name): both are Material, Grs < Rck alphabetically
        var grid = ctrl.Session.Current.ActiveBag.Grid;
        Assert.Equal("Grs", grid.GetCell(0).Stack!.ItemType.Name);
        Assert.Equal("Rck", grid.GetCell(1).Stack!.ItemType.Name);
    }

    // ==================== Undo ====================

    [Fact]
    public void HandleKey_Undo_RestoresState()
    {
        var ctrl = ControllerFor("[Rck5]*[    ] [    ] [    ]");

        // Grab (changes state)
        ctrl.HandleKey(GameKey.Primary);
        Assert.True(ctrl.Session.Current.HasItemsInHand);

        // Undo
        var result = ctrl.HandleKey(GameKey.Undo);
        Assert.True(result.Handled);
        Assert.False(result.Session.Current.HasItemsInHand);
        Assert.Equal(5, result.Session.Current.ActiveBag.Grid.GetCell(0).Stack!.Count);
    }

    [Fact]
    public void HandleKey_Undo_NothingToUndo_StillHandled()
    {
        var ctrl = ControllerFor("[    ]*[    ] [    ] [    ]");
        var result = ctrl.HandleKey(GameKey.Undo);

        Assert.True(result.Handled);
        Assert.Equal("Nothing to undo", result.StatusMessage);
    }

    // ==================== Enter / Leave Bag ====================

    [Fact]
    public void HandleKey_Primary_EntersBag()
    {
        // Create a bag-type item at cursor
        var innerBag = new Bag(Grid.Create(3, 2));
        var bagStack = new ItemStack(SmallBag, 1, ContainedBag: innerBag);
        var rootGrid = Grid.Create(4, 3).SetCell(0, new Cell(bagStack));
        var state = new GameState(
            new Bag(rootGrid),
            new Cursor(new Position(0, 0)),
            AllTypes,
            GameState.CreateHandBag());
        var ctrl = new GameController(GameSession.New(state));

        var result = ctrl.HandleKey(GameKey.Primary);

        Assert.True(result.Handled);
        // Should now be nested
        Assert.True(result.Session.Current.IsNested);
        // Cursor reset to (0,0) in inner bag
        Assert.Equal(new Position(0, 0), result.Session.Current.Cursor.Position);
        // Active bag should be the inner bag (3×2)
        Assert.Equal(3, result.Session.Current.ActiveBag.Grid.Columns);
        Assert.Equal(2, result.Session.Current.ActiveBag.Grid.Rows);
    }

    [Fact]
    public void HandleKey_LeaveBag_ReturnsToParent()
    {
        // Enter a bag, then leave
        var innerBag = new Bag(Grid.Create(3, 2));
        var bagStack = new ItemStack(SmallBag, 1, ContainedBag: innerBag);
        var rootGrid = Grid.Create(4, 3).SetCell(0, new Cell(bagStack));
        var state = new GameState(
            new Bag(rootGrid),
            new Cursor(new Position(0, 0)),
            AllTypes,
            GameState.CreateHandBag());
        var ctrl = new GameController(GameSession.New(state));

        // Enter
        ctrl.HandleKey(GameKey.Primary);
        Assert.True(ctrl.Session.Current.IsNested);

        // Leave
        var result = ctrl.HandleKey(GameKey.LeaveBag);
        Assert.True(result.Handled);
        Assert.False(result.Session.Current.IsNested);
        Assert.Equal(4, result.Session.Current.ActiveBag.Grid.Columns);
    }

    [Fact]
    public void HandleKey_LeaveBag_AtRoot_StillHandled()
    {
        var ctrl = ControllerFor("[    ]*[    ] [    ] [    ]");
        var result = ctrl.HandleKey(GameKey.LeaveBag);

        // LeaveBag at root still returns a session (logged as failed)
        Assert.True(result.Handled);
        Assert.False(result.Session.Current.IsNested);
    }

    // ==================== Mouse Click Dispatch ====================

    [Fact]
    public void HandleGridClick_Primary_GrabsAtPosition()
    {
        var ctrl = ControllerFor("[    ] [Rck5] [    ] [    ]");
        var result = ctrl.HandleGridClick(new Position(0, 1), ClickType.Primary);

        Assert.True(result.Handled);
        // Cursor moved to (0,1) and grabbed
        Assert.True(result.Session.Current.HasItemsInHand);
        Assert.Equal(5, result.Session.Current.HandItems[0].Count);
        Assert.True(result.Session.Current.ActiveBag.Grid.GetCell(new Position(0, 1)).IsEmpty);
    }

    [Fact]
    public void HandleGridClick_Secondary_HalfGrabsAtPosition()
    {
        var ctrl = ControllerFor("[    ] [Rck8] [    ] [    ]");
        var result = ctrl.HandleGridClick(new Position(0, 1), ClickType.Secondary);

        Assert.True(result.Handled);
        Assert.True(result.Session.Current.HasItemsInHand);
        Assert.Equal(4, result.Session.Current.HandItems[0].Count);
    }

    [Fact]
    public void HandleGridClick_MovesAndDrops()
    {
        var ctrl = ControllerFor(
            "[    ] [    ] [    ] [    ]\n" +
            "Hand: (Rck5)");
        var result = ctrl.HandleGridClick(new Position(0, 2), ClickType.Primary);

        Assert.True(result.Handled);
        Assert.False(result.Session.Current.HasItemsInHand);
        Assert.Equal(5, result.Session.Current.ActiveBag.Grid.GetCell(new Position(0, 2)).Stack!.Count);
    }

    // ==================== Back Click ====================

    [Fact]
    public void HandleBackClick_LeavesNestedBag()
    {
        var innerBag = new Bag(Grid.Create(3, 2));
        var bagStack = new ItemStack(SmallBag, 1, ContainedBag: innerBag);
        var rootGrid = Grid.Create(4, 3).SetCell(0, new Cell(bagStack));
        var state = new GameState(
            new Bag(rootGrid),
            new Cursor(new Position(0, 0)),
            AllTypes,
            GameState.CreateHandBag());
        var ctrl = new GameController(GameSession.New(state));

        // Enter
        ctrl.HandleKey(GameKey.Primary);
        Assert.True(ctrl.Session.Current.IsNested);

        // Back click
        var result = ctrl.HandleBackClick();
        Assert.True(result.Handled);
        Assert.False(result.Session.Current.IsNested);
    }

    // ==================== Tick ====================

    [Fact]
    public void Tick_NoRecipes_NoChange()
    {
        var ctrl = ControllerFor("[    ]*[    ] [    ] [    ]");
        var result = ctrl.Tick();

        Assert.False(result.Handled); // No change
    }

    // ==================== Controller State Consistency ====================

    [Fact]
    public void Session_UpdatesAfterEachAction()
    {
        var ctrl = ControllerFor("[Rck5]*[Grs3] [    ] [    ]");

        var s1 = ctrl.Session;
        ctrl.HandleKey(GameKey.Right);
        var s2 = ctrl.Session;
        Assert.NotEqual(s1, s2); // Cursor moved

        ctrl.HandleKey(GameKey.Primary);
        var s3 = ctrl.Session;
        Assert.NotEqual(s2, s3); // Grabbed item
    }

    [Fact]
    public void MultipleUndos_RestoreSequentially()
    {
        var ctrl = ControllerFor("[Rck5]*[Grs3] [    ] [    ]");

        // Grab Rck
        ctrl.HandleKey(GameKey.Primary);
        // Move right
        ctrl.HandleKey(GameKey.Right);
        // Drop Rck at (0,1) — merges with Grs? No, different type, will swap or fail
        // Actually drop at empty cell — move right again
        ctrl.HandleKey(GameKey.Right);
        // Drop at (0,2)
        ctrl.HandleKey(GameKey.Primary);
        Assert.False(ctrl.Session.Current.HasItemsInHand);

        // Undo drop
        ctrl.HandleKey(GameKey.Undo);
        Assert.True(ctrl.Session.Current.HasItemsInHand);

        // Undo grab
        ctrl.HandleKey(GameKey.Undo);
        Assert.False(ctrl.Session.Current.HasItemsInHand);
        Assert.Equal(5, ctrl.Session.Current.ActiveBag.Grid.GetCell(0).Stack!.Count);
    }

    // ==================== QuickSplit via Controller ====================

    [Fact]
    public void HandleKey_QuickSplit_SplitsStack()
    {
        var ctrl = ControllerFor("[Rck8]*[    ] [    ] [    ]");
        var result = ctrl.HandleKey(GameKey.QuickSplit);

        Assert.True(result.Handled);
        Assert.True(result.Session.Current.HasItemsInHand);
        // Split 8: left=4, right=4
        Assert.Equal(4, result.Session.Current.HandItems[0].Count);
        Assert.Equal(4, result.Session.Current.ActiveBag.Grid.GetCell(0).Stack!.Count);
    }
}
