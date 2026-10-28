using Pockets.Core.Models;

namespace Pockets.Core.Sim;

/// <summary>
/// A rule the tick phase runs (Slice 1.4): one pass over the whole state per time step. Its writes to grids
/// go through <see cref="IBagWriter"/> (the SetItem ban). Phase 3's field/transfer engine replaces this
/// coarse whole-state pass with <c>IFieldRule.Next</c> / <c>ITransferRule.Propose</c>.
/// </summary>
public interface ITickRule : IRule
{
    /// <summary>Advances <paramref name="state"/> by one step of this rule.</summary>
    GameState Apply(GameState state, RuleContext context);
}

/// <summary>
/// What a rule pass reads besides the state: the step number, the pre-step known-recipe set (the session
/// always ticked against the pre-action <see cref="GameState.KnownRecipes"/>, Slice 1.2 subtlety 2), the L1
/// content, the step's <see cref="SimStream.Craft"/> cursor (advanced in place, committed by the caller),
/// and <see cref="Emit"/>, which queues an event whose <c>Seq</c> is assigned when the step's list is built.
/// </summary>
public sealed record RuleContext(
    int Step,
    ImmutableHashSet<string> KnownRecipes,
    SimContent Content,
    Random CraftRandom,
    Action<Func<int, SimEvent>> Emit);

/// <summary>
/// The registered rules, in canonical tick-phase order (Slice 1.4): facilities (in
/// <see cref="Simulation.OrderedFacilities"/> order), then plants — the order the tick phase has always
/// had. <see cref="Simulation"/>'s <c>AdvanceTime</c> runs exactly this list.
/// </summary>
public static class RuleRegistry
{
    /// <summary>The tick-phase rules, in run order.</summary>
    public static ImmutableArray<ITickRule> TickPhase { get; } =
        ImmutableArray.Create<ITickRule>(FacilityCraftRule.Instance, PlantGrowthRule.Instance);
}
