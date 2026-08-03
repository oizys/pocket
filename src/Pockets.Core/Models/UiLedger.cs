using System.Collections.Immutable;

namespace Pockets.Core.Models;

/// <summary>
/// A chrome element that either exists or doesn't right now. The eleven progressive-UI rows
/// from the target-demo journey (design/target-demo-journey.md — the "what exists when" table),
/// in materialization order. Extend by adding a row here (and, if a gameplay event reveals it,
/// a <see cref="UiTrigger"/> + a <see cref="UiLedger.TriggerMap"/> entry) — never a hardcoded
/// per-frontend bool.
/// </summary>
public enum ChromeElement
{
    DialogueBox,
    Grid,
    DescriptionPane,
    Toolbar,
    LookInOverlay,
    Breadcrumbs,
    ShrineView,
    ClockReadout,
    ActionQueue,
    Minimap,
    FullnessPips
}

/// <summary>
/// A gameplay event that can reveal chrome. Some triggers reference mechanics not yet built
/// (Shrine, Quiet Compass, the eye core); those carry event plumbing + tests here but have no
/// gameplay wiring until their slice. The wired-today triggers are <see cref="FirstPickup"/>,
/// <see cref="FirstPeek"/>, and <see cref="FirstEnter"/> (see <see cref="GameSession"/>).
/// </summary>
public enum UiTrigger
{
    FirstCursorRest,
    FirstPickup,
    FirstPeek,
    FirstEnter,
    EnterShrine,
    NoticeClock,
    FirstTimedAction,
    CompassCrafted,
    CoreSlotted
}

/// <summary>
/// Chrome-as-state: the set of <see cref="ChromeElement"/>s currently materialized. Chrome
/// presence is game state, not a frontend decision — mutated only by gameplay triggers — so
/// progressive-UI disclosure is asserted once, identically, on every frontend. Lives on
/// <see cref="GameState"/>; both frontends render each element only when its flag is on.
/// </summary>
public record UiLedger(ImmutableHashSet<ChromeElement> Present)
{
    /// <summary>
    /// Which chrome each trigger reveals. The registry that keeps triggers extensible: adding a
    /// disclosure means adding a row here, not editing render code in two frontends.
    /// </summary>
    public static readonly ImmutableDictionary<UiTrigger, ImmutableArray<ChromeElement>> TriggerMap =
        new Dictionary<UiTrigger, ImmutableArray<ChromeElement>>
        {
            [UiTrigger.FirstCursorRest] = ImmutableArray.Create(ChromeElement.DescriptionPane),
            [UiTrigger.FirstPickup] = ImmutableArray.Create(ChromeElement.Toolbar),
            [UiTrigger.FirstPeek] = ImmutableArray.Create(ChromeElement.LookInOverlay),
            [UiTrigger.FirstEnter] = ImmutableArray.Create(ChromeElement.Breadcrumbs),
            [UiTrigger.EnterShrine] = ImmutableArray.Create(ChromeElement.ShrineView),
            [UiTrigger.NoticeClock] = ImmutableArray.Create(ChromeElement.ClockReadout),
            [UiTrigger.FirstTimedAction] = ImmutableArray.Create(ChromeElement.ActionQueue),
            [UiTrigger.CompassCrafted] = ImmutableArray.Create(ChromeElement.Minimap),
            [UiTrigger.CoreSlotted] = ImmutableArray.Create(ChromeElement.FullnessPips),
        }.ToImmutableDictionary();

    /// <summary>Nothing materialized.</summary>
    public static readonly UiLedger Empty = new(ImmutableHashSet<ChromeElement>.Empty);

    /// <summary>
    /// Everything materialized — the default for non-demo profiles, so existing stages/tests see
    /// zero visible behavior change (the frontends already draw all chrome unconditionally).
    /// </summary>
    public static readonly UiLedger AllPresent =
        new(Enum.GetValues<ChromeElement>().ToImmutableHashSet());

    /// <summary>
    /// The demo profile's opening chrome: grid + cursor only (journey 0:45 row). Everything else
    /// grows in through triggers. (Frame-0 dialogue-box-only lands with Slice 2's dialogue box.)
    /// </summary>
    public static readonly UiLedger DemoInitial = new(ImmutableHashSet.Create(ChromeElement.Grid));

    /// <summary>True when the given chrome element is currently materialized.</summary>
    public bool Has(ChromeElement element) => Present.Contains(element);

    /// <summary>Materializes a chrome element. Idempotent — returns the same instance if already on.</summary>
    public UiLedger With(ChromeElement element) =>
        Present.Contains(element) ? this : this with { Present = Present.Add(element) };

    /// <summary>
    /// Fires a trigger: materializes every chrome element it reveals (idempotent). An unmapped
    /// trigger is a no-op. Returns the same instance when nothing changes, so idempotent fires on
    /// an already-complete ledger don't spuriously perturb <see cref="GameState"/> equality.
    /// </summary>
    public UiLedger Fire(UiTrigger trigger)
    {
        if (!TriggerMap.TryGetValue(trigger, out var elements))
            return this;

        var next = Present;
        foreach (var element in elements)
            next = next.Add(element);

        return ReferenceEquals(next, Present) ? this : this with { Present = next };
    }
}
