using System.Collections.Immutable;

namespace Pockets.Core.Models;

/// <summary>
/// Runtime dialogue progression, carried on <see cref="GameState"/>. A small queue of beat ids
/// (front = the beat currently showing) plus the line index within it, the set of beats that have
/// already fired (the fire-once guard), and the set of unique item types the cursor has inspected
/// (the counter the <see cref="DialogueTriggerKind.NthUniqueInspect"/> condition reads).
///
/// Undo semantics (decided for Slice 2): dialogue progression is <b>monotonic and never rewinds</b>
/// — <see cref="GameSession.Undo"/> carries this substate forward across a restore. Rationale:
/// (1) dialogue is narrative, like cursor movement, which is already non-undoable; (2) fire-once is
/// only sound if <see cref="FiredBeats"/> can't be rolled back to let a beat re-fire; (3) the
/// simplest correct behavior is narrative that only moves forward. Beat firing, advancing, and
/// dismissing therefore never push the undo stack.
/// </summary>
public record DialogueState(
    ImmutableList<string> Queue,
    int LineIndex,
    ImmutableHashSet<string> FiredBeats,
    ImmutableHashSet<string> InspectedItems)
{
    /// <summary>Nothing showing, nothing fired, nothing inspected.</summary>
    public static readonly DialogueState Empty = new(
        ImmutableList<string>.Empty, 0,
        ImmutableHashSet<string>.Empty, ImmutableHashSet<string>.Empty);

    /// <summary>The beat currently showing (front of the queue), or null when the box is closed.</summary>
    public string? ActiveBeatId => Queue.Count > 0 ? Queue[0] : null;

    /// <summary>True when a beat is currently showing.</summary>
    public bool IsActive => Queue.Count > 0;

    /// <summary>Whether a beat has already fired (fire-once guard).</summary>
    public bool HasFired(string beatId) => FiredBeats.Contains(beatId);

    /// <summary>Number of unique item types inspected so far.</summary>
    public int UniqueInspectCount => InspectedItems.Count;

    /// <summary>
    /// Fires a beat: marks it fired and appends it to the queue. Idempotent per beat — a beat
    /// already in <see cref="FiredBeats"/> is a no-op (returns the same instance), which is what
    /// makes every condition fire-once even under rapid input. If the queue was empty the beat
    /// becomes active at line 0.
    /// </summary>
    public DialogueState Enqueue(string beatId)
    {
        if (FiredBeats.Contains(beatId))
            return this;
        return this with
        {
            FiredBeats = FiredBeats.Add(beatId),
            Queue = Queue.Add(beatId),
            LineIndex = Queue.Count == 0 ? 0 : LineIndex
        };
    }

    /// <summary>
    /// Records that the cursor inspected an item type. Returns the same instance when the type has
    /// already been inspected, so re-scanning the same items never grows the unique count (a
    /// rapid back-and-forth scan can't push a threshold twice).
    /// </summary>
    public DialogueState Inspect(string itemName) =>
        InspectedItems.Contains(itemName)
            ? this
            : this with { InspectedItems = InspectedItems.Add(itemName) };

    /// <summary>
    /// Advances the active beat by one line. If already on the last line, dismisses it (dequeues
    /// the front and resets the line index). Returns the new state and the id of any beat that was
    /// dismissed (so the caller can apply its on-dismiss chrome reveal). No-op when nothing is showing.
    /// </summary>
    public (DialogueState State, string? Dismissed) Advance(int activeLineCount)
    {
        if (Queue.Count == 0)
            return (this, null);
        if (LineIndex + 1 < activeLineCount)
            return (this with { LineIndex = LineIndex + 1 }, null);
        var dismissed = Queue[0];
        return (this with { Queue = Queue.RemoveAt(0), LineIndex = 0 }, dismissed);
    }
}
