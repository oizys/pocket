using System.Collections.Immutable;
using Pockets.Core.Models;

namespace Pockets.Core.Tests.Models;

public class PlantLogicTests
{
    private static readonly ItemType BeanPlant = new("Green Bean Plant", Category.Consumable, IsStackable: false);
    private static readonly ItemType GreenBean = new("Green Bean", Category.Consumable, IsStackable: true, MaxStackSize: 20);
    private static readonly ItemType Rock = new("Rock", Category.Material, IsStackable: true, MaxStackSize: 20);

    /// <summary>
    /// Creates a plant ItemStack with standard properties.
    /// </summary>
    private static ItemStack MakePlant(int progress = 0, int duration = 6, int yield = 3) =>
        new ItemStack(BeanPlant, 1)
            .WithProperty("Progress", new IntValue(progress))
            .WithProperty("Duration", new IntValue(duration))
            .WithProperty("Yield", new IntValue(yield))
            .WithProperty("Produce", new StringValue("Green Bean"));

    /// <summary>
    /// Creates a GameState with a single planter cell at index 0 in a 4×1 grid.
    /// </summary>
    private static GameState MakePlantState(ItemStack? plant = null)
    {
        var grid = Grid.Create(4, 1);
        grid = grid.SetCell(0, new Cell(Stack: plant, Frame: new PlanterFrame()));
        var bag = new Bag(grid);
        var itemTypes = ImmutableArray.Create(BeanPlant, GreenBean, Rock);
        var handBag = GameState.CreateHandBag();
        var store = BagStore.Empty.Add(bag).Add(handBag);
        return new GameState(store, LocationMap.Create(handBag.Id, bag.Id), itemTypes);
    }

    // ==================== IsPlant ====================

    [Fact]
    public void IsPlant_PlanterFrameWithPlant_ReturnsTrue()
    {
        var cell = new Cell(Stack: MakePlant(), Frame: new PlanterFrame());
        Assert.True(PlantLogic.IsPlant(cell));
    }

    [Fact]
    public void IsPlant_EmptyPlanterFrame_ReturnsFalse()
    {
        var cell = new Cell(Frame: new PlanterFrame());
        Assert.False(PlantLogic.IsPlant(cell));
    }

    [Fact]
    public void IsPlant_NoPlanterFrame_ReturnsFalse()
    {
        var cell = new Cell(Stack: MakePlant());
        Assert.False(PlantLogic.IsPlant(cell));
    }

    [Fact]
    public void IsPlant_StackableItemInPlanter_ReturnsFalse()
    {
        var cell = new Cell(Stack: new ItemStack(Rock, 5), Frame: new PlanterFrame());
        Assert.False(PlantLogic.IsPlant(cell));
    }

    [Fact]
    public void IsPlant_NoDurationProperty_ReturnsFalse()
    {
        var noDuration = new ItemStack(BeanPlant, 1)
            .WithProperty("Progress", new IntValue(0));
        var cell = new Cell(Stack: noDuration, Frame: new PlanterFrame());
        Assert.False(PlantLogic.IsPlant(cell));
    }

    // ==================== IsGrown ====================

    [Fact]
    public void IsGrown_ProgressEqualsDuration_ReturnsTrue()
    {
        var plant = MakePlant(progress: 6, duration: 6);
        Assert.True(PlantLogic.IsGrown(plant));
    }

    [Fact]
    public void IsGrown_ProgressExceedsDuration_ReturnsTrue()
    {
        var plant = MakePlant(progress: 8, duration: 6);
        Assert.True(PlantLogic.IsGrown(plant));
    }

    [Fact]
    public void IsGrown_ProgressLessThanDuration_ReturnsFalse()
    {
        var plant = MakePlant(progress: 3, duration: 6);
        Assert.False(PlantLogic.IsGrown(plant));
    }

    [Fact]
    public void IsGrown_ZeroProgress_ReturnsFalse()
    {
        var plant = MakePlant(progress: 0, duration: 6);
        Assert.False(PlantLogic.IsGrown(plant));
    }

    // ==================== TickPlants ====================

    [Fact]
    public void TickPlants_AdvancesProgress()
    {
        var state = MakePlantState(MakePlant(progress: 0));
        var ticked = PlantLogic.TickPlants(state);
        var plant = ticked.RootBag.Grid.GetCell(0).Stack!;
        Assert.Equal(1, plant.GetInt("Progress"));
    }

    [Fact]
    public void TickPlants_MultipleTicks_AdvancesCorrectly()
    {
        var state = MakePlantState(MakePlant(progress: 0));
        for (int i = 0; i < 4; i++)
            state = PlantLogic.TickPlants(state);
        var plant = state.RootBag.Grid.GetCell(0).Stack!;
        Assert.Equal(4, plant.GetInt("Progress"));
    }

    [Fact]
    public void TickPlants_CapsAtDuration()
    {
        var state = MakePlantState(MakePlant(progress: 5, duration: 6));
        state = PlantLogic.TickPlants(state);
        var plant = state.RootBag.Grid.GetCell(0).Stack!;
        Assert.Equal(6, plant.GetInt("Progress"));

        // One more tick should NOT advance past duration
        state = PlantLogic.TickPlants(state);
        plant = state.RootBag.Grid.GetCell(0).Stack!;
        Assert.Equal(6, plant.GetInt("Progress"));
    }

    [Fact]
    public void TickPlants_EmptyPlanter_NoChange()
    {
        var state = MakePlantState(null);
        var ticked = PlantLogic.TickPlants(state);
        Assert.True(ticked.RootBag.Grid.GetCell(0).IsEmpty);
    }

    [Fact]
    public void TickPlants_NonPlantItem_NotAdvanced()
    {
        var grid = Grid.Create(4, 1);
        grid = grid.SetCell(0, new Cell(Stack: new ItemStack(Rock, 5)));
        var bag = new Bag(grid);
        var handBag = GameState.CreateHandBag();
        var store = BagStore.Empty.Add(bag).Add(handBag);
        var state = new GameState(store, LocationMap.Create(handBag.Id, bag.Id),
            ImmutableArray.Create(Rock));

        var ticked = PlantLogic.TickPlants(state);
        Assert.Equal(5, ticked.RootBag.Grid.GetCell(0).Stack!.Count);
    }

    // ==================== Harvest (ToolGrab on grown plant) ====================

    [Fact]
    public void ToolGrab_GrownPlant_ProducesBeansInHand()
    {
        var state = MakePlantState(MakePlant(progress: 6, duration: 6));
        var result = state.ToolGrab();

        Assert.True(result.Success);
        // Hand should contain 3 Green Beans
        var handItems = result.State.HandItems;
        Assert.Single(handItems);
        Assert.Equal("Green Bean", handItems[0].ItemType.Name);
        Assert.Equal(3, handItems[0].Count);
    }

    [Fact]
    public void ToolGrab_GrownPlant_PlantStaysInCell()
    {
        var state = MakePlantState(MakePlant(progress: 6, duration: 6));
        var result = state.ToolGrab();

        Assert.True(result.Success);
        var cell = result.State.RootBag.Grid.GetCell(0);
        Assert.False(cell.IsEmpty);
        Assert.Equal("Green Bean Plant", cell.Stack!.ItemType.Name);
    }

    [Fact]
    public void ToolGrab_GrownPlant_ResetsProgress()
    {
        var state = MakePlantState(MakePlant(progress: 6, duration: 6));
        var result = state.ToolGrab();

        Assert.True(result.Success);
        var plant = result.State.RootBag.Grid.GetCell(0).Stack!;
        Assert.Equal(0, plant.GetInt("Progress"));
    }

    [Fact]
    public void ToolGrab_GrownPlant_HandFull_Fails()
    {
        var state = MakePlantState(MakePlant(progress: 6, duration: 6));
        // Fill hand first
        var (filledHand, _) = state.HandBag.AcquireItems(new[] { new ItemStack(Rock, 1) });
        state = state with { Store = state.Store.Set(state.HandBagId, filledHand) };

        var result = state.ToolGrab();
        Assert.False(result.Success);
        Assert.Contains("Hand is full", result.Error);
    }

    [Fact]
    public void ToolGrab_UngrownPlant_NormalGrab()
    {
        var state = MakePlantState(MakePlant(progress: 3, duration: 6));
        var result = state.ToolGrab();

        Assert.True(result.Success);
        // Normal grab: plant is removed from cell, placed in hand
        Assert.True(result.State.RootBag.Grid.GetCell(0).IsEmpty);
        Assert.Equal("Green Bean Plant", result.State.HandItems[0].ItemType.Name);
    }

    // ==================== Full integration: 6-tick cycle ====================

    [Fact]
    public void FullCycle_SixTicks_GrowAndHarvest()
    {
        var state = MakePlantState(MakePlant(progress: 0, duration: 6));

        // Tick 6 times
        for (int i = 0; i < 6; i++)
        {
            state = PlantLogic.TickPlants(state);
            if (i < 5)
                Assert.False(PlantLogic.IsGrown(state.RootBag.Grid.GetCell(0).Stack!));
        }

        // Plant should now be grown
        Assert.True(PlantLogic.IsGrown(state.RootBag.Grid.GetCell(0).Stack!));

        // Harvest via grab
        var result = state.ToolGrab();
        Assert.True(result.Success);
        Assert.Equal("Green Bean", result.State.HandItems[0].ItemType.Name);
        Assert.Equal(3, result.State.HandItems[0].Count);

        // Plant reset
        var plant = result.State.RootBag.Grid.GetCell(0).Stack!;
        Assert.Equal(0, plant.GetInt("Progress"));
        Assert.Equal(6, plant.GetInt("Duration"));

        // Can grow again
        var state2 = result.State;
        for (int i = 0; i < 6; i++)
            state2 = PlantLogic.TickPlants(state2);
        Assert.True(PlantLogic.IsGrown(state2.RootBag.Grid.GetCell(0).Stack!));
    }
}
