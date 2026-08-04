using System.Collections.Immutable;
using Pockets.Core;
using Pockets.Core.Data;
using Pockets.Core.Models;
using Pockets.Core.Rendering;

namespace Pockets.Core.Tests.Models;

/// <summary>
/// Slice 4 — look-in vs. enter; enter-only bags. The generic <c>C</c> peek opens a one-deep look-in
/// overlay over ANY peekable bag (a plain chest included), toggling closed on C; Q also closes.
/// An <see cref="Bag.EnterOnly"/> bag refuses the peek — no panel, cursor/world untouched — surfacing
/// a failed-peek affordance (the <see cref="FeedbackPulse.FailedPeek"/> shake + a fire-once
/// <see cref="DialogueTriggerKind.FirstFailedPeek"/> beat). E still enters it via breadcrumbs.
/// The refused peek is the only tell — there is deliberately no visible enter-only marking (RATIFIED).
/// </summary>
public class PeekTests
{
    private static readonly ItemType Rock = new("Rock", Category.Material, IsStackable: true, MaxStackSize: 20);
    private static readonly ItemType ChestType = new("Chest", Category.Bag, IsStackable: false);
    private static readonly ItemType PocketType = new("Quiet Pocket", Category.Bag, IsStackable: false);
    private static readonly ImmutableArray<ItemType> Types = ImmutableArray.Create(Rock, ChestType, PocketType);

    private const string FailedPeekMarkdown = """
        # Dialogue: failed-peek
        Trigger: FirstFailedPeek

        - (wary) Can't just peek at this one.
        """;

    /// <summary>
    /// Root grid: cell 0 = a peekable Chest holding 3 Rock; cell 1 = an enter-only Quiet Pocket
    /// holding 1 Rock; cell 2 = 10 loose Rock. Plus hand + toolbar. Session carries the failed-peek book.
    /// </summary>
    private static GameController MakeController()
    {
        var chestBag = new Bag(Grid.Create(4, 2), "Chest")
            .AcquireItems(new[] { new ItemStack(Rock, 3) }).UpdatedBag;
        var pocketBag = new Bag(Grid.Create(2, 2), "Quiet Pocket") { EnterOnly = true }
            .AcquireItems(new[] { new ItemStack(Rock, 1) }).UpdatedBag;

        var rootGrid = Grid.Create(8, 4)
            .SetCell(0, new Cell(new ItemStack(ChestType, 1, ContainedBagId: chestBag.Id)))
            .SetCell(1, new Cell(new ItemStack(PocketType, 1, ContainedBagId: pocketBag.Id)))
            .SetCell(2, new Cell(new ItemStack(Rock, 10)));
        var rootBag = new Bag(rootGrid);
        var handBag = GameState.CreateHandBag();
        var toolbarBag = new Bag(Grid.Create(4, 1), "Toolbar");

        var store = BagStore.Empty
            .Add(rootBag).Add(handBag).Add(toolbarBag).Add(chestBag).Add(pocketBag);
        var locations = LocationMap.Create(handBag.Id, rootBag.Id)
            .Set(LocationId.T, Location.AtOrigin(toolbarBag.Id));
        var state = new GameState(store, locations, Types) { Ui = UiLedger.DemoInitial };

        var book = DialogueLoader.LoadFromMarkdown(FailedPeekMarkdown);
        return new GameController(GameSession.New(state) with { Book = book });
    }

    // ==================== Bag.EnterOnly property ====================

    [Fact]
    public void Bag_EnterOnly_DefaultsFalse_AndTravelsThroughWith()
    {
        var bag = new Bag(Grid.Create(2, 2));
        Assert.False(bag.EnterOnly);

        var enterOnly = bag with { EnterOnly = true };
        Assert.True(enterOnly.EnterOnly);
        // Identity is preserved through `with`, so the property rides with the bag in the store.
        Assert.Equal(bag.Id, enterOnly.Id);
    }

    [Fact]
    public void DialogueTrigger_Parse_UnderstandsFirstFailedPeek()
    {
        var trigger = DialogueTrigger.Parse("FirstFailedPeek");
        Assert.Equal(DialogueTriggerKind.FirstFailedPeek, trigger.Kind);
    }

    // ==================== Peek opens/closes a look-in overlay ====================

    [Fact]
    public void Peek_OnPlainChest_OpensLookInOverlay_AndFocusesC()
    {
        var c = MakeController();

        c.HandleKey(GameKey.Peek);

        var state = c.Session.Current;
        Assert.True(state.Locations.Has(LocationId.C));
        Assert.Equal(LocationId.C, c.Focus);
        Assert.Equal("Chest", state.Store.GetById(state.Locations.Get(LocationId.C).BagId)!.EnvironmentType);
        // FirstPeek → LookInOverlay fires structurally when C opens.
        Assert.True(state.Ui.Has(ChromeElement.LookInOverlay));
        // Peeking does NOT enter — B stays at root.
        Assert.False(state.IsNested);
    }

    [Fact]
    public void Peek_OnPlainChest_Again_ClosesOverlay_AndRefocusesB()
    {
        var c = MakeController();

        c.HandleKey(GameKey.Peek); // open, focus C
        c.HandleKey(GameKey.Peek); // C toggles closed

        Assert.False(c.Session.Current.Locations.Has(LocationId.C));
        Assert.Equal(LocationId.B, c.Focus);
    }

    [Fact]
    public void Peek_OnEmptyCell_IsHandledNoOp()
    {
        var c = MakeController();
        // Move onto an empty cell (cell 3 is empty).
        c.HandleKey(GameKey.Right); // (0,1) pocket
        c.HandleKey(GameKey.Right); // (0,2) rock
        c.HandleKey(GameKey.Right); // (0,3) empty

        var result = c.HandleKey(GameKey.Peek);

        Assert.True(result.Handled);
        Assert.False(c.Session.Current.Locations.Has(LocationId.C));
        Assert.Equal(LocationId.B, c.Focus);
    }

    [Fact]
    public void Peek_OpenThenClose_LeavesWorldCensusUntouched()
    {
        var c = MakeController();
        var before = InvariantChecker.Census(c.Session.Current);

        c.HandleKey(GameKey.Peek); // open
        c.HandleKey(GameKey.Peek); // close

        var after = InvariantChecker.Census(c.Session.Current);
        Assert.Empty(InvariantChecker.CensusDelta(before, after)); // golden: no items moved/created
    }

    [Fact]
    public void Peek_CrossContainerMove_ConservesItems()
    {
        var c = MakeController();
        var before = InvariantChecker.Census(c.Session.Current);

        c.HandleKey(GameKey.Peek);             // peek the Chest (focus C)
        Assert.Equal(LocationId.C, c.Focus);
        c.HandleKey(GameKey.Primary);          // grab the Chest's Rock into hand (cross-container)

        Assert.True(c.Session.Current.HasItemsInHand);
        var after = InvariantChecker.Census(c.Session.Current);
        Assert.Empty(InvariantChecker.CensusDelta(before, after)); // item moved, not created/destroyed
    }

    // ==================== Enter-only refuses the peek ====================

    [Fact]
    public void Peek_OnEnterOnly_DoesNotOpenPanel_AndLeavesCursorUnchanged()
    {
        var c = MakeController();
        c.HandleKey(GameKey.Right); // (0,1) — the enter-only Quiet Pocket
        var cursorBefore = c.Session.Current.Cursor.Position;

        c.HandleKey(GameKey.Peek);

        var state = c.Session.Current;
        Assert.False(state.Locations.Has(LocationId.C));
        Assert.False(state.Locations.Has(LocationId.W));
        Assert.False(state.IsNested);
        Assert.Equal(cursorBefore, state.Cursor.Position);
        Assert.Equal(LocationId.B, c.Focus);
    }

    [Fact]
    public void Peek_OnEnterOnly_FiresFailedPeekBeat_Once()
    {
        var c = MakeController();
        c.HandleKey(GameKey.Right); // to the pocket

        c.HandleKey(GameKey.Peek);
        var dlg = c.Session.Current.Dialogue;
        Assert.True(dlg.IsActive);
        Assert.Equal("failed-peek", dlg.ActiveBeatId);

        // Dismiss the beat, then peek again: the beat must NOT re-fire (fire-once).
        c.HandleKey(GameKey.Primary); // dismiss dialogue
        Assert.False(c.Session.Current.Dialogue.IsActive);

        c.HandleKey(GameKey.Peek);
        Assert.False(c.Session.Current.Dialogue.IsActive); // no re-show
    }

    [Fact]
    public void Peek_OnEnterOnly_QueuesFailedPeekPulse_EveryTime()
    {
        var c = MakeController();
        c.HandleKey(GameKey.Right); // to the pocket

        c.HandleKey(GameKey.Peek);
        Assert.Equal(FeedbackPulse.FailedPeek, c.ConsumeFeedbackPulse());
        Assert.Equal(FeedbackPulse.None, c.ConsumeFeedbackPulse()); // consumed on read

        // After dismissing the once-only beat, the shake still fires on a repeat refusal — the pulse
        // is the tell that survives the spent dialogue.
        c.HandleKey(GameKey.Primary); // dismiss
        c.HandleKey(GameKey.Peek);
        Assert.Equal(FeedbackPulse.FailedPeek, c.ConsumeFeedbackPulse());
    }

    [Fact]
    public void Peek_OnEnterOnly_DoesNotPushUndo()
    {
        var c = MakeController();
        c.HandleKey(GameKey.Right); // to the pocket
        var depthBefore = c.Session.UndoDepth;

        c.HandleKey(GameKey.Peek);

        // A refused peek is a no-op on the world; dialogue firing is monotonic and never undoable.
        Assert.Equal(depthBefore, c.Session.UndoDepth);
    }

    // ==================== Enter (E/Primary) still works on enter-only ====================

    [Fact]
    public void Enter_OnEnterOnly_PushesBreadcrumb()
    {
        var c = MakeController();
        c.HandleKey(GameKey.Right); // (0,1) — the enter-only pocket

        c.HandleKey(GameKey.Primary); // E enters

        Assert.True(c.Session.Current.IsNested);
        Assert.Equal("Quiet Pocket", c.Session.Current.ActiveBag.EnvironmentType);
    }

    // ==================== GameSession.FireFailedPeek (unit) ====================

    [Fact]
    public void FireFailedPeek_IsIdempotentAcrossCalls_AndDoesNotChangeCensus()
    {
        var c = MakeController();
        var session = c.Session;
        var censusBefore = InvariantChecker.Census(session.Current);

        var once = session.FireFailedPeek();
        Assert.True(once.Current.Dialogue.IsActive);

        var twice = once.FireFailedPeek();
        // Already queued/fired → same instance, no second copy in the queue.
        Assert.Same(once, twice);
        Assert.Equal(censusBefore, InvariantChecker.Census(twice.Current));
    }
}
