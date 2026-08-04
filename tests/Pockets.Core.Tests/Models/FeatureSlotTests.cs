using System.Collections.Immutable;
using Pockets.Core.Models;

namespace Pockets.Core.Tests.Models;

/// <summary>
/// Slice-5 feature slots: the glyph-keyed Shrine slot that accepts only its matching-glyph core and,
/// once slotted, locks irreversibly. Covers the frame's filter language and the slotting tool path
/// (accept / wrong-item reject / non-removable).
/// </summary>
public class FeatureSlotTests
{
    private static readonly ItemType EyeCoreType = new("Eye Core", Category.Tool, IsStackable: false);
    private static readonly ItemType RockType = new("Plain Rock", Category.Material, IsStackable: true);

    private static ItemStack EyeCore() =>
        new ItemStack(EyeCoreType, 1).WithProperty(FeatureSlotFrame.GlyphProperty, new StringValue("eye"));

    // ==================== Frame filter language ====================

    [Fact]
    public void FeatureSlot_DefaultsUnlocked()
    {
        Assert.False(new FeatureSlotFrame("eye").IsLocked);
    }

    [Fact]
    public void FeatureSlot_AcceptsMatchingGlyphCore()
    {
        var slot = new FeatureSlotFrame("eye");
        Assert.True(slot.Accepts(EyeCore()));
    }

    [Fact]
    public void FeatureSlot_RejectsWrongGlyph()
    {
        var slot = new FeatureSlotFrame("eye");
        var wrongGlyph = new ItemStack(EyeCoreType, 1).WithProperty(FeatureSlotFrame.GlyphProperty, new StringValue("clock"));
        Assert.False(slot.Accepts(wrongGlyph));
    }

    [Fact]
    public void FeatureSlot_RejectsItemWithNoGlyph()
    {
        var slot = new FeatureSlotFrame("eye");
        Assert.False(slot.Accepts(new ItemStack(RockType, 1)));
    }

    [Fact]
    public void FeatureSlot_ItemTypeFilter_TakesPriorityOverGlyph()
    {
        var slot = new FeatureSlotFrame("eye", ItemTypeFilter: EyeCoreType);
        Assert.True(slot.Accepts(new ItemStack(EyeCoreType, 1)));        // exact type, even without a glyph prop
        Assert.False(slot.Accepts(new ItemStack(RockType, 1)));
    }

    [Fact]
    public void Cell_EmptyFeatureSlot_AcceptsNothingViaGenericPath()
    {
        // An empty feature slot must never be a generic acquisition target — slotting is explicit.
        var cell = new Cell(Frame: new FeatureSlotFrame("eye"));
        Assert.False(cell.Accepts(EyeCoreType));
        Assert.False(cell.Accepts(RockType));
    }

    [Fact]
    public void Cell_FilledFeatureSlot_TrustsItsContent()
    {
        // A filled slot's core passed the glyph filter at slot time; the invariant checker must see it valid.
        var cell = new Cell(Stack: EyeCore(), Frame: new FeatureSlotFrame("eye", IsLocked: true));
        Assert.True(cell.Accepts(EyeCoreType));
        Assert.True(cell.IsLockedFeatureSlot);
    }

    // ==================== Slotting tool path ====================

    /// <summary>Builds a one-cell "Shrine" bag with a single feature slot at (0,0), the given core in hand.</summary>
    private static GameState WithSlotAndHand(FeatureSlotFrame slot, ItemStack? handItem)
    {
        var shrineGrid = Grid.Create(1, 1).SetCell(0, new Cell(Frame: slot));
        var shrine = new Bag(shrineGrid, GameState.ShrineEnvironment);
        var hand = GameState.CreateHandBag(1);
        if (handItem is not null)
            (hand, _) = hand.AcquireItems(new[] { handItem });
        var store = BagStore.Empty.Add(shrine).Add(hand);
        var locations = LocationMap.Create(hand.Id, shrine.Id);
        return new GameState(store, locations, ImmutableArray.Create(EyeCoreType, RockType));
    }

    [Fact]
    public void Slotting_MatchingCore_PlacesAndLocks()
    {
        var state = WithSlotAndHand(new FeatureSlotFrame("eye"), EyeCore());
        var result = state.ToolDrop();

        Assert.True(result.Success);
        var cell = result.State.ActiveBag.Grid.GetCell(0);
        Assert.Equal("Eye Core", cell.Stack!.ItemType.Name);
        Assert.True(cell.IsLockedFeatureSlot);
        Assert.False(result.State.HasItemsInHand);
    }

    [Fact]
    public void Slotting_WrongItem_IsRejected_SlotUntouched()
    {
        var state = WithSlotAndHand(new FeatureSlotFrame("eye"), new ItemStack(RockType, 1));
        var result = state.ToolDrop();

        Assert.False(result.Success);
        var cell = result.State.ActiveBag.Grid.GetCell(0);
        Assert.True(cell.IsEmpty);                          // slot stays empty
        Assert.False(((FeatureSlotFrame)cell.Frame!).IsLocked); // still unlocked
        Assert.True(result.State.HasItemsInHand);           // the rock stays in hand
    }

    [Fact]
    public void Slotting_IntoAlreadyLockedSlot_IsRejected()
    {
        var slot = new FeatureSlotFrame("eye", IsLocked: true);
        var shrineGrid = Grid.Create(1, 1).SetCell(0, new Cell(Stack: EyeCore(), Frame: slot));
        var shrine = new Bag(shrineGrid, GameState.ShrineEnvironment);
        var hand = GameState.CreateHandBag(1);
        (hand, _) = hand.AcquireItems(new[] { EyeCore() });
        var store = BagStore.Empty.Add(shrine).Add(hand);
        var state = new GameState(store, LocationMap.Create(hand.Id, shrine.Id), ImmutableArray.Create(EyeCoreType));

        Assert.False(state.ToolDrop().Success);
    }

    [Fact]
    public void SlottedCore_CannotBeGrabbed()
    {
        var slot = new FeatureSlotFrame("eye", IsLocked: true);
        var shrineGrid = Grid.Create(1, 1).SetCell(0, new Cell(Stack: EyeCore(), Frame: slot));
        var shrine = new Bag(shrineGrid, GameState.ShrineEnvironment);
        var hand = GameState.CreateHandBag(1);
        var store = BagStore.Empty.Add(shrine).Add(hand);
        var state = new GameState(store, LocationMap.Create(hand.Id, shrine.Id), ImmutableArray.Create(EyeCoreType));

        var result = state.ToolGrab();
        Assert.False(result.Success);
        Assert.False(result.State.HasItemsInHand);          // nothing left the slot
        Assert.Equal("Eye Core", result.State.ActiveBag.Grid.GetCell(0).Stack!.ItemType.Name);
    }
}
