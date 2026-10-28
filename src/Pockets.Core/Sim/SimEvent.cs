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

    /// <summary>An intent was applied and changed state (coarse, Slice 1.2).</summary>
    public sealed record IntentApplied(int Step, int Seq, Intent Intent) : SimEvent(Step, Seq);

    /// <summary>An intent failed; state is unchanged. L2 reconciles any prediction against it.</summary>
    public sealed record IntentRejected(int Step, int Seq, Intent Intent, string Reason) : SimEvent(Step, Seq);

    /// <summary>The step advanced simulated time (facilities and plants ticked).</summary>
    public sealed record TimeAdvanced(int Step, int Seq) : SimEvent(Step, Seq);

    /// <summary>A facility finished a craft (coarse, Slice 1.2; Slice 1.5 adds the outputs).</summary>
    public sealed record CraftCompleted(int Step, int Seq, Guid FacilityBagId, string RecipeId) : SimEvent(Step, Seq);

    /// <summary>A gameplay trigger grew the UI ledger.</summary>
    public sealed record UiTriggerFired(int Step, int Seq, UiTrigger Trigger) : SimEvent(Step, Seq);

    /// <summary>
    /// <b>Reserved (P1-D3); no belts exist yet.</b> One event per belt chain per step: the chain moved
    /// <paramref name="Count"/> items in <paramref name="Dir"/>, with its head at cell index
    /// <paramref name="HeadCell"/> (row-major). L2 derives per-item poses from chain plus occupancy;
    /// per-item events are emitted only at chain boundaries.
    /// </summary>
    public sealed record BeltAdvanced(int Step, int Seq, int ChainId, Direction Dir, int Count, int HeadCell)
        : SimEvent(Step, Seq);
}
