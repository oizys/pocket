using System.Collections.Immutable;
using Pockets.Core.Models;
using Pockets.Core.Sim;

namespace Pockets.Realization;

/// <summary>
/// One drawn pose and its rest key (Slice 2.5): the scene entity the pose stands for. For a stack in flight the pose
/// carries its departure key while <see cref="Rest"/> is the cell it lands in; at rest the two are the same.
/// </summary>
/// <param name="Pose">The sampled pose.</param>
/// <param name="Rest">The rest entity (a stack's landing cell, an overlay's target, else the pose's own key).</param>
public readonly record struct DrawnPose(Pose Pose, EntityKey Rest);

/// <summary>
/// The frame-loop side of a timeline (Slice 2.5), engine-neutral so it is tested without Godot. A frontend commits
/// every <see cref="ControllerResult"/>'s events (input and clock) and advances it by each frame's integer ms; the
/// poses to draw are the timeline sampled at the accumulated time, exactly as the pose golden samples it.
/// </summary>
public sealed class PosePlayer
{
    private readonly RealizationPolicy _policy;

    /// <summary>Starts at rest on <paramref name="rest"/>; <paramref name="policy"/> defaults to <see cref="RealizationPolicy.Default"/>.</summary>
    public PosePlayer(Scene rest, RealizationPolicy? policy = null)
    {
        _policy = policy ?? RealizationPolicy.Default;
        Timeline = Timeline.Rest(rest);
    }

    /// <summary>Starts at rest on <paramref name="initial"/>'s scene of the <see cref="PoseStream.Shown"/> locations, offset by <paramref name="fine"/>.</summary>
    public PosePlayer(GameState initial, IFineView? fine = null, RealizationPolicy? policy = null)
        : this(Scene.FromState(initial, PoseStream.Shown, fine), policy)
    {
    }

    /// <summary>The current timeline.</summary>
    public Timeline Timeline { get; private set; }

    /// <summary>Integer ms since the last commit, clamped to the timeline's span.</summary>
    public int ElapsedMs { get; private set; }

    /// <summary>True while some track is still playing.</summary>
    public bool IsAnimating => ElapsedMs < Timeline.SpanMs;

    /// <summary>The poses to draw now, with their rest keys.</summary>
    public ImmutableArray<DrawnPose> Poses => Timeline.SampleDrawn(ElapsedMs);

    /// <summary>
    /// Commits <paramref name="events"/> leading to <paramref name="next"/> at clock delta <paramref name="atMs"/> and
    /// restarts the frame time. Input snaps every live track inside <see cref="Timeline.Commit"/> (D7) and a
    /// <c>Rewound</c> resets (A3), so the caller does nothing extra for either. <paramref name="fineTick"/> marks an integrator tick's commit, which
    /// never snaps (P2-D13).
    /// </summary>
    public void Commit(IEnumerable<SimEvent> events, Scene next, int atMs = 0, bool fineTick = false)
    {
        Timeline = Timeline.Commit(events, next, _policy, atMs, fineTick);
        ElapsedMs = 0;
    }

    /// <summary>
    /// Commits against <paramref name="next"/>'s scene, built as <see cref="PoseStream"/> builds it (the shown
    /// locations, <paramref name="fine"/>'s offsets, the timeline's facings), so the drawing matches the pose golden.
    /// </summary>
    public void Commit(IEnumerable<SimEvent> events, GameState next, int atMs, IFineView? fine, bool fineTick = false) =>
        Commit(events, Scene.FromState(next, PoseStream.Shown, fine, Timeline.Facings), atMs, fineTick);

    /// <summary>
    /// Advances the frame time by <paramref name="deltaMs"/>, clamped at the span. Returns true when the drawn poses
    /// may have changed (the frontend redraws), false at rest or for a non-positive delta.
    /// </summary>
    public bool Advance(int deltaMs)
    {
        if (deltaMs <= 0 || !IsAnimating) return false;
        var left = Timeline.SpanMs - ElapsedMs;
        ElapsedMs = deltaMs >= left ? Timeline.SpanMs : ElapsedMs + deltaMs;
        return true;
    }
}

/// <summary>
/// Turns a monotonic integer ms reading (Godot's <c>Time.GetTicksMsec</c>) into per-frame deltas (Slice 2.5). No
/// floating point: the frame loop accumulates integer ms for sampling only and never feeds the L1 clock.
/// </summary>
public sealed class FrameClock
{
    private long? _last;

    /// <summary>The ms since the previous reading: 0 on the first, 0 if the reading went backwards, at most int.MaxValue.</summary>
    public int Tick(long nowMs)
    {
        var delta = _last is { } last ? nowMs - last : 0;
        _last = nowMs;
        return delta <= 0 ? 0 : delta >= int.MaxValue ? int.MaxValue : (int)delta;
    }
}
