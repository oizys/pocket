using Pockets.Spikes.Belts;

namespace Pockets.Spikes.Tests;

/// <summary>Catch-up measurements for belt merge networks (N1/N2/N3) under fixed side priority.</summary>
public class MergeCatchUpTests
{
    private static readonly int[] Ns = { 240, 241, 2400, 24000 };

    private static IEnumerable<(MergeNetwork net, FeedConfig feed)> All() =>
        MergeNetworks.All.SelectMany(n => MergeFeeds.For(n).Select(f => (n, f)));

    [Fact]
    public void Networks_Have_Expected_Sources_And_PathLengths()
    {
        var n1 = MergeNetworks.N1;
        Assert.Equal("ABx", string.Concat(n1.Sources.Select(s => s.Letter)) + "x");
        Assert.Equal(15, n1.Sources[0].PathLen);
        Assert.Equal(new[] { 'A', 'B', 'C' }, MergeNetworks.N2.Sources.Select(s => s.Letter));
        Assert.Equal(new[] { 'A', 'B', 'D' }, MergeNetworks.N3.Sources.Select(s => s.Letter));
        Assert.Equal(22, MergeNetworks.N3.Sources[0].PathLen + 0);
    }

    [Fact]
    public void Unsaturated_Delivers_Everything_Eventually()
    {
        var net = MergeNetworks.N2; var feed = MergeFeeds.For(net).First(f => f.Name == "under");
        var r = MergeCatchUp.Full(net, feed, 2400);
        Assert.True(r.Consumed[0] >= 2400 / 3 - 20);
        Assert.True(r.Consumed[1] >= 2400 / 4 - 20);
    }

    [Fact]
    public void SubStep_Is_Exact_Everywhere()
    {
        foreach (var (net, feed) in All())
        {
            var full = MergeCatchUp.FullAt(net, feed, Ns);
            foreach (var n in Ns.Where(n => n <= 2400))
                foreach (var k in new[] { 2, 4, 8 })
                {
                    var row = MergeCatchUp.Compare(net, "substep", k, n, full[n], MergeCatchUp.SubStep(net, feed, n, k));
                    Assert.True(row.Exact, $"{net.Name} {feed.Name} n={n} k={k}");
                    Assert.Equal(0, row.StepsLate);
                }
        }
    }

    [Fact]
    public void CycleSkip_Is_Exact_Everywhere_And_Cost_Is_Flat()
    {
        foreach (var (net, feed) in All())
        {
            var full = MergeCatchUp.FullAt(net, feed, Ns);
            foreach (var n in Ns)
            {
                var cs = MergeCatchUp.CycleSkip(net, feed, n);
                var row = MergeCatchUp.Compare(net, "cycleskip", 0, n, full[n], cs);
                Assert.True(row.Exact, $"{net.Name} {feed.Name} n={n}");
                Assert.Equal(0, row.StepsLate);
            }
            var a = MergeCatchUp.CycleSkip(net, feed, 2400); var b = MergeCatchUp.CycleSkip(net, feed, 24000);
            Assert.True(a.Cycle > 0 && b.Cycle == a.Cycle && b.Transient == a.Transient, $"{net.Name} {feed.Name}");
            Assert.True(Math.Abs(b.Cost - a.Cost) < a.Cycle, $"{net.Name} {feed.Name} cost {a.Cost} vs {b.Cost} C={a.Cycle}");
            Assert.True(b.Cost < 2400, $"{net.Name} {feed.Name} cost {b.Cost}");
        }
    }

    [Fact]
    public void CycleSkip_Seeded_Sweep_Is_Exact()
    {
        var s = MergeCatchUp.DefaultSweep.Value;
        Assert.Equal(600, s.Runs);
        Assert.Equal(0, s.Mismatches);
        Assert.True(s.MaxCycle > 0 && s.MaxTransient >= 0 && s.MaxCost > 0);
    }

    [Fact]
    public void BigStep_Diverges_In_Named_Configs()
    {
        foreach (var name in new[] { "under", "mainsat", "backlog", "coprime" })
            foreach (var net in MergeNetworks.All)
            {
                var feed = MergeFeeds.For(net).First(f => f.Name == name);
                var full = MergeCatchUp.FullAt(net, feed, new[] { 240 })[240];
                foreach (var k in new[] { 2, 4, 8 })
                    Assert.False(MergeCatchUp.Compare(net, "bigstep", k, 240, full, MergeCatchUp.BigStep(net, feed, 240, k)).Exact,
                        $"{net.Name} {name} k={k}");
            }
    }

    [Fact]
    public void MainOnly_Holds_When_Unsaturated_And_Fails_When_Backlogged()
    {
        foreach (var net in MergeNetworks.All)
        {
            var under = MergeFeeds.For(net).First(f => f.Name == "under");
            var full = MergeCatchUp.FullAt(net, under, new[] { 2400 })[2400];
            Assert.True(MergeCatch(net, under, 2400, full).Exact, $"{net.Name} under");
        }
        var failed = MergeNetworks.All.SelectMany(net => MergeFeeds.For(net).Where(f => f.Name.StartsWith("backlog"))
            .Select(f => MergeCatch(net, f, 2400, MergeCatchUp.FullAt(net, f, new[] { 2400 })[2400]))).Count(r => !r.Exact);
        Assert.True(failed >= 1);
    }

    private static MergeRow MergeCatch(MergeNetwork net, FeedConfig f, int n, MergeRun full) =>
        MergeCatchUp.Compare(net, "mainonly", 0, n, full, MergeCatchUp.MainOnly(net, f, n));

    [Fact]
    public void Fluid_Is_O1_And_Rounds_Down()
    {
        var net = MergeNetworks.N1; var feed = MergeFeeds.For(net).First(f => f.Name == "under");
        var r = MergeCatchUp.Fluid(net, feed, 2400);
        Assert.Equal(1, r.Cost);
        Assert.Equal((2400 - 15) / 3, r.Consumed[0]);
    }
}
