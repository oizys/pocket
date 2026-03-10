using Pockets.Core.Models;

namespace Pockets.Core.Data;

/// <summary>
/// Creates facility bags with input/output slot CellFrames.
/// Each facility is a small bag (1×3 grid) with 2 input slots and 1 output slot.
/// </summary>
public static class FacilityBuilder
{
    /// <summary>
    /// Creates a Workbench facility bag (2 input, 1 output).
    /// </summary>
    public static Bag CreateWorkbench() =>
        CreateFacility(RecipeRegistry.WorkbenchFacilityType, "Workbench", "Brown");

    /// <summary>
    /// Creates a Tanner facility bag (2 input, 1 output).
    /// </summary>
    public static Bag CreateTanner() =>
        CreateFacility(RecipeRegistry.TannerFacilityType, "Tanner", "Brown");

    /// <summary>
    /// Creates a Seedling Pot facility bag (2 input, 1 output).
    /// </summary>
    public static Bag CreateSeedlingPot() =>
        CreateFacility(RecipeRegistry.SeedlingPotFacilityType, "Seedling Pot", "Green");

    /// <summary>
    /// Creates a facility bag with the given type, 2 input slots and 1 output slot.
    /// </summary>
    private static Bag CreateFacility(string facilityType, string environment, string colorScheme)
    {
        var grid = Grid.Create(3, 1);
        var builder = grid.Cells.ToBuilder();
        builder[0] = new Cell(Frame: new InputSlotFrame("in1"));
        builder[1] = new Cell(Frame: new InputSlotFrame("in2"));
        builder[2] = new Cell(Frame: new OutputSlotFrame("out1"));
        grid = grid with { Cells = builder.MoveToImmutable() };

        return new Bag(grid, environment, colorScheme,
            FacilityState: new FacilityState());
    }
}
