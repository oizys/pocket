namespace Pockets.Core.Sim;

/// <summary>
/// How a rule's state can be advanced over Δ steps (two-layer #23, §Time). Every rule declares one
/// explicitly: there is no zero member, so a default can never pass as a declaration (Slice 1.4).
/// </summary>
public enum TimeClass
{
    /// <summary>Δ steps computed directly by formula (e.g. a capped growth counter).</summary>
    ClosedForm = 1,

    /// <summary>Jump straight to the next discrete event (e.g. a craft countdown completing).</summary>
    EventSkippable = 2,

    /// <summary>Simulate at most <see cref="IRule.MaxDelta"/> steps of catch-up, then snap.</summary>
    BoundedCatchUp = 3,

    /// <summary>Must be stepped one step at a time; no shortcut.</summary>
    MustSimulate = 4
}
