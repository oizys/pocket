namespace Pockets.Core.Models;

/// <summary>
/// Top-level game state composing the root bag, cursor, and known item types.
/// All operations return new instances (immutable).
/// </summary>
public record GameState(
    Bag RootBag,
    Cursor Cursor,
    ImmutableArray<ItemType> ItemTypes)
{
    /// <summary>
    /// Creates the initial Stage 1 game state: 8×4 bag, cursor at origin,
    /// with the given item stacks acquired into the grid.
    /// </summary>
    public static GameState CreateStage1(
        ImmutableArray<ItemType> itemTypes,
        IEnumerable<ItemStack> initialStacks)
    {
        var bag = new Bag(Grid.Create(8, 4));
        var (filledBag, _) = bag.AcquireItems(initialStacks);
        return new GameState(filledBag, new Cursor(new Position(0, 0)), itemTypes);
    }

    /// <summary>
    /// Returns a new GameState with the cursor moved one step in the given direction.
    /// </summary>
    public GameState MoveCursor(Direction direction) =>
        this with { Cursor = Cursor.Move(direction, RootBag.Grid.Rows, RootBag.Grid.Columns) };

    /// <summary>
    /// Returns the cell at the current cursor position.
    /// </summary>
    public Cell CurrentCell => RootBag.Grid.GetCell(Cursor.Position);
}
