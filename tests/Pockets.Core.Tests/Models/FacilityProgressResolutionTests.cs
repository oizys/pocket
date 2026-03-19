using System.Collections.Immutable;
using Pockets.Core.Models;
using Pockets.Core.Data;
using Xunit;

namespace Pockets.Core.Tests.Models;

/// <summary>
/// Validates three patterns for reading facility progress now that it lives
/// on the owning ItemStack as a ("Progress", IntValue) property.
/// </summary>
public class FacilityProgressResolutionTests
{
    private static readonly ItemType Rock = new("Plain Rock", Category.Material, true);
    private static readonly ItemType Wood = new("Rough Wood", Category.Material, true);
    private static readonly ItemType Axe = new("Stone Axe", Category.Weapon, false);
    private static readonly ItemType WorkbenchItem = new("Workbench", Category.Structure, false);

    private static readonly Recipe AxeRecipe = new(
        "axe", "Stone Axe",
        new[] { new RecipeInput(Rock, 5), new RecipeInput(Wood, 3) },
        () => new List<ItemStack> { new(Axe, 1) },
        Duration: 3);

    private static readonly ImmutableDictionary<string, ImmutableArray<string>> FacilityRecipeMap =
        ImmutableDictionary<string, ImmutableArray<string>>.Empty
            .Add("Workbench", ImmutableArray.Create("axe"));

    /// <summary>
    /// Creates a game state with a workbench facility at a known cell index in the root bag.
    /// The workbench has inputs loaded and is ready to craft.
    /// </summary>
    private static (GameState State, int FacilityCellIndex) CreateStateWithFacility()
    {
        // Build facility bag (3x1: 2 inputs + 1 output)
        var facilityGrid = Grid.Create(3, 1);
        var facilityBuilder = facilityGrid.Cells.ToBuilder();
        facilityBuilder[0] = new Cell(new ItemStack(Rock, 5), Frame: new InputSlotFrame("in1", ItemTypeFilter: Rock));
        facilityBuilder[1] = new Cell(new ItemStack(Wood, 3), Frame: new InputSlotFrame("in2", ItemTypeFilter: Wood));
        facilityBuilder[2] = new Cell(Frame: new OutputSlotFrame("out1"));
        facilityGrid = facilityGrid with { Cells = facilityBuilder.MoveToImmutable() };
        var facilityBag = new Bag(facilityGrid, "Workbench", "Brown",
            FacilityState: new FacilityState(ActiveRecipeId: "axe"));

        // Build root bag (4x2) with the workbench item at cell 0
        var rootGrid = Grid.Create(4, 2);
        var workbenchStack = new ItemStack(WorkbenchItem, 1, ContainedBag: facilityBag);
        rootGrid = rootGrid.SetCell(0, new Cell(workbenchStack));

        var handBag = new Bag(Grid.Create(1, 1));
        var itemTypes = ImmutableArray.Create(Rock, Wood, Axe, WorkbenchItem);
        var state = new GameState(new Bag(rootGrid), new Cursor(new Position(0, 0)), itemTypes, handBag);

        return (state, 0);
    }

    // ============================================
    // Pattern 1: Current bag via breadcrumbs
    // ============================================

    [Fact]
    public void Breadcrumbs_CanResolveOwningStackProgress()
    {
        var (state, facilityCellIndex) = CreateStateWithFacility();

        // Set progress on the owning ItemStack
        var ownerStack = state.RootBag.Grid.GetCell(facilityCellIndex).Stack!;
        var updatedStack = ownerStack.WithProperty("Progress", new IntValue(2));
        var updatedRoot = state.RootBag with
        {
            Grid = state.RootBag.Grid.SetCell(facilityCellIndex, new Cell(updatedStack))
        };
        state = state with { RootBag = updatedRoot };

        // Enter the facility bag (creates a breadcrumb)
        var enterResult = state.EnterBag();
        Assert.True(enterResult.Success);
        var nested = enterResult.State;
        Assert.True(nested.IsNested);

        // Resolve progress via breadcrumbs: peek to get parent cell index
        var breadcrumb = nested.BreadcrumbStack.Peek();
        Assert.Equal(facilityCellIndex, breadcrumb.CellIndex);

        // Walk from root to parent bag (depth - 1). For depth 1, parent is root.
        var parentBag = nested.RootBag; // Only one level deep
        var resolvedStack = parentBag.Grid.GetCell(breadcrumb.CellIndex).Stack!;
        var progress = resolvedStack.GetInt("Progress");

        Assert.Equal(2, progress);
    }

    [Fact]
    public void Breadcrumbs_NoProgress_ReturnsNull()
    {
        var (state, _) = CreateStateWithFacility();

        // Enter without setting progress
        var enterResult = state.EnterBag();
        var nested = enterResult.State;

        var breadcrumb = nested.BreadcrumbStack.Peek();
        var parentBag = nested.RootBag;
        var resolvedStack = parentBag.Grid.GetCell(breadcrumb.CellIndex).Stack!;
        var progress = resolvedStack.GetInt("Progress");

        Assert.Null(progress);
    }

    // ============================================
    // Pattern 2: All bags to update (tick loop)
    // ============================================

    [Fact]
    public void TickLoop_FindsAllFacilities_UpdatesOwnerProgress()
    {
        var (state, _) = CreateStateWithFacility();
        var recipes = ImmutableArray.Create(AxeRecipe);

        var session = GameSession.New(state, recipes, FacilityRecipeMap, TickMode.Realtime);

        // Tick once — should start crafting (progress goes to 1)
        var ticked = session.Tick();

        // Verify progress is on the owning ItemStack
        var facilityStack = ticked.Current.RootBag.Grid.GetCell(0).Stack!;
        Assert.Equal(1, facilityStack.GetInt("Progress"));

        // Also verify the facility bag's state was updated
        var facilityBag = facilityStack.ContainedBag!;
        Assert.Equal("axe", facilityBag.FacilityState!.RecipeId);
    }

    [Fact]
    public void TickLoop_ProgressAdvancesToCompletion()
    {
        var (state, _) = CreateStateWithFacility();
        var recipes = ImmutableArray.Create(AxeRecipe);
        var session = GameSession.New(state, recipes, FacilityRecipeMap, TickMode.Realtime);

        // Tick 3 times (duration = 3): should complete craft
        session = session.Tick(); // progress 0 → 1
        session = session.Tick(); // progress 1 → 2
        session = session.Tick(); // progress 2 → 3 → complete, reset to 0

        var facilityStack = session.Current.RootBag.Grid.GetCell(0).Stack!;
        Assert.Equal(0, facilityStack.GetInt("Progress"));

        // Output should be produced
        var facilityBag = facilityStack.ContainedBag!;
        Assert.Null(facilityBag.FacilityState!.RecipeId); // Reset after completion
        var outputCell = facilityBag.Grid.GetCell(2);
        Assert.False(outputCell.IsEmpty);
        Assert.Equal("Stone Axe", outputCell.Stack!.ItemType.Name);
    }

    [Fact]
    public void TickLoop_MultipleTicksTrackProgressCorrectly()
    {
        var (state, _) = CreateStateWithFacility();
        var recipes = ImmutableArray.Create(AxeRecipe);
        var session = GameSession.New(state, recipes, FacilityRecipeMap, TickMode.Realtime);

        // Tick once
        session = session.Tick();
        var stack1 = session.Current.RootBag.Grid.GetCell(0).Stack!;
        Assert.Equal(1, stack1.GetInt("Progress"));

        // Tick again
        session = session.Tick();
        var stack2 = session.Current.RootBag.Grid.GetCell(0).Stack!;
        Assert.Equal(2, stack2.GetInt("Progress"));
    }

    // ============================================
    // Pattern 3: Progress at a given cell index
    // ============================================

    [Fact]
    public void CellIndex_ReadProgressDirectlyFromStack()
    {
        var (state, facilityCellIndex) = CreateStateWithFacility();

        // Set progress on the owning stack
        var ownerStack = state.RootBag.Grid.GetCell(facilityCellIndex).Stack!;
        var updated = ownerStack.WithProperty("Progress", new IntValue(5));
        var rootGrid = state.RootBag.Grid.SetCell(facilityCellIndex, new Cell(updated));
        state = state with { RootBag = state.RootBag with { Grid = rootGrid } };

        // Read progress directly from the cell — the rendering use case
        var cell = state.RootBag.Grid.GetCell(facilityCellIndex);
        var progress = cell.Stack?.GetInt("Progress");

        Assert.Equal(5, progress);
    }

    [Fact]
    public void CellIndex_NoProgressProperty_ReturnsNull()
    {
        var (state, facilityCellIndex) = CreateStateWithFacility();

        var cell = state.RootBag.Grid.GetCell(facilityCellIndex);
        var progress = cell.Stack?.GetInt("Progress");

        Assert.Null(progress);
    }

    [Fact]
    public void CellIndex_EmptyCell_HandledGracefully()
    {
        var (state, _) = CreateStateWithFacility();

        // Check an empty cell
        var cell = state.RootBag.Grid.GetCell(1);
        var progress = cell.Stack?.GetInt("Progress");

        Assert.Null(progress);
    }

    // ============================================
    // BagRegistry owner info
    // ============================================

    [Fact]
    public void BagRegistry_TracksOwnerInfo()
    {
        var (state, facilityCellIndex) = CreateStateWithFacility();

        var registry = state.Registry;
        var facility = registry.Facilities.First();
        var ownerInfo = registry.GetOwnerOf(facility.Id);

        Assert.NotNull(ownerInfo);
        Assert.Equal(state.RootBag.Id, ownerInfo.ParentBagId);
        Assert.Equal(facilityCellIndex, ownerInfo.CellIndex);
    }

    [Fact]
    public void BagRegistry_RootBag_HasNoOwner()
    {
        var (state, _) = CreateStateWithFacility();

        var registry = state.Registry;
        var ownerInfo = registry.GetOwnerOf(state.RootBag.Id);

        Assert.Null(ownerInfo);
    }

    [Fact]
    public void BagRegistry_OwnerStackProgressReadable()
    {
        var (state, facilityCellIndex) = CreateStateWithFacility();

        // Set progress
        var ownerStack = state.RootBag.Grid.GetCell(facilityCellIndex).Stack!;
        var updated = ownerStack.WithProperty("Progress", new IntValue(7));
        var rootGrid = state.RootBag.Grid.SetCell(facilityCellIndex, new Cell(updated));
        state = state with { RootBag = state.RootBag with { Grid = rootGrid } };

        // Read via registry
        var registry = state.Registry;
        var facility = registry.Facilities.First();
        var ownerInfo = registry.GetOwnerOf(facility.Id)!;
        var parentBag = registry.GetById(ownerInfo.ParentBagId)!;
        var resolvedStack = parentBag.Grid.GetCell(ownerInfo.CellIndex).Stack!;

        Assert.Equal(7, resolvedStack.GetInt("Progress"));
    }
}
