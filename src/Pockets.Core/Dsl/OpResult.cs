using System.Collections.Immutable;
using Pockets.Core.Models;

namespace Pockets.Core.Dsl;

/// <summary>
/// The state token that flows through the DSL stack. Every opcode consumes one
/// and produces one. Carries the current game state, the snapshot from before
/// the expression started (for undo/diff), and accumulated errors.
/// </summary>
public record OpResult(
    GameState State,
    GameState Before,
    ImmutableList<string> Errors)
{
    /// <summary>
    /// True when no errors have accumulated.
    /// </summary>
    public bool IsOk => Errors.Count == 0;

    /// <summary>
    /// Creates the initial OpResult for a new expression.
    /// </summary>
    public static OpResult Initial(GameState state) =>
        new(state, state, ImmutableList<string>.Empty);

    /// <summary>
    /// Thread state forward: new state, preserve Before and accumulated errors.
    /// </summary>
    public OpResult Chain(GameState newState) =>
        new(newState, Before, Errors);

    /// <summary>
    /// Accumulate an error without changing state.
    /// </summary>
    public OpResult ChainError(string error) =>
        new(State, Before, Errors.Add(error));

    /// <summary>
    /// Chain a new state and accumulate an error.
    /// </summary>
    public OpResult Chain(GameState newState, string error) =>
        new(newState, Before, Errors.Add(error));

    /// <summary>
    /// Clear all errors (used by try on the success path).
    /// </summary>
    public OpResult ClearErrors() =>
        new(State, Before, ImmutableList<string>.Empty);
}
