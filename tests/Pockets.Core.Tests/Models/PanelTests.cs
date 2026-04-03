using System.Collections.Immutable;
using Pockets.Core.Models;

namespace Pockets.Core.Tests.Models;

public class PanelTests
{
    private static readonly ItemType Rock = new("Rock", Category.Material, IsStackable: true, MaxStackSize: 20);
    private static readonly ItemType FacilityType = new("Workbench", Category.Structure, IsStackable: false);
    private static readonly ItemType WildType = new("Forest Bag", Category.Bag, IsStackable: false);
    private static readonly ImmutableArray<ItemType> Types = ImmutableArray.Create(Rock, FacilityType, WildType);

    private static GameState MakeStateWithFacilityAndWild()
    {
        var facilityBag = new Bag(Grid.Create(3, 1), "Workbench", FacilityState: new FacilityState());
        var wildBag = new Bag(Grid.Create(6, 4), "Forest");

        var grid = Grid.Create(8, 4)
            .SetCell(0, new Cell(new ItemStack(FacilityType, 1, ContainedBagId: facilityBag.Id)))
            .SetCell(1, new Cell(new ItemStack(WildType, 1, ContainedBagId: wildBag.Id)))
            .SetCell(2, new Cell(new ItemStack(Rock, 10)));

        var rootBag = new Bag(grid);
        var handBag = GameState.CreateHandBag();
        var toolbarBag = new Bag(Grid.Create(10, 1), "Toolbar");
        var store = BagStore.Empty
            .Add(rootBag).Add(handBag).Add(toolbarBag)
            .Add(facilityBag).Add(wildBag);

        var locations = LocationMap.Create(handBag.Id, rootBag.Id)
            .Set(LocationId.T, Location.AtOrigin(toolbarBag.Id));

        return new GameState(store, locations, Types);
    }

    // ==================== OpenAsContainer ====================

    [Fact]
    public void OpenAsContainer_AddsCLocation()
    {
        var state = MakeStateWithFacilityAndWild();
        var facilityBagId = state.CurrentCell.Stack!.ContainedBagId!.Value;
        var result = state.OpenAsContainer(facilityBagId);

        Assert.True(result.Success);
        Assert.True(result.State.Locations.Has(LocationId.C));
        var cLoc = result.State.Locations.Get(LocationId.C);
        Assert.Equal(facilityBagId, cLoc.BagId);
    }

    [Fact]
    public void OpenAsContainer_FailsIfAlreadyOpen()
    {
        var state = MakeStateWithFacilityAndWild();
        var bagId = state.CurrentCell.Stack!.ContainedBagId!.Value;
        var opened = state.OpenAsContainer(bagId).State;
        var result = opened.OpenAsContainer(bagId);

        Assert.False(result.Success);
        Assert.Contains("already open", result.Error!);
    }

    // ==================== OpenAsWorld ====================

    [Fact]
    public void OpenAsWorld_AddsWLocation()
    {
        var state = MakeStateWithFacilityAndWild();
        state = state.MoveCursor(Direction.Right); // move to wilderness bag cell
        var wildBagId = state.CurrentCell.Stack!.ContainedBagId!.Value;
        var result = state.OpenAsWorld(wildBagId);

        Assert.True(result.Success);
        Assert.True(result.State.Locations.Has(LocationId.W));
    }

    // ==================== ClosePanel ====================

    [Fact]
    public void ClosePanel_RemovesCLocation()
    {
        var state = MakeStateWithFacilityAndWild();
        var bagId = state.CurrentCell.Stack!.ContainedBagId!.Value;
        var opened = state.OpenAsContainer(bagId).State;

        var result = opened.ClosePanel(LocationId.C);
        Assert.True(result.Success);
        Assert.False(result.State.Locations.Has(LocationId.C));
    }

    [Fact]
    public void ClosePanel_CannotCloseB()
    {
        var state = MakeStateWithFacilityAndWild();
        var result = state.ClosePanel(LocationId.B);
        Assert.False(result.Success);
    }

    [Fact]
    public void ClosePanel_CannotCloseH()
    {
        var state = MakeStateWithFacilityAndWild();
        var result = state.ClosePanel(LocationId.H);
        Assert.False(result.Success);
    }

    // ==================== Toolbar ====================

    [Fact]
    public void CreateStage1_HasToolbar()
    {
        var state = GameState.CreateStage1(Types, Array.Empty<ItemStack>());
        Assert.True(state.Locations.Has(LocationId.T));
        var tBag = state.Store.GetById(state.Locations.Get(LocationId.T).BagId)!;
        Assert.Equal(10, tBag.Grid.Columns);
        Assert.Equal(1, tBag.Grid.Rows);
    }

    // ==================== Focus (via GameController) ====================

    [Fact]
    public void Focus_DefaultsToB()
    {
        var state = MakeStateWithFacilityAndWild();
        var session = GameSession.New(state);
        var controller = new GameController(session);

        Assert.Equal(LocationId.B, controller.Focus);
    }

    [Fact]
    public void FocusNext_CyclesThroughOpenPanels()
    {
        var state = MakeStateWithFacilityAndWild();
        var session = GameSession.New(state);
        var controller = new GameController(session);

        // Only T and B are open. Tab should cycle B → W (not open) → skip → T
        controller.HandleKey(GameKey.FocusNext);
        // T is before B in cycle order, so from B it should go to next open: T (wrapping)
        // FocusCycleOrder = { T, C, B, W }. Open: T, B. Current: B (index 2 in open=[T,B]).
        // Next: (1+1) % 2 = 0 → T
        Assert.Equal(LocationId.T, controller.Focus);

        controller.HandleKey(GameKey.FocusNext);
        Assert.Equal(LocationId.B, controller.Focus);
    }

    [Fact]
    public void FocusPrev_CyclesBackward()
    {
        var state = MakeStateWithFacilityAndWild();
        var session = GameSession.New(state);
        var controller = new GameController(session);

        controller.HandleKey(GameKey.FocusPrev);
        Assert.Equal(LocationId.T, controller.Focus); // B → prev → T
    }

    [Fact]
    public void MoveCursorAt_MovesFocusedPanel()
    {
        var state = MakeStateWithFacilityAndWild();
        var session = GameSession.New(state);
        var controller = new GameController(session);

        // Focus on T, move right
        controller.SetFocus(LocationId.T);
        controller.HandleKey(GameKey.Right);

        var tLoc = controller.Session.Current.Locations.Get(LocationId.T);
        Assert.Equal(new Position(0, 1), tLoc.Cursor.Position);

        // B cursor should be unchanged
        Assert.Equal(new Position(0, 0), controller.Session.Current.Cursor.Position);
    }

    // ==================== Primary opens panels ====================

    [Fact]
    public void Primary_OnFacility_OpensAsContainer()
    {
        var state = MakeStateWithFacilityAndWild();
        var session = GameSession.New(state);
        var controller = new GameController(session);

        // Cursor is on cell 0 (facility)
        controller.HandleKey(GameKey.Primary);

        Assert.True(controller.Session.Current.Locations.Has(LocationId.C));
        Assert.Equal(LocationId.C, controller.Focus);
    }

    [Fact]
    public void Primary_OnWilderness_OpensAsWorld()
    {
        var state = MakeStateWithFacilityAndWild();
        var session = GameSession.New(state);
        var controller = new GameController(session);

        // Move to cell 1 (wilderness)
        controller.HandleKey(GameKey.Right);
        controller.HandleKey(GameKey.Primary);

        Assert.True(controller.Session.Current.Locations.Has(LocationId.W));
        Assert.Equal(LocationId.W, controller.Focus);
    }

    [Fact]
    public void LeaveBag_WhenFocusedOnC_ClosesPanel()
    {
        var state = MakeStateWithFacilityAndWild();
        var session = GameSession.New(state);
        var controller = new GameController(session);

        // Open facility as C
        controller.HandleKey(GameKey.Primary);
        Assert.Equal(LocationId.C, controller.Focus);

        // Q closes C and returns focus to B
        controller.HandleKey(GameKey.LeaveBag);
        Assert.False(controller.Session.Current.Locations.Has(LocationId.C));
        Assert.Equal(LocationId.B, controller.Focus);
    }
}
