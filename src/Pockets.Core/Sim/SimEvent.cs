using Pockets.Core.Models;

namespace Pockets.Core.Sim;

/// <summary>
/// Something that happened in one <c>Sim.Step</c>, for L2 to animate and for the parity event golden
/// (two-layer #23, Event contract). Every event carries the <see cref="Step"/> it happened in and its
/// <see cref="Seq"/> — its position within that step's event list, assigned in emission order. In
/// Phase 1 Seq is step-global, so <c>(Step, Seq)</c> is the whole canonical order (contract rule 2);
/// per-bag <c>ScheduleOrder</c> folds into emission order. Value equality comes from records.
/// <para>
/// A closed hierarchy (private constructor, sealed nested cases). The taxonomy is deliberately
/// minimal: the coarse Slice 1.2 events plus the reserved <see cref="BeltAdvanced"/> shape (P1-D3).
/// Slice 1.5 fills in the semantic events. Bags are referenced by in-memory GUIDs; serialization
/// renames them canonically (P1-D8).
/// </para>
/// </summary>
public abstract record SimEvent
{
    private SimEvent(int step, int seq)
    {
        Step = step;
        Seq = seq;
    }

    /// <summary>The step this event happened in.</summary>
    public int Step { get; init; }

    /// <summary>Position within the step's event list (0-based, emission order).</summary>
    public int Seq { get; init; }

    /// <summary>The canonical event order: by <see cref="Step"/>, then <see cref="Seq"/>. Kind never decides.</summary>
    public static IComparer<SimEvent> Order { get; } =
        Comparer<SimEvent>.Create((a, b) => (a.Step, a.Seq).CompareTo((b.Step, b.Seq)));

    // ==================== Envelope ====================

    /// <summary>
    /// An intent was applied and changed state. Since Slice 1.5 it is the <i>envelope</i>: it opens every
    /// applied intent's events and names the cause; the semantic events that follow say what happened.
    /// A modal, menu or clock intent changes no cell, so its envelope is its whole story.
    /// </summary>
    public sealed record IntentApplied(int Step, int Seq, Intent Intent) : SimEvent(Step, Seq);

    /// <summary>An intent failed; state is unchanged. L2 reconciles any prediction against it.</summary>
    public sealed record IntentRejected(int Step, int Seq, Intent Intent, string Reason) : SimEvent(Step, Seq);

    /// <summary>The step advanced simulated time (facilities and plants ticked).</summary>
    public sealed record TimeAdvanced(int Step, int Seq) : SimEvent(Step, Seq);

    /// <summary>
    /// Undo (Slice 1.8, a shell event — Step never emits it): the timeline was rewound to the snapshot taken
    /// at step <paramref name="ToStep"/>, and the event log truncated back to it. L2 snaps to the state rather
    /// than animating back (lossy L2 history, A3). Carried-forward fields (dialogue, clock, RNG, step count)
    /// did not rewind. It is the undo's only event.
    /// </summary>
    public sealed record Rewound(int Step, int Seq, int ToStep) : SimEvent(Step, Seq);

    // ==================== Cursor and navigation ====================

    /// <summary><paramref name="Loc"/>'s cursor moved within <paramref name="Bag"/>.</summary>
    public sealed record CursorMoved(int Step, int Seq, LocationId Loc, Guid Bag, Position From, Position To)
        : SimEvent(Step, Seq);

    /// <summary>
    /// A refused move or look (replaced the controller's <c>FeedbackPulse</c>; the frontends play their shake/flash from it): <paramref name="Loc"/>'s
    /// cursor at <paramref name="At"/> in <paramref name="Bag"/> bumped, in <paramref name="Dir"/> (null when
    /// the refusal has no direction, as with a failed peek), for <paramref name="Reason"/>. Nothing moved.
    /// </summary>
    public sealed record Bumped(int Step, int Seq, LocationId Loc, Guid Bag, Position At, Direction? Dir, BumpReason Reason)
        : SimEvent(Step, Seq);

    /// <summary><paramref name="Loc"/> entered <paramref name="Bag"/>, now <paramref name="Depth"/> breadcrumbs deep; its cursor reset.</summary>
    public sealed record BagEntered(int Step, int Seq, LocationId Loc, Guid Bag, int Depth) : SimEvent(Step, Seq);

    /// <summary><paramref name="Loc"/> left a bag and is back in <paramref name="Bag"/>, now <paramref name="Depth"/> deep; its cursor restored.</summary>
    public sealed record BagLeft(int Step, int Seq, LocationId Loc, Guid Bag, int Depth) : SimEvent(Step, Seq);

    /// <summary>The panel <paramref name="Loc"/> opened (or now shows) <paramref name="Bag"/>.</summary>
    public sealed record PanelOpened(int Step, int Seq, LocationId Loc, Guid Bag) : SimEvent(Step, Seq);

    /// <summary>The panel <paramref name="Loc"/>, which showed <paramref name="Bag"/>, closed.</summary>
    public sealed record PanelClosed(int Step, int Seq, LocationId Loc, Guid Bag) : SimEvent(Step, Seq);

    // ==================== Stacks ====================

    /// <summary>A whole stack (or all of what left the source) moved into an empty cell.</summary>
    public sealed record StackMoved(int Step, int Seq, ItemType Item, int Count, CellRef From, CellRef To, StackCause Cause)
        : SimEvent(Step, Seq);

    /// <summary>
    /// Part of a stack moved into an empty cell: <paramref name="Left"/> stayed at <paramref name="From"/>,
    /// <paramref name="Moved"/> went to <paramref name="To"/>.
    /// </summary>
    public sealed record StackSplit(int Step, int Seq, ItemType Item, CellRef From, CellRef To, int Left, int Moved, StackCause Cause)
        : SimEvent(Step, Seq);

    /// <summary><paramref name="Count"/> items moved from <paramref name="From"/> onto a stack of the same type at <paramref name="To"/>.</summary>
    public sealed record StackMerged(int Step, int Seq, ItemType Item, int Count, CellRef From, CellRef To, StackCause Cause)
        : SimEvent(Step, Seq);

    /// <summary>Items left <paramref name="Cell"/> with no destination in the world (e.g. a DSL op).</summary>
    public sealed record StackRemoved(int Step, int Seq, ItemType Item, int Count, CellRef Cell, StackCause Cause)
        : SimEvent(Step, Seq);

    /// <summary>
    /// <paramref name="Bag"/>'s contents were rearranged (sort). <paramref name="Permutation"/>[i] is the old
    /// cell index of the first stack folded into new cell i, or -1 when cell i is empty. Names every cell of the bag.
    /// </summary>
    public sealed record BagRearranged(int Step, int Seq, Guid Bag, ImmutableArray<int> Permutation) : SimEvent(Step, Seq)
    {
        /// <summary>Value equality, with the permutation compared element-wise.</summary>
        public bool Equals(BagRearranged? other) =>
            other is not null && Step == other.Step && Seq == other.Seq && Bag == other.Bag
            && ArrayEq(Permutation, other.Permutation);

        /// <inheritdoc />
        public override int GetHashCode() => HashCode.Combine(Step, Seq, Bag, ArrayHash(Permutation));
    }

    /// <summary><paramref name="Count"/> <paramref name="Item"/> appeared at <paramref name="Cell"/> from <paramref name="Source"/>.</summary>
    public sealed record Acquired(int Step, int Seq, ItemType Item, int Count, CellRef Cell, StackCause Source)
        : SimEvent(Step, Seq);

    // ==================== Facilities ====================

    /// <summary>
    /// <paramref name="Facility"/> started crafting <paramref name="RecipeId"/>. <paramref name="Owner"/> is the
    /// cell holding the facility's stack (its progress lives there), when it has one.
    /// </summary>
    public sealed record CraftStarted(int Step, int Seq, Guid Facility, string RecipeId, CellRef? Owner) : SimEvent(Step, Seq);

    /// <summary><paramref name="Facility"/>'s craft counted down; <paramref name="Remaining"/> steps to go.</summary>
    public sealed record CraftProgressed(int Step, int Seq, Guid Facility, string RecipeId, int Remaining, CellRef? Owner)
        : SimEvent(Step, Seq);

    /// <summary><paramref name="Facility"/>'s craft stopped because its inputs no longer match; progress reset.</summary>
    public sealed record CraftAborted(int Step, int Seq, Guid Facility, string RecipeId, CellRef? Owner) : SimEvent(Step, Seq);

    /// <summary>
    /// A facility finished a craft: its inputs at <paramref name="Consumed"/> were used up and
    /// <paramref name="Outputs"/> were placed in its output slots; its progress reset at <paramref name="Owner"/>.
    /// </summary>
    public sealed record CraftCompleted(
        int Step, int Seq, Guid FacilityBagId, string RecipeId,
        ImmutableArray<Placed> Outputs, ImmutableArray<CellRef> Consumed, CellRef? Owner) : SimEvent(Step, Seq)
    {
        /// <summary>Value equality, with outputs and consumed cells compared element-wise.</summary>
        public bool Equals(CraftCompleted? other) =>
            other is not null && Step == other.Step && Seq == other.Seq && FacilityBagId == other.FacilityBagId
            && RecipeId == other.RecipeId && Owner == other.Owner
            && ArrayEq(Outputs, other.Outputs) && ArrayEq(Consumed, other.Consumed);

        /// <inheritdoc />
        public override int GetHashCode() =>
            HashCode.Combine(Step, Seq, FacilityBagId, RecipeId, Owner, ArrayHash(Outputs), ArrayHash(Consumed));
    }

    /// <summary>
    /// <paramref name="Facility"/>'s active recipe was set to <paramref name="RecipeId"/> (recipe menu, or
    /// cycling); any craft in progress reset at <paramref name="Owner"/>. <paramref name="SlotsRebuilt"/>: the
    /// facility's slots were rebuilt for the recipe (names every cell of the facility).
    /// </summary>
    public sealed record RecipeSelected(int Step, int Seq, Guid Facility, string RecipeId, CellRef? Owner, bool SlotsRebuilt)
        : SimEvent(Step, Seq);

    // ==================== Plants ====================

    /// <summary>The plant at <paramref name="Cell"/> grew to <paramref name="Stage"/> of <paramref name="Of"/>.</summary>
    public sealed record PlantGrew(int Step, int Seq, CellRef Cell, int Stage, int Of) : SimEvent(Step, Seq);

    /// <summary>
    /// <paramref name="Cell"/> was harvested: its item (or a grown plant's produce) went to
    /// <paramref name="Yields"/>. A harvested plant stays at <paramref name="Cell"/> with its growth reset.
    /// </summary>
    public sealed record Harvested(int Step, int Seq, CellRef Cell, ImmutableArray<Placed> Yields) : SimEvent(Step, Seq)
    {
        /// <summary>Value equality, with the yields compared element-wise.</summary>
        public bool Equals(Harvested? other) =>
            other is not null && Step == other.Step && Seq == other.Seq && Cell == other.Cell
            && ArrayEq(Yields, other.Yields);

        /// <inheritdoc />
        public override int GetHashCode() => HashCode.Combine(Step, Seq, Cell, ArrayHash(Yields));
    }

    // ==================== Chrome, dialogue, recipes ====================

    /// <summary>A gameplay trigger grew the UI ledger.</summary>
    public sealed record UiTriggerFired(int Step, int Seq, UiTrigger Trigger) : SimEvent(Step, Seq);

    /// <summary>A dialogue beat was queued (it fires once).</summary>
    public sealed record DialogueQueued(int Step, int Seq, string BeatId) : SimEvent(Step, Seq);

    /// <summary>The showing beat advanced a line, or was dismissed after its last line.</summary>
    public sealed record DialogueAdvanced(int Step, int Seq, string BeatId, bool Dismissed) : SimEvent(Step, Seq);

    /// <summary>The recipe card at <paramref name="Card"/> was learned (and consumed): <paramref name="RecipeId"/> is now known.</summary>
    public sealed record RecipeLearned(int Step, int Seq, string RecipeId, CellRef Card) : SimEvent(Step, Seq);

    /// <summary>The player crossed into the wilderness <paramref name="Bag"/> for the first time (a minimap wedge).</summary>
    public sealed record ZoneReached(int Step, int Seq, Guid Bag) : SimEvent(Step, Seq);

    /// <summary>An element-wise hash for the array payloads (records hash arrays by reference).</summary>
    private static int ArrayHash<T>(ImmutableArray<T> items) =>
        items.IsDefault ? 0 : items.Aggregate(items.Length, (h, x) => HashCode.Combine(h, x));

    /// <summary>Element-wise equality for the array payloads (a default array equals only a default array).</summary>
    private static bool ArrayEq<T>(ImmutableArray<T> a, ImmutableArray<T> b) =>
        a.IsDefault || b.IsDefault ? a.IsDefault == b.IsDefault : a.SequenceEqual(b);

    /// <summary>
    /// <b>Reserved (P1-D3); no belts exist yet.</b> One event per belt chain per step: the chain moved
    /// <paramref name="Count"/> items in <paramref name="Dir"/>, with its head at cell index
    /// <paramref name="HeadCell"/> (row-major). L2 derives per-item poses from chain plus occupancy;
    /// per-item events are emitted only at chain boundaries.
    /// </summary>
    public sealed record BeltAdvanced(int Step, int Seq, int ChainId, Direction Dir, int Count, int HeadCell)
        : SimEvent(Step, Seq);
}

/// <summary>A cell of a bag: the bag's store id and the row-major cell index.</summary>
public readonly record struct CellRef(Guid Bag, int Index);

/// <summary>An item stack placed at a cell (craft outputs, harvest yields).</summary>
public sealed record Placed(ItemType Item, int Count, CellRef Cell);

/// <summary>Why a stack moved, split, merged, appeared or vanished.</summary>
public enum StackCause
{
    /// <summary>Taken into the hand.</summary>
    Grab = 1,
    /// <summary>Put down from the hand (including place-one).</summary>
    Drop,
    /// <summary>Hand and cell exchanged contents.</summary>
    Swap,
    /// <summary>Split off a stack into the hand.</summary>
    Split,
    /// <summary>A bare pickup flew to the toolbar (the demo's fixed-inventory routing).</summary>
    Pickup,
    /// <summary>Harvested out of a nested bag into its parent.</summary>
    Harvest,
    /// <summary>A core slotted into a feature slot.</summary>
    Slot,
    /// <summary>The debug AcquireRandom tool.</summary>
    Acquire,
    /// <summary>A DSL program.</summary>
    Dsl,
    /// <summary>A facility's slots were dumped when its recipe was switched.</summary>
    RecipeSwitch,
}

/// <summary>Why a move or look was refused (<see cref="SimEvent.Bumped"/>).</summary>
public enum BumpReason
{
    /// <summary>The target cell is unnavigable (a tree).</summary>
    Unnavigable = 1,
    /// <summary>An enter-only bag refused a look-in peek.</summary>
    FailedPeek,
}
