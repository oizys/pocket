using System.Collections.Immutable;
using Pockets.Core.Models;

namespace Pockets.Core.Tests.Models;

/// <summary>
/// The UiLedger is chrome-as-state: which UI elements exist right now, mutated only by gameplay
/// triggers. These pin the ledger's algebra (idempotent fires, the trigger→chrome registry) and
/// the structural trigger detection in GameSession (pickup → toolbar, enter → breadcrumbs,
/// peek → look-in overlay), plus the invariant that non-demo profiles stay everything-on.
/// </summary>
public class UiLedgerTests
{
    private static readonly ItemType Rock = new("Plain Rock", Category.Material, IsStackable: true);

    // --- Ledger algebra ---

    [Fact]
    public void DemoInitial_HasGridOnly()
    {
        Assert.True(UiLedger.DemoInitial.Has(ChromeElement.Grid));
        Assert.False(UiLedger.DemoInitial.Has(ChromeElement.Toolbar));
        Assert.False(UiLedger.DemoInitial.Has(ChromeElement.Breadcrumbs));
        Assert.Single(UiLedger.DemoInitial.Present);
    }

    [Fact]
    public void AllPresent_HasEveryChromeElement()
    {
        foreach (var element in Enum.GetValues<ChromeElement>())
            Assert.True(UiLedger.AllPresent.Has(element));
    }

    [Fact]
    public void Empty_HasNothing()
    {
        foreach (var element in Enum.GetValues<ChromeElement>())
            Assert.False(UiLedger.Empty.Has(element));
    }

    [Fact]
    public void With_MaterializesElement_AndIsIdempotent()
    {
        var lit = UiLedger.Empty.With(ChromeElement.Toolbar);
        Assert.True(lit.Has(ChromeElement.Toolbar));

        // Idempotent: re-adding returns the same instance (no spurious state churn).
        Assert.Same(lit, lit.With(ChromeElement.Toolbar));
    }

    [Theory]
    [InlineData(UiTrigger.FirstCursorRest, ChromeElement.DescriptionPane)]
    [InlineData(UiTrigger.FirstPickup, ChromeElement.Toolbar)]
    [InlineData(UiTrigger.FirstPeek, ChromeElement.LookInOverlay)]
    [InlineData(UiTrigger.FirstEnter, ChromeElement.Breadcrumbs)]
    [InlineData(UiTrigger.EnterShrine, ChromeElement.ShrineView)]
    [InlineData(UiTrigger.NoticeClock, ChromeElement.ClockReadout)]
    [InlineData(UiTrigger.FirstTimedAction, ChromeElement.ActionQueue)]
    [InlineData(UiTrigger.CompassCrafted, ChromeElement.Minimap)]
    [InlineData(UiTrigger.CoreSlotted, ChromeElement.FullnessPips)]
    public void Fire_RevealsMappedChrome(UiTrigger trigger, ChromeElement expected)
    {
        var before = UiLedger.Empty;
        Assert.False(before.Has(expected));

        var after = before.Fire(trigger);
        Assert.True(after.Has(expected));
    }

    [Fact]
    public void Fire_OnAlreadyPresentChrome_ReturnsSameInstance()
    {
        // Every trigger fired against an everything-on ledger is a no-op that must not perturb
        // equality — this is what keeps non-demo profiles free of spurious state churn.
        foreach (var trigger in Enum.GetValues<UiTrigger>())
            Assert.Same(UiLedger.AllPresent, UiLedger.AllPresent.Fire(trigger));
    }

    [Fact]
    public void TriggerMap_CoversEveryTrigger()
    {
        foreach (var trigger in Enum.GetValues<UiTrigger>())
            Assert.True(UiLedger.TriggerMap.ContainsKey(trigger), $"{trigger} has no chrome mapping");
    }

    // --- GameState integration ---

    [Fact]
    public void GameState_DefaultsToAllPresent()
    {
        var state = MakeState(UiLedger.AllPresent, new ItemStack(Rock, 3));
        Assert.Same(UiLedger.AllPresent, state.Ui);
    }

    [Fact]
    public void FireUiTrigger_IsIdempotent_WhenAlreadyPresent()
    {
        var state = MakeState(UiLedger.AllPresent, new ItemStack(Rock, 3));
        Assert.Same(state, state.FireUiTrigger(UiTrigger.FirstPickup));
    }

    // --- Structural trigger detection through GameSession ---

    [Fact]
    public void Pickup_RevealsToolbar_InDemoLedger()
    {
        // Demo ledger starts toolbar-off; grabbing an item into the hand fires FirstPickup.
        var state = MakeState(UiLedger.DemoInitial, new ItemStack(Rock, 3));
        var session = GameSession.New(state);
        Assert.False(session.Current.Ui.Has(ChromeElement.Toolbar));

        var after = session.ExecuteGrab();

        Assert.True(after.Current.HasItemsInHand);
        Assert.True(after.Current.Ui.Has(ChromeElement.Toolbar));
    }

    [Fact]
    public void Enter_RevealsBreadcrumbs_InDemoLedger()
    {
        var pouch = new Bag(Grid.Create(2, 2), "Pouch");
        var pouchType = new ItemType("Belt Pouch", Category.Bag, IsStackable: false);
        var state = GameState.CreateStage1(
            ImmutableArray.Create(pouchType),
            new[] { new ItemStack(pouchType, 1, ContainedBagId: pouch.Id) },
            extraBags: new[] { pouch }) with { Ui = UiLedger.DemoInitial };

        var session = GameSession.New(state);
        Assert.False(session.Current.Ui.Has(ChromeElement.Breadcrumbs));

        var after = session.ExecuteEnterBag();

        Assert.True(after.Current.IsNested);
        Assert.True(after.Current.Ui.Has(ChromeElement.Breadcrumbs));
    }

    [Fact]
    public void OpeningLookInPanel_RevealsLookInOverlay_InDemoLedger()
    {
        var inner = new Bag(Grid.Create(2, 2), "Chest");
        var bagType = new ItemType("Small Chest", Category.Bag, IsStackable: false);
        var state = GameState.CreateStage1(
            ImmutableArray.Create(bagType),
            new[] { new ItemStack(bagType, 1, ContainedBagId: inner.Id) },
            extraBags: new[] { inner }) with { Ui = UiLedger.DemoInitial };

        var session = GameSession.New(state);
        Assert.False(session.Current.Ui.Has(ChromeElement.LookInOverlay));

        var opened = state.OpenAsContainer(inner.Id);
        var after = session.ApplyToolResult(opened, () => "open container");

        Assert.True(after.Current.Ui.Has(ChromeElement.LookInOverlay));
    }

    [Fact]
    public void NonDemoPickup_LeavesLedgerAllPresent_WithNoSpuriousChurn()
    {
        var state = MakeState(UiLedger.AllPresent, new ItemStack(Rock, 3));
        var session = GameSession.New(state);

        var after = session.ExecuteGrab();

        // The action happened (one undo frame) but the everything-on ledger is untouched —
        // same instance, so no view-model diff and no phantom chrome transition.
        Assert.Equal(1, after.UndoDepth);
        Assert.Same(UiLedger.AllPresent, after.Current.Ui);
    }

    [Fact]
    public void NoOpAction_DoesNotTouchLedgerOrUndo()
    {
        // Primary on an empty cell with an empty hand is a no-op: no undo frame, ledger intact.
        var state = MakeState(UiLedger.DemoInitial); // empty grid
        var session = GameSession.New(state);

        var after = session.ExecutePrimary();

        Assert.Equal(0, after.UndoDepth);
        Assert.Same(session.Current.Ui, after.Current.Ui);
    }

    // --- helpers ---

    /// <summary>Builds an 8×4 root state with the given ledger and optional stacks placed from cell 0.</summary>
    private static GameState MakeState(UiLedger ui, params ItemStack[] stacks) =>
        GameState.CreateStage1(ImmutableArray.Create(Rock), stacks) with { Ui = ui };
}
