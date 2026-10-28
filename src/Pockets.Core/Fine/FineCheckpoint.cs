using Pockets.Core.Sim;

namespace Pockets.Core.Fine;

/// <summary>The fine state at a realtime checkpoint, parallel to the session's L1 checkpoint of the same <paramref name="Mark"/>.</summary>
public sealed record FineCheckpoint(LogMark Mark, FineState Fine);
