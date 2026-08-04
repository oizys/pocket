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
    /// True when this cell is a Shrine feature slot (has FeatureSlotFrame).
    /// </summary>
    public bool IsFeatureSlot => Frame is FeatureSlotFrame;

    /// <summary>
    /// True when this cell is a locked feature slot — its slotted core is irremovable (Slice 5).
    /// </summary>
    public bool IsLockedFeatureSlot => Frame is FeatureSlotFrame { IsLocked: true };

    /// <summary>
    /// Returns true if the given item type is allowed in this cell.
    /// Checks both CategoryFilter and InputSlotFrame filter if present.
    ///
    /// Feature slots (Slice 5) are never a target for the generic acquisition/drop path: an EMPTY
    /// feature slot accepts nothing here (it is filled only by the explicit, glyph-checked slotting
    /// action), while a FILLED feature slot's core is trusted (it passed the slot's glyph filter when
    /// slotted), so the invariant checker sees the held item as valid.
    /// </summary>
    public bool Accepts(ItemType itemType)
    {
        if (Frame is FeatureSlotFrame)
            return Stack is not null;
        if (CategoryFilter is not null && CategoryFilter != itemType.Category)
            return false;
        if (Frame is InputSlotFrame input && !input.Accepts(itemType))
            return false;
        return true;
    }
}
