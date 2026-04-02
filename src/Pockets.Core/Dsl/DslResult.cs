using Pockets.Core.Models;

namespace Pockets.Core.Dsl;

/// <summary>
/// Result of a DSL opcode execution. Carries the updated game state,
/// success/failure, and optional remainder (e.g. items that didn't fit).
/// Pushed onto the data stack by each opcode; combinators consume results.
/// </summary>
public record DslResult(
    GameState State,
    bool Success,
    string? Error = null,
    ItemStack? Remainder = null)
{
    public static DslResult Ok(GameState state, ItemStack? remainder = null) =>
        new(state, true, Remainder: remainder);

    public static DslResult Fail(GameState state, string error) =>
        new(state, false, error);
}
