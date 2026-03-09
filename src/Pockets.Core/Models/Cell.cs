namespace Pockets.Core.Models;

/// <summary>
/// A single cell in a grid. Holds an optional ItemStack and an optional category filter.
/// Bag contents are carried by ItemStack.ContainedBag, not by the cell.
/// </summary>
public record Cell(ItemStack? Stack = null, Category? CategoryFilter = null)
{
    /// <summary>
    /// True when this cell contains no item stack.
    /// </summary>
    public bool IsEmpty => Stack is null;

    /// <summary>
    /// True when this cell contains a bag-type item with contents.
    /// </summary>
    public bool HasBag => Stack?.ContainedBag is not null;

    /// <summary>
    /// Returns true if the given item type is allowed in this cell.
    /// No filter means any item is accepted; a filter requires a category match.
    /// </summary>
    public bool Accepts(ItemType itemType) =>
        CategoryFilter is null || CategoryFilter == itemType.Category;
}
