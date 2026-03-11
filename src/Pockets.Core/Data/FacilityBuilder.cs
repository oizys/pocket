using Pockets.Core.Models;

namespace Pockets.Core.Data;

/// <summary>
/// Creates facility bags with input/output slot CellFrames.
/// Each facility is a small bag (1×3 grid) with 2 input slots and 1 output slot.
/// Input slots are filtered to accept only the specific item types required by the recipe.
/// </summary>
public static class FacilityBuilder
{
    /// <summary>
    /// Creates a Workbench facility bag with input slots filtered to the recipe's required item types.
    /// </summary>
    public static Bag CreateWorkbench(Recipe? recipe = null) =>
        CreateFacility(RecipeRegistry.WorkbenchFacilityType, "Workbench", "Brown", recipe);

    /// <summary>
    /// Creates a Tanner facility bag with input slots filtered to the recipe's required item types.
    /// </summary>
    public static Bag CreateTanner(Recipe? recipe = null) =>
        CreateFacility(RecipeRegistry.TannerFacilityType, "Tanner", "Brown", recipe);

    /// <summary>
    /// Creates a Seedling Pot facility bag with input slots filtered to the recipe's required item types.
    /// </summary>
    public static Bag CreateSeedlingPot(Recipe? recipe = null) =>
        CreateFacility(RecipeRegistry.SeedlingPotFacilityType, "Seedling Pot", "Green", recipe);

    /// <summary>
    /// Creates a facility bag with the given type, 2 input slots and 1 output slot.
    /// When a recipe is provided, each input slot is filtered to accept only that recipe input's item type.
    /// </summary>
    private static Bag CreateFacility(string facilityType, string environment, string colorScheme, Recipe? recipe)
    {
        var grid = Grid.Create(3, 1);
        var builder = grid.Cells.ToBuilder();

        if (recipe is not null && recipe.Inputs.Count >= 2)
        {
            builder[0] = new Cell(Frame: new InputSlotFrame("in1", ItemTypeFilter: recipe.Inputs[0].ItemType));
            builder[1] = new Cell(Frame: new InputSlotFrame("in2", ItemTypeFilter: recipe.Inputs[1].ItemType));
        }
        else
        {
            builder[0] = new Cell(Frame: new InputSlotFrame("in1"));
            builder[1] = new Cell(Frame: new InputSlotFrame("in2"));
        }
        builder[2] = new Cell(Frame: new OutputSlotFrame("out1"));
        grid = grid with { Cells = builder.MoveToImmutable() };

        return new Bag(grid, environment, colorScheme,
            FacilityState: new FacilityState(ActiveRecipeId: recipe?.Id));
    }
}
