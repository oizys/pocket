namespace Pockets.Core.Models;

/// <summary>
/// Result of a tool operation. Contains the (possibly unchanged) GameState,
/// whether the operation succeeded, and an optional error message.
/// Failed operations return the original state unmodified.
/// </summary>
public record ToolResult(GameState State, bool Success, string? Error = null)
{
    /// <summary>
    /// Creates a successful result with the given state.
    /// </summary>
    public static ToolResult Ok(GameState state) => new(state, true);

    /// <summary>
    /// Creates a failed result, returning the original state unchanged.
    /// </summary>
    public static ToolResult Fail(GameState state, string error) => new(state, false, error);
}
