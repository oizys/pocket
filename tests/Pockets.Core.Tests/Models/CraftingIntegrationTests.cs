using System.Collections.Immutable;
using Pockets.Core.Models;
using Pockets.Core.Data;

namespace Pockets.Core.Tests.Models;

public class CraftingIntegrationTests
{
    private static readonly ItemType Rock = new("Plain Rock", Category.Material, IsStackable: true, MaxStackSize: 20);
    private static readonly ItemType Wood = new("Rough Wood", Category.Material, IsStackable: true, MaxStackSize: 20);
    private static readonly ItemType StoneAxe = new("Stone Axe", Category.Tool, IsStackable: false);
    private static readonly ItemType WorkbenchType = new("Workbench", Category.Structure, IsStackable: false);

    private static readonly ImmutableArray<ItemType> AllTypes =
        ImmutableArray.Create(Rock, Wood, StoneAxe, WorkbenchType);

    private static readonly Recipe AxeRecipe = new(
        "workbench_axe", "Stone Axe",
        new[] { new RecipeInput(Rock, 5), new RecipeInput(Wood, 3) },
        () => new[] { new ItemStack(StoneAxe, 1) },
        Duration: 3);

    private static readonly ImmutableArray<Recipe> Recipes = ImmutableArray.Create(AxeRecipe);

    /// <summary>
    /// Creates a game state with a workbench facility and crafting materials in the root bag.
    /// </summary>
    private static GameState CreateStateWithWorkbench()
    {
        var workbench = FacilityBuilder.CreateWorkbench(AxeRecipe);
        var rootGrid = Grid.Create(8, 4);

        // Place workbench at cell 0
        rootGrid = rootGrid.SetCell(0, new Cell(new ItemStack(WorkbenchType, 1, ContainedBag: workbench)));
        // Place 5 Rock at cell 1
        rootGrid = rootGrid.SetCell(1, new Cell(new ItemStack(Rock, 5)));
        // Place 3 Wood at cell 2
        rootGrid = rootGrid.SetCell(2, new Cell(new ItemStack(Wood, 3)));

        var root = new Bag(rootGrid);
        return new GameState(root, new Cursor(new Position(0, 0)), AllTypes, GameState.CreateHandBag());
    }

    [Fact]
    public void ReplaceBagById_UpdatesFacilityInTree()
    {
        var state = CreateStateWithWorkbench();
        var workbench = state.RootBag.Grid.GetCell(0).Stack!.ContainedBag!;

        // Modify the workbench facility state and use ownerTransform to set Progress on the owning ItemStack
        var modified = workbench with { FacilityState = new FacilityState(RecipeId: "test") };
        var updated = state.ReplaceBagById(workbench.Id, modified,
            ownerTransform: stack => stack.WithProperty("Progress", new IntValue(42)));

        var foundStack = updated.RootBag.Grid.GetCell(0).Stack!;
        var found = foundStack.ContainedBag!;
        Assert.Equal("test", found.FacilityState!.RecipeId);
        Assert.Equal(42, foundStack.GetInt("Progress"));
    }

    [Fact]
    public void Session_TickIncrements_OnAction()
    {
        var state = CreateStateWithWorkbench();
        var session = GameSession.New(state, Recipes, TickMode.Rogue);

        Assert.Equal(0, session.TickCount);

        // Move cursor to Rock (cell 1) and grab
        session = session.MoveCursor(Direction.Right);
        Assert.Equal(0, session.TickCount); // move is not undoable

        session = session.ExecutePrimary(); // grab rock
        Assert.Equal(1, session.TickCount);
    }

    [Fact]
    public void FullCraftingCycle_WorkbenchMakesStoneAxe()
    {
        var state = CreateStateWithWorkbench();
        var session = GameSession.New(state, Recipes, TickMode.Rogue);

        // Step 1: Grab Rock (cursor at cell 0 = workbench, move to cell 1 = rock)
        session = session.MoveCursor(Direction.Right); // → cell (0,1) = Rock
        session = session.ExecutePrimary(); // grab 5 Rock

        // Step 2: Enter workbench — move cursor to cell 0
        session = session.MoveCursor(Direction.Left); // → cell (0,0) = Workbench
        session = session.ExecutePrimary(); // enter workbench

        // Step 3: Drop Rock in input slot 0
        // Cursor should be at (0,0) inside workbench = input slot 1
        Assert.True(session.Current.IsNested);
        session = session.ExecutePrimary(); // drop rock in input slot

        // Step 4: Leave workbench, grab Wood
        session = session.ExecuteLeaveBag();
        session = session.MoveCursor(Direction.Right);
        session = session.MoveCursor(Direction.Right); // → cell (0,2) = Wood
        session = session.ExecutePrimary(); // grab 3 Wood

        // Step 5: Enter workbench again
        session = session.MoveCursor(Direction.Left);
        session = session.MoveCursor(Direction.Left); // → cell (0,0) = Workbench
        session = session.ExecutePrimary(); // enter workbench

        // Step 6: Move to input slot 1, drop wood
        session = session.MoveCursor(Direction.Right); // → input slot 2
        session = session.ExecutePrimary(); // drop wood

        // Now both inputs are filled. Each action ticks the facility.
        // Recipe duration is 3, so we need 3 more actions/ticks.
        // The ticks have been accumulating — let's check the workbench state.

        // Do 3 actions to advance ticks (sort is a handy no-impact action at root)
        session = session.ExecuteLeaveBag();

        // We need actions that produce ticks. Let's do sort 3 times.
        session = session.ExecuteSort();
        session = session.ExecuteSort();
        session = session.ExecuteSort();

        // Check: workbench output should now have Stone Axe
        var workbenchCell = session.Current.RootBag.Grid.GetCell(0);
        var workbenchBag = workbenchCell.Stack!.ContainedBag!;
        var outputCell = workbenchBag.Grid.GetCell(2); // output slot

        Assert.False(outputCell.IsEmpty, "Output slot should contain Stone Axe");
        Assert.Equal(StoneAxe, outputCell.Stack!.ItemType);
    }
}
