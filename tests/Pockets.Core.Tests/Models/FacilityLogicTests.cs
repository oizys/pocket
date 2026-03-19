using Pockets.Core.Models;

namespace Pockets.Core.Tests.Models;

public class FacilityLogicTests
{
    private static readonly ItemType Rock = new("Rock", Category.Material, IsStackable: true, MaxStackSize: 20);
    private static readonly ItemType Wood = new("Wood", Category.Material, IsStackable: true, MaxStackSize: 20);
    private static readonly ItemType StoneAxe = new("Stone Axe", Category.Tool, IsStackable: false);

    private static readonly Recipe AxeRecipe = new(
        "axe", "Stone Axe",
        new[] { new RecipeInput(Rock, 5), new RecipeInput(Wood, 3) },
        () => new[] { new ItemStack(StoneAxe, 1) },
        Duration: 3);

    /// <summary>
    /// Creates a facility bag with 2 input slots and 1 output slot (1×3 grid).
    /// </summary>
    private static Bag CreateFacility(params (ItemType type, int count)[] inputs)
    {
        var grid = Grid.Create(3, 1);
        grid = grid.SetCell(0, new Cell(
            Stack: inputs.Length > 0 ? new ItemStack(inputs[0].type, inputs[0].count) : null,
            Frame: new InputSlotFrame("in1")));
        grid = grid.SetCell(1, new Cell(
            Stack: inputs.Length > 1 ? new ItemStack(inputs[1].type, inputs[1].count) : null,
            Frame: new InputSlotFrame("in2")));
        grid = grid.SetCell(2, new Cell(Frame: new OutputSlotFrame("out1")));
        return new Bag(grid, FacilityState: new FacilityState());
    }

    // ==================== Recipe Matching ====================

    [Fact]
    public void RecipeMatches_SufficientInputs_ReturnsTrue()
    {
        var facility = CreateFacility((Rock, 5), (Wood, 3));
        var stacks = FacilityLogic.GetInputStacks(facility);
        Assert.True(FacilityLogic.RecipeMatches(AxeRecipe, stacks));
    }

    [Fact]
    public void RecipeMatches_InsufficientInputs_ReturnsFalse()
    {
        var facility = CreateFacility((Rock, 3), (Wood, 3));
        var stacks = FacilityLogic.GetInputStacks(facility);
        Assert.False(FacilityLogic.RecipeMatches(AxeRecipe, stacks));
    }

    [Fact]
    public void RecipeMatches_EmptyInputs_ReturnsFalse()
    {
        var facility = CreateFacility();
        var stacks = FacilityLogic.GetInputStacks(facility);
        Assert.False(FacilityLogic.RecipeMatches(AxeRecipe, stacks));
    }

    [Fact]
    public void FindMatchingRecipe_Match_ReturnsRecipe()
    {
        var facility = CreateFacility((Rock, 5), (Wood, 3));
        var result = FacilityLogic.FindMatchingRecipe(facility, new[] { AxeRecipe });
        Assert.NotNull(result);
        Assert.Equal("axe", result.Id);
    }

    [Fact]
    public void FindMatchingRecipe_NoMatch_ReturnsNull()
    {
        var facility = CreateFacility((Rock, 2));
        var result = FacilityLogic.FindMatchingRecipe(facility, new[] { AxeRecipe });
        Assert.Null(result);
    }

    // ==================== Tick: Starting Craft ====================

    [Fact]
    public void Tick_MatchingInputs_StartsCrafting()
    {
        var facility = CreateFacility((Rock, 5), (Wood, 3));
        var (ticked, progress) = FacilityLogic.Tick(facility, 0, new[] { AxeRecipe });

        Assert.Equal("axe", ticked.FacilityState!.RecipeId);
        Assert.Equal(1, progress);
    }

    [Fact]
    public void Tick_NoMatchingInputs_NoChange()
    {
        var facility = CreateFacility((Rock, 2));
        var (ticked, progress) = FacilityLogic.Tick(facility, 0, new[] { AxeRecipe });

        Assert.Null(ticked.FacilityState!.RecipeId);
        Assert.Equal(0, progress);
    }

    [Fact]
    public void Tick_NotActive_NoChange()
    {
        var facility = CreateFacility((Rock, 5), (Wood, 3));
        facility = facility with { FacilityState = new FacilityState(IsActive: false) };
        var (ticked, progress) = FacilityLogic.Tick(facility, 0, new[] { AxeRecipe });

        Assert.Null(ticked.FacilityState!.RecipeId);
    }

    // ==================== Tick: Progress ====================

    [Fact]
    public void Tick_InProgress_IncrementsProgress()
    {
        var facility = CreateFacility((Rock, 5), (Wood, 3));
        facility = facility with { FacilityState = new FacilityState(RecipeId: "axe") };

        var (ticked, progress) = FacilityLogic.Tick(facility, 1, new[] { AxeRecipe });

        Assert.Equal("axe", ticked.FacilityState!.RecipeId);
        Assert.Equal(2, progress);
    }

    // ==================== Tick: Completion ====================

    [Fact]
    public void Tick_CompletesAtDuration_ConsumesInputs_ProducesOutput()
    {
        var facility = CreateFacility((Rock, 5), (Wood, 3));
        // Duration is 3, so at progress 2, one more tick completes it
        facility = facility with { FacilityState = new FacilityState(RecipeId: "axe") };

        var (ticked, progress) = FacilityLogic.Tick(facility, 2, new[] { AxeRecipe });

        // Recipe complete: state reset
        Assert.Null(ticked.FacilityState!.RecipeId);
        Assert.Equal(0, progress);

        // Inputs consumed
        var input0 = ticked.Grid.GetCell(0);
        var input1 = ticked.Grid.GetCell(1);
        Assert.True(input0.IsEmpty);
        Assert.True(input1.IsEmpty);

        // Output produced
        var output = ticked.Grid.GetCell(2);
        Assert.False(output.IsEmpty);
        Assert.Equal(StoneAxe, output.Stack!.ItemType);
        Assert.Equal(1, output.Stack.Count);
    }

    [Fact]
    public void Tick_CompletesWithExcessInputs_LeavesRemainder()
    {
        // Put 8 Rock (need 5) and 3 Wood (need 3)
        var facility = CreateFacility((Rock, 8), (Wood, 3));
        facility = facility with { FacilityState = new FacilityState(RecipeId: "axe") };

        var (ticked, progress) = FacilityLogic.Tick(facility, 2, new[] { AxeRecipe });

        // 8 - 5 = 3 Rock remain
        var input0 = ticked.Grid.GetCell(0);
        Assert.False(input0.IsEmpty);
        Assert.Equal(3, input0.Stack!.Count);

        // Wood fully consumed
        var input1 = ticked.Grid.GetCell(1);
        Assert.True(input1.IsEmpty);

        // Output produced
        var output = ticked.Grid.GetCell(2);
        Assert.Equal(StoneAxe, output.Stack!.ItemType);
    }

    [Fact]
    public void Tick_OutputSlotOccupied_StillCompletes()
    {
        // This tests behavior when output slot already has something
        // For now, completion only places in empty output slots
        var facility = CreateFacility((Rock, 5), (Wood, 3));
        // Pre-fill output slot
        var grid = facility.Grid.SetCell(2, new Cell(
            Stack: new ItemStack(StoneAxe, 1),
            Frame: new OutputSlotFrame("out1")));
        facility = facility with
        {
            Grid = grid,
            FacilityState = new FacilityState(RecipeId: "axe")
        };

        var (ticked, progress) = FacilityLogic.Tick(facility, 2, new[] { AxeRecipe });

        // Inputs consumed but output couldn't be placed (slot occupied)
        // The output is lost — player should grab output before next craft completes
        Assert.Null(ticked.FacilityState!.RecipeId);
    }

    // ==================== Tick: Reset on Invalid ====================

    [Fact]
    public void Tick_InputsRemovedMidCraft_ResetsProgress()
    {
        // Start crafting but then inputs are gone
        var facility = CreateFacility(); // empty inputs
        facility = facility with { FacilityState = new FacilityState(RecipeId: "axe") };

        var (ticked, progress) = FacilityLogic.Tick(facility, 1, new[] { AxeRecipe });

        Assert.Null(ticked.FacilityState!.RecipeId);
        Assert.Equal(0, progress);
    }

    // ==================== Helper: GetInputStacks ====================

    [Fact]
    public void GetInputStacks_ReturnsOnlyInputSlotItems()
    {
        var facility = CreateFacility((Rock, 5), (Wood, 3));
        // Put something in the output slot too
        var grid = facility.Grid.SetCell(2, new Cell(
            Stack: new ItemStack(StoneAxe, 1),
            Frame: new OutputSlotFrame("out1")));
        facility = facility with { Grid = grid };

        var stacks = FacilityLogic.GetInputStacks(facility);
        Assert.Equal(2, stacks.Count);
        Assert.All(stacks, s => Assert.True(s.ItemType == Rock || s.ItemType == Wood));
    }

    // ==================== Helper: OutputSlotsEmpty ====================

    [Fact]
    public void OutputSlotsEmpty_AllEmpty_ReturnsTrue()
    {
        var facility = CreateFacility((Rock, 5));
        Assert.True(FacilityLogic.OutputSlotsEmpty(facility));
    }

    [Fact]
    public void OutputSlotsEmpty_HasOutput_ReturnsFalse()
    {
        var facility = CreateFacility();
        var grid = facility.Grid.SetCell(2, new Cell(
            Stack: new ItemStack(StoneAxe, 1),
            Frame: new OutputSlotFrame("out1")));
        facility = facility with { Grid = grid };
        Assert.False(FacilityLogic.OutputSlotsEmpty(facility));
    }

    // ==================== Full Cycle ====================

    [Fact]
    public void FullCycle_StartToCompletion()
    {
        var facility = CreateFacility((Rock, 5), (Wood, 3));
        var recipes = new[] { AxeRecipe };
        int progress = 0;

        // Tick 1: starts crafting
        (facility, progress) = FacilityLogic.Tick(facility, progress, recipes);
        Assert.Equal("axe", facility.FacilityState!.RecipeId);
        Assert.Equal(1, progress);

        // Tick 2: progress
        (facility, progress) = FacilityLogic.Tick(facility, progress, recipes);
        Assert.Equal(2, progress);

        // Tick 3: completes (duration = 3)
        (facility, progress) = FacilityLogic.Tick(facility, progress, recipes);
        Assert.Null(facility.FacilityState.RecipeId);
        Assert.Equal(0, progress);

        // Inputs consumed, output produced
        Assert.True(facility.Grid.GetCell(0).IsEmpty);
        Assert.True(facility.Grid.GetCell(1).IsEmpty);
        Assert.Equal(StoneAxe, facility.Grid.GetCell(2).Stack!.ItemType);
    }

    // ==================== OutputFactory produces unique items ====================

    [Fact]
    public void OutputFactory_CalledEachTime_ProducesUniqueItems()
    {
        var bagType = new ItemType("Bag", Category.Bag, IsStackable: false);
        int callCount = 0;
        var bagRecipe = new Recipe(
            "bag", "Test Bag",
            new[] { new RecipeInput(Rock, 1) },
            () =>
            {
                callCount++;
                var bag = new Bag(Grid.Create(2, 2));
                return new[] { new ItemStack(bagType, 1, ContainedBag: bag) };
            },
            Duration: 1);

        var facility = CreateFacility((Rock, 1));
        facility = facility with { FacilityState = new FacilityState(RecipeId: "bag") };

        var (ticked, progress) = FacilityLogic.Tick(facility, 0, new[] { bagRecipe });

        Assert.Equal(1, callCount);
        var output = ticked.Grid.GetCell(2).Stack!;
        Assert.NotNull(output.ContainedBag);
    }
}
