namespace Pockets.Core.Sim;

/// <summary>
/// The two rule kinds (two-layer #23, Rule kinds); every rule is exactly one. No zero member, so an
/// undeclared value can never pass as a real kind (Slice 1.4 registry test).
/// </summary>
public enum RuleKind
{
    /// <summary>Gather: a cell computes its own next value from its previous-buffer neighbourhood.</summary>
    Field = 1,

    /// <summary>Propose → resolve → commit: writes to another cell, so conflicts are resolved.</summary>
    Transfer = 2
}
