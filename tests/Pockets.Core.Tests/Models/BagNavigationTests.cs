using System.Collections.Immutable;
using Pockets.Core.Models;
using Pockets.Core.Rendering;

namespace Pockets.Core.Tests.Models;

public class BagNavigationTests
{
    private static readonly ItemType Rck = new("Rck", Category.Material, IsStackable: true, MaxStackSize: 9);
    private static readonly ItemType Swd = new("Swd", Category.Weapon, IsStackable: false);
    private static readonly ItemType SmallBag = new("Small Bag", Category.Bag, IsStackable: false);

    private static readonly ImmutableArray<ItemType> AllTypes =
        ImmutableArray.Create(Rck, Swd, SmallBag);

    /// <summary>
    /// Creates a game state with an inner bag at root cell (0,0).
    /// The inner bag is a 3×2 grid, optionally pre-filled.
    /// </summary>
    private static GameState CreateWithInnerBag(
        IEnumerable<ItemStack>? innerContents = null,
        IEnumerable<ItemStack>? rootContents = null)
    {
        // Create inner bag
        var innerGrid = Grid.Create(3, 2);
        var innerBag = new Bag(innerGrid, "Cave", "Dark");
        if (innerContents != null)
        {
            var (filled, _) = innerBag.AcquireItems(innerContents);
            innerBag = filled;
        }

        // Create root bag with the inner bag at cell 0
        var rootGrid = Grid.Create(4, 3);
        var bagCell = new Cell(new ItemStack(SmallBag, 1, ContainedBag: innerBag));
        rootGrid = rootGrid.SetCell(0, bagCell);

        if (rootContents != null)
        {
            // Place other items starting at cell 1
            var (filledGrid, _) = rootGrid.AcquireItems(rootContents,
                ImmutableHashSet.Create(0)); // skip cell 0 (bag)
            rootGrid = filledGrid;
        }

        var rootBag = new Bag(rootGrid);
        return new GameState(rootBag, new Cursor(new Position(0, 0)), AllTypes, GameState.CreateHandBag());
    }

    // ==================== EnterBag ====================

    [Fact]
    public void EnterBag_CursorOnBag_EntersBag()
    {
        var state = CreateWithInnerBag();
        var result = state.EnterBag();

        Assert.True(result.Success);
        Assert.True(result.State.IsNested);
        // Active bag should be the inner bag (3×2)
        Assert.Equal(3, result.State.ActiveBag.Grid.Columns);
        Assert.Equal(2, result.State.ActiveBag.Grid.Rows);
        // Cursor reset to (0,0)
        Assert.Equal(new Position(0, 0), result.State.Cursor.Position);
    }

    [Fact]
    public void EnterBag_NoBagAtCursor_Fails()
    {
        var state = CreateWithInnerBag();
        // Move cursor to empty cell
        state = state.MoveCursor(Direction.Right);
        var result = state.EnterBag();

        Assert.False(result.Success);
        Assert.Contains("No bag at cursor", result.Error);
    }

    [Fact]
    public void EnterBag_SavesCursorPosition()
    {
        var state = CreateWithInnerBag();
        var result = state.EnterBag();

        // Breadcrumb should save cursor at (0,0)
        var entry = result.State.BreadcrumbStack.Peek();
        Assert.Equal(new Position(0, 0), entry.SavedCursor.Position);
    }

    [Fact]
    public void EnterBag_InnerBagHasContents()
    {
        var innerItems = new[] { new ItemStack(Rck, 5), new ItemStack(Swd, 1) };
        var state = CreateWithInnerBag(innerContents: innerItems);
        var result = state.EnterBag();

        Assert.True(result.Success);
        var activeBag = result.State.ActiveBag;
        Assert.False(activeBag.Grid.GetCell(0).IsEmpty);
        Assert.Equal(Rck, activeBag.Grid.GetCell(0).Stack!.ItemType);
        Assert.Equal(5, activeBag.Grid.GetCell(0).Stack!.Count);
    }

    [Fact]
    public void EnterBag_BreadcrumbPath_ShowsTrail()
    {
        var state = CreateWithInnerBag();
        var result = state.EnterBag();

        var path = result.State.BreadcrumbPath;
        Assert.Equal(2, path.Count);
        Assert.Equal("Default", path[0]); // root bag environment
        Assert.Equal("Small Bag", path[1]); // inner bag item name
    }

    // ==================== LeaveBag ====================

    [Fact]
    public void LeaveBag_AtRoot_Fails()
    {
        var state = CreateWithInnerBag();
        var result = state.LeaveBag();

        Assert.False(result.Success);
        Assert.Contains("root", result.Error);
    }

    [Fact]
    public void LeaveBag_RestoresCursorAndBag()
    {
        var state = CreateWithInnerBag();
        var entered = state.EnterBag().State;

        // Move cursor inside inner bag
        var moved = entered.MoveCursor(Direction.Right);
        Assert.Equal(new Position(0, 1), moved.Cursor.Position);

        // Leave bag
        var left = moved.LeaveBag();
        Assert.True(left.Success);
        Assert.False(left.State.IsNested);

        // Cursor restored to (0,0) where the bag was
        Assert.Equal(new Position(0, 0), left.State.Cursor.Position);
        // Active bag is root (4×3)
        Assert.Equal(4, left.State.ActiveBag.Grid.Columns);
        Assert.Equal(3, left.State.ActiveBag.Grid.Rows);
    }

    // ==================== Tools inside nested bag ====================

    [Fact]
    public void Grab_InsideInnerBag_RemovesFromInnerBag()
    {
        var innerItems = new[] { new ItemStack(Rck, 5) };
        var state = CreateWithInnerBag(innerContents: innerItems);
        var entered = state.EnterBag().State;

        var result = entered.ToolGrab();

        Assert.True(result.Success);
        // Inner bag cell 0 should be empty
        Assert.True(result.State.ActiveBag.Grid.GetCell(0).IsEmpty);
        // Item in hand
        Assert.Single(result.State.HandItems);
        Assert.Equal(Rck, result.State.HandItems[0].ItemType);
    }

    [Fact]
    public void Drop_InsideInnerBag_PlacesInInnerBag()
    {
        var state = CreateWithInnerBag();
        // Put something in hand first
        var rootItems = new[] { new ItemStack(Rck, 3) };
        state = CreateWithInnerBag(rootContents: rootItems);
        // Move cursor to Rck and grab
        state = state.MoveCursor(Direction.Right);
        var grabbed = state.ToolGrab().State;
        // Move back to bag cell and enter
        grabbed = grabbed.MoveCursor(Direction.Left);
        var entered = grabbed.EnterBag().State;

        // Drop inside inner bag
        var result = entered.ToolDrop();

        Assert.True(result.Success);
        Assert.False(result.State.HasItemsInHand);
        Assert.Equal(Rck, result.State.ActiveBag.Grid.GetCell(0).Stack!.ItemType);
    }

    [Fact]
    public void Changes_InsideInnerBag_PropagateToRoot()
    {
        var innerItems = new[] { new ItemStack(Rck, 5) };
        var state = CreateWithInnerBag(innerContents: innerItems);
        var entered = state.EnterBag().State;

        // Grab from inner bag
        var grabbed = entered.ToolGrab().State;

        // Leave bag
        var left = grabbed.LeaveBag().State;

        // Root bag cell 0 should still have the bag item
        Assert.False(left.RootBag.Grid.GetCell(0).IsEmpty);
        Assert.Equal(SmallBag, left.RootBag.Grid.GetCell(0).Stack!.ItemType);

        // But the inner bag should now have an empty cell 0
        var innerBag = left.RootBag.Grid.GetCell(0).Stack!.ContainedBag!;
        Assert.True(innerBag.Grid.GetCell(0).IsEmpty);
    }

    [Fact]
    public void AcquireRandom_InsideInnerBag_PlacesInInnerBag()
    {
        var state = CreateWithInnerBag();
        var entered = state.EnterBag().State;

        var result = entered.ToolAcquireRandom(new Random(42));

        Assert.True(result.Success);
        var activeBag = result.State.ActiveBag;
        var nonEmpty = activeBag.Grid.Cells.Count(c => !c.IsEmpty);
        Assert.Equal(1, nonEmpty);
    }

    // ==================== Cursor wrapping uses active bag dimensions ====================

    [Fact]
    public void MoveCursor_InsideInnerBag_WrapsToInnerBagSize()
    {
        var state = CreateWithInnerBag();
        var entered = state.EnterBag().State;

        // Inner bag is 3×2, moving right 3 times should wrap
        var moved = entered
            .MoveCursor(Direction.Right)
            .MoveCursor(Direction.Right)
            .MoveCursor(Direction.Right);

        Assert.Equal(new Position(0, 0), moved.Cursor.Position);
    }

    // ==================== Sort preserves bag contents ====================

    [Fact]
    public void Sort_PreservesContainedBag()
    {
        // Put some unsorted items alongside the bag so sort actually moves things
        var innerItems = new[] { new ItemStack(Rck, 3) };
        var rootItems = new[] { new ItemStack(Swd, 1), new ItemStack(Rck, 2) };
        var state = CreateWithInnerBag(innerContents: innerItems, rootContents: rootItems);

        var sorted = state.ToolSort();
        Assert.True(sorted.Success);

        // Find the bag item in the sorted grid
        var bagCell = sorted.State.RootBag.Grid.Cells
            .FirstOrDefault(c => c.Stack?.ItemType == SmallBag);
        Assert.NotNull(bagCell);
        Assert.NotNull(bagCell!.Stack!.ContainedBag);
        Assert.Equal(3, bagCell.Stack.ContainedBag!.Grid.Columns);

        // Contents should still be there
        Assert.Equal(Rck, bagCell.Stack.ContainedBag.Grid.GetCell(0).Stack!.ItemType);
    }

    [Fact]
    public void Sort_ThenEnterBag_Works()
    {
        var innerItems = new[] { new ItemStack(Rck, 3) };
        var rootItems = new[] { new ItemStack(Swd, 1) };
        var state = CreateWithInnerBag(innerContents: innerItems, rootContents: rootItems);

        // Sort moves things around
        var sorted = state.ToolSort().State;

        // Find where the bag ended up and move cursor there
        var grid = sorted.RootBag.Grid;
        Position? bagPos = null;
        for (int r = 0; r < grid.Rows; r++)
            for (int c = 0; c < grid.Columns; c++)
            {
                var pos = new Position(r, c);
                if (grid.GetCell(pos).Stack?.ItemType == SmallBag)
                    bagPos = pos;
            }
        Assert.NotNull(bagPos);

        // Move cursor to bag position
        sorted = sorted with { Cursor = new Cursor(bagPos!.Value) };
        var entered = sorted.EnterBag();
        Assert.True(entered.Success);
        Assert.True(entered.State.IsNested);
        Assert.Equal(Rck, entered.State.ActiveBag.Grid.GetCell(0).Stack!.ItemType);
    }

    // ==================== Grab/Drop bag items preserves contents ====================

    [Fact]
    public void GrabBagItem_PreservesContainedBag()
    {
        var innerItems = new[] { new ItemStack(Rck, 5), new ItemStack(Swd, 1) };
        var state = CreateWithInnerBag(innerContents: innerItems);

        // Grab the bag item from cell 0
        var grabbed = state.ToolGrab();
        Assert.True(grabbed.Success);

        // The bag item is now in hand — verify it still has its ContainedBag
        var handItem = grabbed.State.HandItems[0];
        Assert.Equal(SmallBag, handItem.ItemType);
        Assert.NotNull(handItem.ContainedBag);
        Assert.Equal(3, handItem.ContainedBag!.Grid.Columns);

        // Contents should still be there
        var innerBag = handItem.ContainedBag;
        Assert.Equal(Rck, innerBag.Grid.GetCell(0).Stack!.ItemType);
        Assert.Equal(5, innerBag.Grid.GetCell(0).Stack!.Count);
    }

    [Fact]
    public void GrabAndDropBagItem_RoundTrip_PreservesContents()
    {
        var innerItems = new[] { new ItemStack(Rck, 5) };
        var state = CreateWithInnerBag(innerContents: innerItems);

        // Grab bag from cell 0
        var grabbed = state.ToolGrab().State;
        // Move cursor to empty cell and drop
        grabbed = grabbed.MoveCursor(Direction.Right);
        var dropped = grabbed.ToolDrop();

        Assert.True(dropped.Success);
        // Bag should now be at cell (0,1)
        var cell = dropped.State.RootBag.Grid.GetCell(new Position(0, 1));
        Assert.Equal(SmallBag, cell.Stack!.ItemType);
        Assert.NotNull(cell.Stack.ContainedBag);

        // Enter the bag and verify contents survived the move
        var movedState = dropped.State;
        var entered = movedState.EnterBag();
        Assert.True(entered.Success);
        Assert.Equal(Rck, entered.State.ActiveBag.Grid.GetCell(0).Stack!.ItemType);
        Assert.Equal(5, entered.State.ActiveBag.Grid.GetCell(0).Stack!.Count);
    }

    // ==================== Enter/Leave roundtrip via GameSession ====================

    [Fact]
    public void Session_EnterLeave_Undoable()
    {
        var state = CreateWithInnerBag();
        var session = GameSession.New(state);

        session = session.ExecuteEnterBag();
        Assert.True(session.Current.IsNested);
        Assert.Equal(1, session.UndoDepth);

        session = session.ExecuteLeaveBag();
        Assert.False(session.Current.IsNested);
        Assert.Equal(2, session.UndoDepth);

        // Undo leave → back inside
        session = session.Undo()!;
        Assert.True(session.Current.IsNested);

        // Undo enter → back at root
        session = session.Undo()!;
        Assert.False(session.Current.IsNested);
    }
}
