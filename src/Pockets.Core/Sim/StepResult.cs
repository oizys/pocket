namespace Pockets.Core.Sim;

/// <summary>
/// The output of one <c>Sim.Step</c>: the next state and the events of that step, in canonical
/// <see cref="SimEvent.Order"/>. Generic over the state type so Slice 1.2 picks the concrete
/// <c>SimState</c> shape (see the plan's SimState boundary inventory). Equality compares
/// <see cref="Events"/> element-wise, not by array reference.
/// </summary>
public sealed record StepResult<TState>(TState State, ImmutableArray<SimEvent> Events)
{
    /// <summary>A step that changed nothing: same state, no events (today's no-op success).</summary>
    public static StepResult<TState> Unchanged(TState state) => new(state, ImmutableArray<SimEvent>.Empty);

    /// <summary>Value equality: same state and the same events in the same order.</summary>
    public bool Equals(StepResult<TState>? other) =>
        other is not null
        && EqualityComparer<TState>.Default.Equals(State, other.State)
        && Events.SequenceEqual(other.Events);

    /// <summary>Hash consistent with <see cref="Equals(StepResult{TState}?)"/>.</summary>
    public override int GetHashCode() =>
        Events.Aggregate(EqualityComparer<TState>.Default.GetHashCode(State!), HashCode.Combine);
}
