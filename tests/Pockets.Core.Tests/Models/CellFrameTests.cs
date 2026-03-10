using Pockets.Core.Models;

namespace Pockets.Core.Tests.Models;

public class CellFrameTests
{
    private static readonly ItemType Rock = new("Rock", Category.Material, IsStackable: true);
    private static readonly ItemType Sword = new("Sword", Category.Weapon, IsStackable: false);
    private static readonly ItemType Herbs = new("Herbs", Category.Medicine, IsStackable: true);

    // ==================== CellFrame basics ====================

    [Fact]
    public void InputSlotFrame_IsLocked_ByDefault()
    {
        var frame = new InputSlotFrame("in1");
        Assert.True(frame.IsLocked);
    }

    [Fact]
    public void OutputSlotFrame_IsLocked_ByDefault()
    {
        var frame = new OutputSlotFrame("out1");
        Assert.True(frame.IsLocked);
    }

    [Fact]
    public void InputSlotFrame_NoFilter_AcceptsAny()
    {
        var frame = new InputSlotFrame("in1");
        Assert.True(frame.Accepts(Rock));
        Assert.True(frame.Accepts(Sword));
        Assert.True(frame.Accepts(Herbs));
    }

    [Fact]
    public void InputSlotFrame_WithFilter_AcceptsOnlyMatching()
    {
        var frame = new InputSlotFrame("in1", Filter: Category.Material);
        Assert.True(frame.Accepts(Rock));
        Assert.False(frame.Accepts(Sword));
        Assert.False(frame.Accepts(Herbs));
    }

    [Fact]
    public void InputSlotFrame_WithItemTypeFilter_AcceptsOnlyExactType()
    {
        var frame = new InputSlotFrame("in1", ItemTypeFilter: Rock);
        Assert.True(frame.Accepts(Rock));
        Assert.False(frame.Accepts(Sword));
        Assert.False(frame.Accepts(Herbs));
    }

    [Fact]
    public void InputSlotFrame_ItemTypeFilter_OverridesCategoryFilter()
    {
        // Even though category filter would accept all Materials,
        // ItemTypeFilter restricts to just Rock
        var otherMaterial = new ItemType("Sand", Category.Material, IsStackable: true);
        var frame = new InputSlotFrame("in1", Filter: Category.Material, ItemTypeFilter: Rock);
        Assert.True(frame.Accepts(Rock));
        Assert.False(frame.Accepts(otherMaterial));
    }

    // ==================== Cell + Frame integration ====================

    [Fact]
    public void Cell_NoFrame_HasFrameIsFalse()
    {
        var cell = new Cell();
        Assert.False(cell.HasFrame);
        Assert.False(cell.IsInputSlot);
        Assert.False(cell.IsOutputSlot);
    }

    [Fact]
    public void Cell_WithInputSlot_HasFrameIsTrue()
    {
        var cell = new Cell(Frame: new InputSlotFrame("in1"));
        Assert.True(cell.HasFrame);
        Assert.True(cell.IsInputSlot);
        Assert.False(cell.IsOutputSlot);
    }

    [Fact]
    public void Cell_WithOutputSlot_HasFrameIsTrue()
    {
        var cell = new Cell(Frame: new OutputSlotFrame("out1"));
        Assert.True(cell.HasFrame);
        Assert.False(cell.IsInputSlot);
        Assert.True(cell.IsOutputSlot);
    }

    [Fact]
    public void Cell_InputSlot_WithFilter_RejectsWrongCategory()
    {
        var cell = new Cell(Frame: new InputSlotFrame("in1", Filter: Category.Material));
        Assert.True(cell.Accepts(Rock));
        Assert.False(cell.Accepts(Sword));
    }

    [Fact]
    public void Cell_InputSlot_NoFilter_AcceptsAny()
    {
        var cell = new Cell(Frame: new InputSlotFrame("in1"));
        Assert.True(cell.Accepts(Rock));
        Assert.True(cell.Accepts(Sword));
    }

    [Fact]
    public void Cell_CategoryFilter_AndInputSlotFilter_BothApply()
    {
        // CategoryFilter = Material, InputSlotFrame = Medicine → nothing passes both
        var cell = new Cell(
            CategoryFilter: Category.Material,
            Frame: new InputSlotFrame("in1", Filter: Category.Medicine));
        Assert.False(cell.Accepts(Rock));   // passes CategoryFilter, fails InputSlot
        Assert.False(cell.Accepts(Herbs));  // fails CategoryFilter, passes InputSlot
    }

    [Fact]
    public void Cell_WithFrame_CanHoldItems()
    {
        var stack = new ItemStack(Rock, 5);
        var cell = new Cell(Stack: stack, Frame: new InputSlotFrame("in1"));
        Assert.False(cell.IsEmpty);
        Assert.Equal(5, cell.Stack!.Count);
        Assert.True(cell.IsInputSlot);
    }

    [Fact]
    public void Cell_Frame_PreservedViaWith()
    {
        var cell = new Cell(Frame: new InputSlotFrame("in1", Filter: Category.Material));
        var withItem = cell with { Stack = new ItemStack(Rock, 3) };
        Assert.True(withItem.IsInputSlot);
        Assert.Equal("in1", ((InputSlotFrame)withItem.Frame!).SlotId);
    }

    // ==================== Pattern matching ====================

    [Fact]
    public void CellFrame_PatternMatching_Works()
    {
        CellFrame frame = new InputSlotFrame("in1", Filter: Category.Material);

        var description = frame switch
        {
            InputSlotFrame { Filter: not null } isf => $"Input({isf.SlotId}:{isf.Filter})",
            InputSlotFrame isf => $"Input({isf.SlotId})",
            OutputSlotFrame osf => $"Output({osf.SlotId})",
            _ => "Unknown"
        };

        Assert.Equal("Input(in1:Material)", description);
    }

    [Fact]
    public void CellFrame_StructuralEquality()
    {
        var a = new InputSlotFrame("in1", Filter: Category.Material);
        var b = new InputSlotFrame("in1", Filter: Category.Material);
        Assert.Equal(a, b);

        var c = new InputSlotFrame("in1", Filter: Category.Weapon);
        Assert.NotEqual(a, c);
    }

    // ==================== Empty frame cell rendering hint ====================

    [Fact]
    public void EmptyCell_WithFrame_IsEmpty_ButHasFrame()
    {
        var cell = new Cell(Frame: new OutputSlotFrame("out1"));
        Assert.True(cell.IsEmpty);
        Assert.True(cell.HasFrame);
    }
}
