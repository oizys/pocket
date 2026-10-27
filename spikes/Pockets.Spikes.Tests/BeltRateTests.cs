using Pockets.Spikes.Plaque;

namespace Pockets.Spikes.Tests;

/// <summary>Spike C belt part: straight belt L=12, FULL vs rate 1/k vs naive jump vs event-driven closed form.</summary>
public class BeltRateTests
{
    public static IEnumerable<object[]> Configs()
    {
        foreach (var (s, c) in new[] { (3, 1), (2, 2), (1, 3), (1, 5), (4, 3), (1, 1) })
            foreach (var n in new[] { 1, 13, 100, 240, 241, 2400 })
                yield return new object[] { s, c, n };
    }

    [Theory, MemberData(nameof(Configs))]
    public void CatchUp_ClosedForm_Matches_Full(int s, int c, int n)
    {
        var cfg = new LineConfig(12, s, c);
        var full = LineModels.Full(cfg, n);
        var cu = LineModels.CatchUp(cfg, n);
        var rep = LineModels.Compare("cu", full, cu);
        Assert.True(rep.Exact, $"{full} vs {cu}");
    }

    [Theory]
    [InlineData(3, 1, 2)] [InlineData(3, 1, 4)] [InlineData(3, 1, 8)]
    [InlineData(1, 3, 2)] [InlineData(1, 3, 4)] [InlineData(1, 3, 8)]
    public void SubStepped_Matches_Full(int s, int c, int k)
    {
        var cfg = new LineConfig(12, s, c);
        foreach (var n in new[] { 240, 241 })
            Assert.True(LineModels.Compare("sub", LineModels.Full(cfg, n), LineModels.SubStepped(cfg, n, k, flush: true)).Exact);
    }

    [Fact]
    public void SubStepped_Unflushed_Equals_Full_At_Last_Multiple()
    {
        var cfg = new LineConfig(12, 1, 3);
        Assert.True(LineModels.Compare("x", LineModels.Full(cfg, 240), LineModels.SubStepped(cfg, 241, 8, flush: false)).Exact);
    }

    [Theory]
    [InlineData(1, 3, 4)] [InlineData(1, 3, 8)]
    [InlineData(1, 5, 4)] [InlineData(1, 5, 8)]
    // NB: k=2 with (s=1,c=3) happens to coincide with FULL at N=240 (not at 241); see table.
    public void NaiveJump_Diverges_When_Backlogged(int s, int c, int k)
    {
        var cfg = new LineConfig(12, s, c);
        var rep = LineModels.Compare("jump", LineModels.Full(cfg, 240), LineModels.NaiveJump(cfg, 240, k, flush: true));
        Assert.False(rep.Exact, rep.ToString());
    }

    [Fact]
    public void Backlogged_Belt_Compresses_And_Sink_Rate_Is_One_Per_c()
    {
        var cfg = new LineConfig(12, 1, 3);
        var f = LineModels.Full(cfg, 240);
        Assert.Equal("############", f.Occupancy);
        Assert.Equal(76, f.Consumed); // sink ticks 12,15,..,237 (first item reaches the head at tau=12)
    }

    [Fact]
    public void Underloaded_Belt_Is_Sparse()
    {
        var f = LineModels.Full(new LineConfig(12, 3, 1), 240);
        Assert.DoesNotContain("##", f.Occupancy);
    }
}
