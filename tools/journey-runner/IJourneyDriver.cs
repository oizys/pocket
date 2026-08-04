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

    /// <summary>
    /// Advances the game clock by a scripted amount (Slice 5). Deterministic time for parity — the
    /// harness NEVER reads the wall clock; every advance is an explicit journey step.
    /// </summary>
    void AdvanceTime(TimeSpan delta);

    /// <summary>Presses the back / leave-bag affordance.</summary>
    void Back();

    /// <summary>Clicks a grid cell in the active (B) panel.</summary>
    void Click(Position pos, ClickType type);
}

/// <summary>
/// A driver that can capture a per-frontend screenshot at a golden checkpoint (Slice 8). Only the
/// real Godot driver implements this — the screenshot needs the live Godot viewport, so the capture
/// runs on a machine with a Godot runtime (Aaron's Windows box) as part of <c>make parity-godot</c>.
/// The runner probes for this interface and, when <c>--screenshots</c> is set, asks each golden
/// checkpoint's driver to save a PNG; drivers without a viewport (tui, mock-godot) simply don't
/// implement it and are skipped with a note.
/// </summary>
public interface IScreenshotDriver
{
    /// <summary>Saves a screenshot of the current frontend frame to <paramref name="path"/>.</summary>
    void Screenshot(string path);
}
