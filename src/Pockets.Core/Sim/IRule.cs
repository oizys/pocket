namespace Pockets.Core.Sim;

/// <summary>
/// A simulation rule's declaration (two-layer #23, Rule declaration): its kind, its time class and
/// the event kinds it may emit. Behaviour members (<c>IFieldRule.Next</c>,
/// <c>ITransferRule.Propose</c>) arrive with the rule engine; Slice 1.4 registers the existing
/// facility and plant ticks against this.
/// </summary>
public interface IRule
{
    /// <summary>Field or Transfer.</summary>
    RuleKind Kind { get; }

    /// <summary>How the rule catches up over Δ steps. Declared explicitly; no default.</summary>
    TimeClass Time { get; }

    /// <summary>
    /// Catch-up cap in steps, meaningful only for <see cref="TimeClass.BoundedCatchUp"/>, which
    /// must override it with a value &gt; 0. Zero for every other class.
    /// </summary>
    int MaxDelta => 0;

    /// <summary>The <see cref="SimEvent"/> case types this rule may emit.</summary>
    ImmutableArray<Type> EventKinds { get; }
}
