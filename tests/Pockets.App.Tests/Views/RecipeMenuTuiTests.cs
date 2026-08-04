using System;
using System.Collections.Immutable;
using Terminal.Gui;
using Pockets.Core.Models;
using Pockets.App.Views;

namespace Pockets.App.Tests.Views;

/// <summary>
/// The modal recipe menu (TUI render prong, playtest feature): opening it on a facility draws a real
/// centered modal listing the known recipes; the game logic is proven at the Core level
/// (PlaytestFixesTests). Aaron reversed the no-modal rule — this is the "TUI real" half of item 5.
/// </summary>
public class RecipeMenuTuiTests : IDisposable
{
    private TuiTestHarness? _harness;

    private static readonly ItemType Grass = new("Dry Grass", Category.Material, IsStackable: true);
    private static readonly ItemType TableType = new("Crafting Table", Category.Structure, IsStackable: false);

    private static Recipe R(string id, string name) =>
        new(id, name, new[] { new RecipeInput(Grass, 1) },
            () => RecipeOutput.FromStacks(Array.Empty<ItemStack>()), Duration: 3);

    private static (GameState State, ImmutableArray<Recipe> Recipes) MakeState()
    {
        var tableGrid = Grid.Create(3, 1)
            .SetCell(0, new Cell(Frame: new InputSlotFrame("in1")))
            .SetCell(1, new Cell(Frame: new InputSlotFrame("in2")))
            .SetCell(2, new Cell(Frame: new OutputSlotFrame("out1")));
        var table = new Bag(tableGrid, GameState.CraftingTableEnvironment, "Brown",
            FacilityState: new FacilityState(RequiresSelectedRecipe: true));
        var grid = Grid.Create(2, 1).SetCell(0, new Cell(new ItemStack(TableType, 1, ContainedBagId: table.Id)));
        var root = new Bag(grid);
        var hand = GameState.CreateHandBag(1);
        var toolbar = new Bag(Grid.Create(4, 1), "Toolbar");
        var store = BagStore.Empty.Add(root).Add(hand).Add(toolbar).Add(table);
        var locations = LocationMap.Create(hand.Id, root.Id).Set(LocationId.T, Location.AtOrigin(toolbar.Id));
        var state = new GameState(store, locations, ImmutableArray.Create(Grass, TableType))
        { KnownRecipes = ImmutableHashSet.Create("compass", "belt-pouch") };
        return (state, ImmutableArray.Create(R("compass", "Quiet Compass"), R("belt-pouch", "Belt Pouch")));
    }

    private GameView Setup()
    {
        _harness = TuiTestHarness.Create();
        var (state, recipes) = MakeState();
        var view = new GameView(state, recipes, enableTickTimer: false)
            { X = 0, Y = 0, Width = Dim.Fill(), Height = Dim.Fill() };
        _harness.AddView(view);
        _harness.Render();
        return view;
    }

    private void SendKey(GameView view, Key key)
    {
        view.ProcessKey(new KeyEvent(key, new KeyModifiers()));
        _harness!.Render();
    }

    public void Dispose() => _harness?.Dispose();

    [Fact]
    public void OpeningTheMenu_DrawsAModalListingKnownRecipes()
    {
        var view = Setup();
        SendKey(view, (Key)'e'); // Primary on the facility → open it as a C look-in
        SendKey(view, (Key)'r'); // open the recipe menu

        Assert.NotNull(view.Controller.Session.RecipeMenu);
        var dump = _harness!.DumpBuffer();
        Assert.Contains("Quiet Compass", dump);
        Assert.Contains("Belt Pouch", dump);
        Assert.Contains("select", dump); // the ↑/↓ select · Enter set hint
    }

    [Fact]
    public void SelectingARecipe_ClosesTheModal()
    {
        var view = Setup();
        SendKey(view, (Key)'e');
        SendKey(view, (Key)'r');
        Assert.NotNull(view.Controller.Session.RecipeMenu);

        SendKey(view, Key.Enter); // confirm

        Assert.Null(view.Controller.Session.RecipeMenu); // modal closed
    }
}
