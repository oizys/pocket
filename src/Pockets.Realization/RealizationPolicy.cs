namespace Pockets.Realization;

/// <summary>Realization tuning (P2-D5): every duration is integer milliseconds.</summary>
public sealed record RealizationPolicy
{
    /// <summary>A body's one-cell step.</summary>
    public int WalkMs { get; init; } = 125;
    /// <summary>A bump against an obstacle.</summary>
    public int BumpMs { get; init; } = 120;
    /// <summary>A refusal shake.</summary>
    public int ShakeMs { get; init; } = 150;
    /// <summary>A stack sliding between cells.</summary>
    public int SlideMs { get; init; } = 125;
    /// <summary>A sort reshuffle.</summary>
    public int SortMs { get; init; } = 200;
    /// <summary>A pop (appear or disappear) effect.</summary>
    public int PopMs { get; init; } = 160;
    /// <summary>How much of the previous track a reaction track may overlap, in percent.</summary>
    public int ReactionOverlapPercent { get; init; } = 50;
    /// <summary>The inventory cursor glide (linear).</summary>
    public int CursorGlideMs { get; init; } = 60;
    /// <summary>The easing curve.</summary>
    public EaseKind Ease { get; init; } = EaseKind.Smooth;

    /// <summary>The default policy.</summary>
    public static RealizationPolicy Default { get; } = new();
}
