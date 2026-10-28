namespace Pockets.Core.Models;

/// <summary>
/// Pure static functions for plant growth and harvest mechanics.
/// Plants are identified by: PlanterFrame + unique item + Duration property.
/// Growth advances Progress toward Duration each tick. Harvest produces items and resets progress.
/// </summary>
public static class PlantLogic
{
    /// <summary>
    /// Returns true if the cell contains a plant: has PlanterFrame, non-empty, unique item with Duration property.
    /// </summary>
    public static bool IsPlant(Cell cell) =>
        cell.Frame is PlanterFrame
        && !cell.IsEmpty
        && !cell.Stack!.ItemType.IsStackable
        && cell.Stack.GetInt("Duration") is not null;

    /// <summary>
    /// Returns true if the plant's Progress has reached its Duration (fully grown).
    /// </summary>
    public static bool IsGrown(ItemStack stack) =>
        stack.GetInt("Progress") is { } progress
        && stack.GetInt("Duration") is { } duration
        && progress >= duration;

    /// <summary>
    /// Scans all bags in the store and advances Progress on plants where Progress &lt; Duration.
    /// Does not advance past Duration (plants cap at "grown" until harvested).
    /// Returns the updated GameState.
    /// </summary>
    public static GameState TickPlants(GameState state)
    {
        var updatedStore = state.Store;

        foreach (var bag in state.Store.All)
        {
            // One writer per bag with growing plants, committed once (the Slice 1.4 SetItem ban).
            var writer = new Sim.ImmutableBagWriter(bag.Grid);
            var changed = false;

            for (int i = 0; i < bag.Grid.Cells.Length; i++)
            {
                var cell = bag.Grid.GetCell(i);
                if (!IsPlant(cell))
                    continue;

                var stack = cell.Stack!;
                var progress = stack.GetInt("Progress") ?? 0;
                var duration = stack.GetInt("Duration")!.Value;

                if (progress >= duration)
                    continue;

                var newProgress = progress + 1;
                var updatedStack = stack.WithProperty("Progress", new IntValue(newProgress));
                writer.Set(i, cell with { Stack = updatedStack });
                changed = true;
            }

            if (changed)
            {
                var updatedBag = bag with { Grid = writer.Commit() };
                updatedStore = updatedStore.Set(bag.Id, updatedBag);
            }
        }

        return state with { Store = updatedStore };
    }
}
