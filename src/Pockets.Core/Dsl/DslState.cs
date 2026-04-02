using System.Collections.Immutable;
using Pockets.Core.Models;

namespace Pockets.Core.Dsl;

/// <summary>
/// The execution state of a DSL program. Wraps GameState with a data stack
/// for passing values between opcodes. No flags register — results flow
/// through the stack explicitly.
/// </summary>
public record DslState(
    GameState Game,
    ImmutableStack<object> DataStack)
{
    /// <summary>
    /// Creates an initial DslState with an empty data stack.
    /// </summary>
    public static DslState From(GameState game) =>
        new(game, ImmutableStack<object>.Empty);

    /// <summary>
    /// Pushes a value onto the data stack.
    /// </summary>
    public DslState Push(object value) =>
        this with { DataStack = DataStack.Push(value) };

    /// <summary>
    /// Pops a value from the data stack, returning the value and new state.
    /// Throws if stack is empty.
    /// </summary>
    public (T Value, DslState State) Pop<T>()
    {
        if (DataStack.IsEmpty)
            throw new InvalidOperationException($"DSL stack underflow: expected {typeof(T).Name}");

        var value = DataStack.Peek();
        var newStack = DataStack.Pop();

        if (value is T typed)
            return (typed, this with { DataStack = newStack });

        throw new InvalidOperationException(
            $"DSL type error: expected {typeof(T).Name} on stack, got {value?.GetType().Name ?? "null"}");
    }

    /// <summary>
    /// Peeks at the top of the data stack without popping.
    /// Returns null if stack is empty.
    /// </summary>
    public object? Peek() =>
        DataStack.IsEmpty ? null : DataStack.Peek();

    /// <summary>
    /// True if the data stack is empty.
    /// </summary>
    public bool IsStackEmpty => DataStack.IsEmpty;
}
