namespace Pockets.Core.Models;

/// <summary>
/// A single cell in a grid. Holds an optional ItemStack, an optional category filter,
/// and an optional inner Bag (for bag-type items that contain their own grid).
/// </summary>
public record Cell(ItemStack? Stack = null, Category? CategoryFilter = null, Bag? InnerBag = null)
{
    /// <summary>
    /// True when this cell contains no item stack.
    /// </summary>
    public bool IsEmpty => Stack is null;

    /// <summary>
    /// True when this cell contains a bag that can be opened.
    /// </summary>
    public bool HasBag => InnerBag is not null;

    /// <summary>
    /// Returns true if the given item type is allowed in this cell.
    /// No filter means any item is accepted; a filter requires a category match.
    /// </summary>
    public bool Accepts(ItemType itemType) =>
        CategoryFilter is null || CategoryFilter == itemType.Category;
}
