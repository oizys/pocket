namespace Pockets.Realization;

/// <summary>Which easing curve a track uses.</summary>
public enum EaseKind
{
    /// <summary>Constant speed.</summary>
    Linear = 1,
    /// <summary>Integer smoothstep: slow in, slow out.</summary>
    Smooth = 2
}
