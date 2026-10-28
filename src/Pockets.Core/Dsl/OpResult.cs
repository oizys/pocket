using System.Collections.Immutable;
using Pockets.Core.Models;

namespace Pockets.Core.Dsl;

/// <summary>
/// The state token that flows through the DSL stack. Every opcode consumes one
/// and produces one. Carries the current game state, the snapshot from before
/// the expression started (for undo/diff), accumulated errors, and optional
/// surface values to push onto the stack after the OpResult.
/// </summary>
public record OpResult(
    GameState State,
    GameState Before,
    ImmutableList<string> Errors,
    ImmutableArray<object> Pushed = default)
{
    /// <summary>
    /// True when no errors have accumulated.
    /// </summary>
    public bool IsOk => Errors.Count == 0;

    /// <summary>
    /// The RNG random opcodes draw from (Slice 1.1): the session's SimRng over <c>SimStream.Dsl</c>.
    /// Null when the program runs without one, in which case random opcodes fail instead of drawing
    /// from an unseeded source. Carried unchanged through every chain step.
    /// </summary>
    public Random? Rng { get; init; }

    /// <summary>
    /// Creates the initial OpResult for a new expression, optionally carrying the RNG random opcodes use.
    /// </summary>
    public static OpResult Initial(GameState state, Random? rng = null) =>
        new(state, state, ImmutableList<string>.Empty) { Rng = rng };

    /// <summary>
    /// Thread state forward: new state, preserve Before and accumulated errors.
    /// </summary>
    public OpResult Chain(GameState newState) =>
        this with { State = newState, Pushed = default };

    /// <summary>
    /// Accumulate an error without changing state.
    /// </summary>
    public OpResult ChainError(string error) =>
        this with { Errors = Errors.Add(error), Pushed = default };

    /// <summary>
    /// Chain a new state and accumulate an error.
    /// </summary>
    public OpResult Chain(GameState newState, string error) =>
        this with { State = newState, Errors = Errors.Add(error), Pushed = default };

    /// <summary>
    /// Clear all errors (used by try on the success path).
    /// </summary>
    public OpResult ClearErrors() =>
        this with { Errors = ImmutableList<string>.Empty, Pushed = default };

    /// <summary>
    /// Returns a new OpResult with a value to push onto the stack after the OpResult itself.
    /// State is unchanged. Used by query opcodes to push bools/ints.
    /// </summary>
    public OpResult WithPushed(object value) =>
        this with { Pushed = ImmutableArray.Create(value) };

    /// <summary>
    /// Returns a new OpResult with multiple values to push onto the stack.
    /// Values are pushed in order (first item ends up deepest).
    /// </summary>
    public OpResult WithPushed(params object[] values) =>
        this with { Pushed = values.ToImmutableArray() };
}
