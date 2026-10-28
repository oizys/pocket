using Pockets.Core.Models;

namespace Pockets.Core.Sim;

/// <summary>
/// A player or scheduler input to <c>Sim.Step</c> (two-layer #23, Step contract: intents are the only
/// input). A closed hierarchy: the constructor is private, so every case is a sealed record nested
/// here. Every case is plain data — no closures, no bag GUIDs — so an intent log replays exactly in a
/// fresh session (Slice 1.3). Every case that acts on a panel names its target <see cref="LocationId"/>
/// explicitly, so replay never depends on the controller's panel focus (P1-D5).
/// <para>
/// History: Slice 1.0 declared <see cref="Tick"/>; 1.2 added the transitional closure-carrying
/// <c>Tool</c>; 1.1 added <see cref="AcquireRandom"/>; 1.3 deleted <c>Tool</c> for one data case per
/// controller path. Whether an action advances time is not on the intent: <c>Step</c> reads it from
/// <see cref="SimContent.TickMode"/> (rogue: yes; realtime: no, P1-D2).
/// </para>
/// </summary>
public abstract record Intent
{
    private Intent() { }

    // ==================== Clock ====================

    /// <summary>
    /// Advance simulated time by one step with no player action (realtime clock tick,
    /// <c>TickLengthMs = 1000</c> per P1-D4).
    /// </summary>
    public sealed record Tick : Intent;

    /// <summary>
    /// Set the clock readout (<see cref="SimState.Elapsed"/>) to <paramref name="Elapsed"/>. The scheduler
    /// sends this before the <see cref="Tick"/>s a clock sync owes. Changes nothing else: no step, no hooks.
    /// </summary>
    public sealed record SetElapsed(TimeSpan Elapsed) : Intent;

    // ==================== Cursor ====================

    /// <summary>
    /// Move <paramref name="At"/>'s cursor one cell (wrapping). A tree cell refuses the move and records a
    /// bump; a B cursor coming to rest on an item runs the cursor-rest triggers. Not undoable, not logged.
    /// </summary>
    public sealed record Move(LocationId At, Direction Dir) : Intent;

    /// <summary>Place <paramref name="At"/>'s cursor on <paramref name="Pos"/> directly (no triggers). Not undoable.</summary>
    public sealed record SetCursor(LocationId At, Position Pos) : Intent;

    /// <summary>
    /// A mouse click on <paramref name="Pos"/> of panel <paramref name="At"/>: <see cref="SetCursor"/>, then
    /// <see cref="Primary"/> (left) or <see cref="Secondary"/> (right) at that panel.
    /// </summary>
    public sealed record Click(LocationId At, Position Pos, ClickType Type) : Intent;

    // ==================== Panel verbs (routed) ====================

    /// <summary>
    /// The Primary key / left click at <paramref name="At"/>, routed by the state: a bag already open as
    /// C/W toggles closed; a facility or wilderness bag in B opens as C/W; a toolbar bag is peeked; a bare
    /// demo pickup flies to the toolbar; anything else runs the contextual <see cref="ToolPrimary"/>.
    /// </summary>
    public sealed record Primary(LocationId At) : Intent;

    /// <summary>
    /// The look-in key at <paramref name="At"/>: closes a focused C/W panel, toggles a peeked bag closed,
    /// refuses an enter-only bag (failed-peek beat), or opens the cursor bag as a C look-in overlay.
    /// </summary>
    public sealed record Peek(LocationId At) : Intent;

    /// <summary>
    /// Leave / back at <paramref name="At"/>: closes <paramref name="At"/> when it is an open C/W panel,
    /// otherwise leaves the B bag (the Q key and the back button).
    /// </summary>
    public sealed record Leave(LocationId At) : Intent;

    // ==================== Tools (one per tool, run at a location) ====================

    /// <summary>The contextual primary tool at <paramref name="At"/> (grab/drop/swap/merge/enter/harvest). Undoable.</summary>
    public sealed record ToolPrimary(LocationId At) : Intent;

    /// <summary>The secondary tool at <paramref name="At"/> (grab half / place one). Undoable.</summary>
    public sealed record Secondary(LocationId At) : Intent;

    /// <summary>Split the cursor stack at <paramref name="At"/> in half into the hand. Undoable.</summary>
    public sealed record QuickSplit(LocationId At) : Intent;

    /// <summary>Split the cursor stack at <paramref name="At"/>, leaving <paramref name="LeftCount"/>. Undoable.</summary>
    public sealed record ModalSplit(LocationId At, int LeftCount) : Intent;

    /// <summary>Sort and merge the bag at <paramref name="At"/>. Undoable.</summary>
    public sealed record Sort(LocationId At) : Intent;

    /// <summary>
    /// The debug AcquireRandom tool (key 5): acquire one item of a random type into the bag at
    /// <paramref name="At"/>. The type is drawn inside <c>Step</c> from <see cref="SimStream.Acquire"/>.
    /// </summary>
    public sealed record AcquireRandom(LocationId At) : Intent;

    /// <summary>Grab the cursor stack at <paramref name="At"/> into the hand. Undoable.</summary>
    public sealed record Grab(LocationId At) : Intent;

    /// <summary>Drop the hand at <paramref name="At"/>'s cursor. Undoable.</summary>
    public sealed record Drop(LocationId At) : Intent;

    /// <summary>Context-sensitive interact at <paramref name="At"/> (enter a bag, harvest). Undoable.</summary>
    public sealed record Interact(LocationId At) : Intent;

    /// <summary>Enter the bag at <paramref name="At"/>'s cursor. Undoable.</summary>
    public sealed record EnterBag(LocationId At) : Intent;

    /// <summary>Leave <paramref name="At"/>'s current bag for its parent. Undoable.</summary>
    public sealed record LeaveBag(LocationId At) : Intent;

    /// <summary>Harvest the cursor item at <paramref name="At"/> into the parent bag. Undoable.</summary>
    public sealed record Harvest(LocationId At) : Intent;

    /// <summary>Cycle the active recipe of the facility at <paramref name="At"/> (legacy filtered-slot facilities). Undoable.</summary>
    public sealed record CycleRecipe(LocationId At) : Intent;

    /// <summary>Run a DSL program against the state (inside <c>Step</c>, drawing from <see cref="SimStream.Dsl"/>). Undoable.</summary>
    public sealed record Dsl(string Expression) : Intent;

    // ==================== Modal state (L1, P1-D5) ====================

    /// <summary>Advance the showing dialogue beat one line, or dismiss it after its last line. Not undoable.</summary>
    public sealed record AdvanceDialogue : Intent;

    /// <summary>Open the inline split editor on <paramref name="At"/>'s cursor stack (needs 2+ items).</summary>
    public sealed record BeginSplit(LocationId At) : Intent;

    /// <summary>Change the inline split's grab count by <paramref name="Delta"/>, clamped to [1, total − 1].</summary>
    public sealed record AdjustSplit(int Delta) : Intent;

    /// <summary>Commit the inline split (a <see cref="ModalSplit"/> at the split's location), then close the editor.</summary>
    public sealed record CommitSplit : Intent;

    /// <summary>Close the inline split editor with no other change.</summary>
    public sealed record CancelSplit : Intent;

    /// <summary>Open the modal recipe menu on the facility active at <paramref name="At"/>.</summary>
    public sealed record OpenRecipeMenu(LocationId At) : Intent;

    /// <summary>Move the recipe-menu selection by <paramref name="Delta"/>, clamped.</summary>
    public sealed record MoveRecipeMenu(int Delta) : Intent;

    /// <summary>Set the facility's active recipe to the menu selection and close the menu. Undoable.</summary>
    public sealed record ConfirmRecipeMenu : Intent;

    /// <summary>Close the recipe menu with no other change.</summary>
    public sealed record CloseRecipeMenu : Intent;

    // ==================== Shell ====================

    /// <summary>
    /// Restore the previous undo snapshot. Handled by the session shell, which owns the snapshot stack;
    /// it is an intent so the intent log replays it. Dialogue, the RNG and <c>TickCount</c> keep moving forward.
    /// </summary>
    public sealed record Undo : Intent;

    /// <summary>
    /// Test-only (P2-D11): set the L2a offset of cell <paramref name="Index"/> of <paramref name="At"/>'s active bag.
    /// Handled by the session shell like <see cref="Undo"/>, which owns the fine state; it never reaches Step.
    /// </summary>
    public sealed record SetFineOffset(LocationId At, int Index, int Dx, int Dy) : Intent;
}
