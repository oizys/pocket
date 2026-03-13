namespace Pockets.Core.Models;

/// <summary>
/// Result of a GameController input action. Contains the updated session,
/// an optional status message for the UI, and whether the input was handled.
/// </summary>
public record ControllerResult(GameSession Session, string? StatusMessage, bool Handled)
{
    /// <summary>
    /// Creates a handled result with a status message.
    /// </summary>
    public static ControllerResult Handle(GameSession session, string statusMessage) =>
        new(session, statusMessage, true);

    /// <summary>
    /// Creates an unhandled result (key not recognized).
    /// </summary>
    public static ControllerResult Unhandled(GameSession session) =>
        new(session, null, false);
}
