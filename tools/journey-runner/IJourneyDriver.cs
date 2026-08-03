using System.Text.Json.Nodes;
using Pockets.Core.Models;

namespace Pockets.JourneyRunner;

/// <summary>
/// What a driver reports after a step. <see cref="ViewModel"/> is always present and is
/// the canonical cross-driver checkpoint. <see cref="FullState"/> is the complete GameState,
/// available only for in-process drivers (TUI, mock-godot) — the full invariant pack and
/// conservation census require it. <see cref="RenderProbe"/> is a per-frontend render dump
/// (TUI: character buffer) for thin assert-render checks; null when the driver has no
/// observable render surface here.
/// </summary>
public record DriverObservation(JsonObject ViewModel, GameState? FullState, string? RenderProbe);

/// <summary>
/// A frontend driver the journey runner can plug in. All frontends converge on
/// <see cref="Pockets.Core.Models.GameController"/>; each driver exercises its own path to it
/// (TUI: GameView under FakeDriver; Godot: WebSocket; mock-godot: shared debug handler in
/// process) and reports observations through the same shape so scripts and checkpoints are
/// driver-agnostic.
/// </summary>
public interface IJourneyDriver : IDisposable
{
    /// <summary>Human-readable driver name for logs/checkpoints (e.g. "tui", "mock-godot").</summary>
    string Name { get; }

    /// <summary>Reads the current observation without mutating state.</summary>
    DriverObservation Observe();

    /// <summary>Sends a logical game key.</summary>
    void Key(GameKey key);

    /// <summary>Advances one facility tick (realtime "wait").</summary>
    void Tick();

    /// <summary>Presses the back / leave-bag affordance.</summary>
    void Back();

    /// <summary>Clicks a grid cell in the active (B) panel.</summary>
    void Click(Position pos, ClickType type);
}
