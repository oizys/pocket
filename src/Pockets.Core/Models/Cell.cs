namespace Pockets.Core.Models;

/// <summary>
/// A single cell in a grid. Holds an optional ItemStack, optional category filter,
/// and optional CellFrame (behavior/chrome). Bag contents are carried by ItemStack.ContainedBag.
/// </summary>
public record Cell(ItemStack? Stack = null, Category? CategoryFilter = null, CellFrame? Frame = null)
{
    /// <summary>
    /// True when this cell contains no item stack.
    /// </summary>
    public bool IsEmpty => Stack is null;

    /// <summary>
    /// True when this cell contains a bag-type item with a bag reference.
    /// </summary>
    public bool HasBag => Stack?.ContainedBagId is not null;

    /// <summary>
    /// True when this cell has a CellFrame attached.
    /// </summary>
    public bool HasFrame => Frame is not null;

    /// <summary>
    /// True when this cell is an input slot (has InputSlotFrame).
    /// </summary>
    public bool IsInputSlot => Frame is InputSlotFrame;

    /// <summary>
    /// True when this cell is an output slot (has OutputSlotFrame).
    /// </summary>
    public bool IsOutputSlot => Frame is OutputSlotFrame;

    /// <summary>
    /// Returns true if the given item type is allowed in this cell.
    /// Checks both CategoryFilter and InputSlotFrame filter if present.
    /// </summary>
    public bool Accepts(ItemType itemType)
    {
        if (CategoryFilter is not null && CategoryFilter != itemType.Category)
            return false;
        if (Frame is InputSlotFrame input && !input.Accepts(itemType))
            return false;
        return true;
    }
}
