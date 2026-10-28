using Pockets.Core.Sim;

namespace Pockets.Core.Models;

/// <summary>
/// Result of a GameController input action. Contains the updated session,
/// an optional status message for the UI, whether the input was handled, and the
/// <see cref="SimEvent"/>s the input's steps emitted.
/// </summary>
public record ControllerResult(GameSession Session, string? StatusMessage, bool Handled)
{
    /// <summary>
    /// Every event the call's intents emitted, in order (Slice 1.6). At n = 1 a frontend applies them
    /// instantly: it re-renders from the view-model and plays one-shot cues (a <see cref="SimEvent.Bumped"/>
    /// shake/flash) from the events. Empty when the call sent no intent.
    /// </summary>
    public ImmutableArray<SimEvent> Events { get; init; } = ImmutableArray<SimEvent>.Empty;

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
