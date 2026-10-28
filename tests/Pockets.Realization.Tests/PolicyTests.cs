namespace Pockets.Realization.Tests;

/// <summary>The default realization policy (P2-D5).</summary>
public class PolicyTests
{
    /// <summary>Defaults match the plan.</summary>
    [Fact]
    public void Default_Values()
    {
        var p = RealizationPolicy.Default;
        Assert.Equal(125, p.WalkMs);
        Assert.Equal(120, p.BumpMs);
        Assert.Equal(150, p.ShakeMs);
        Assert.Equal(125, p.SlideMs);
        Assert.Equal(200, p.SortMs);
        Assert.Equal(160, p.PopMs);
        Assert.Equal(50, p.ReactionOverlapPercent);
        Assert.Equal(EaseKind.Smooth, p.Ease);
    }

    /// <summary>The policy is a record: with-expressions copy and override.</summary>
    [Fact]
    public void With_Overrides()
    {
        var p = RealizationPolicy.Default with { WalkMs = 1, Ease = EaseKind.Linear };
        Assert.Equal(1, p.WalkMs);
        Assert.Equal(EaseKind.Linear, p.Ease);
        Assert.Equal(125, RealizationPolicy.Default.WalkMs);
    }
}
