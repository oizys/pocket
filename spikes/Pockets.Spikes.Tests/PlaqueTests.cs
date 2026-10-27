using Pockets.Spikes.Plaque;

namespace Pockets.Spikes.Tests;

/// <summary>Spike C tests (plaque fields): rates 1/k and offline catch-up for P1 (accumulator) and P2 (spread).</summary>
public class PlaqueTests
{
    private static readonly PlaqueParams Pr = PlaqueParams.Corner();

    public static IEnumerable<object[]> Ns => new[] { 1, 7, 30, 240, 241, 2400 }.Select(n => new object[] { n });

    [Theory, MemberData(nameof(Ns))]
    public void P1_CatchUp_ClosedForm_Matches_Full(int n)
    {
        foreach (var seed in new[] { 1, 2, 3 })
        {
            var p0 = PlaqueParams.RandomInitial(Pr, seed);
            var full = PlaqueSim.Full(PlaqueRule.Accumulator, Pr, p0, n);
            var cu = PlaqueSim.CatchUp(PlaqueRule.Accumulator, Pr, p0, n);
            var rep = PlaqueSim.Compare("p1-catchup", full, cu, n);
            Assert.True(rep.Exact, rep.ToString());
            Assert.Equal(0, rep.CrossDiffCells);
            Assert.Equal(1, cu.Cost);
        }
    }

    [Theory, MemberData(nameof(Ns))]
    public void P2_CatchUp_ThresholdJumps_Match_Full(int n)
    {
        foreach (var seed in new[] { 1, 2, 3, 4, 5 })
        {
            var p0 = PlaqueParams.RandomInitial(Pr, seed);
            var full = PlaqueSim.Full(PlaqueRule.Spread, Pr, p0, n);
            var cu = PlaqueSim.CatchUp(PlaqueRule.Spread, Pr, p0, n);
            var rep = PlaqueSim.Compare("p2-catchup", full, cu, n);
            Assert.True(rep.Exact, rep.ToString());
            Assert.Equal(0, rep.CrossDiffCells);
            Assert.True(cu.Cost <= n);
        }
    }

    [Fact]
    public void P2_CatchUp_From_Zero_Grid_Is_Far_Cheaper_Than_Stepping()
    {
        var p0 = new int[Pr.W * Pr.H];
        var full = PlaqueSim.Full(PlaqueRule.Spread, Pr, p0, 2400);
        var cu = PlaqueSim.CatchUp(PlaqueRule.Spread, Pr, p0, 2400);
        Assert.True(PlaqueSim.Compare("x", full, cu, 2400).Exact);
        Assert.True(cu.Cost < 2400 / 10, $"jumps={cu.Cost}");
    }

    [Fact]
    public void P2_Is_Monotone_NonDecreasing()
    {
        var p0 = PlaqueParams.RandomInitial(Pr, 9);
        var a = PlaqueSim.Full(PlaqueRule.Spread, Pr, p0, 10).P;
        var b = PlaqueSim.Full(PlaqueRule.Spread, Pr, p0, 11).P;
        Assert.All(a.Zip(b), z => Assert.True(z.Second >= z.First));
    }

    [Theory]
    [InlineData(2, 240)] [InlineData(4, 240)] [InlineData(8, 240)]
    [InlineData(2, 241)] [InlineData(4, 241)] [InlineData(8, 241)]
    public void P1_BigStep_Is_Exact_But_Crossing_Times_Are_Quantised(int k, int n)
    {
        var p0 = PlaqueParams.RandomInitial(Pr, 3);
        var full = PlaqueSim.Full(PlaqueRule.Accumulator, Pr, p0, n);
        var big = PlaqueSim.BigStep(PlaqueRule.Accumulator, Pr, p0, n, k, flush: true);
        var rep = PlaqueSim.Compare("p1-big", full, big, n);
        Assert.True(rep.Exact, rep.ToString());          // no neighbour coupling => k*r is exact
        Assert.True(rep.CrossDiffCells > 0);              // but observed crossing steps round up to the batch edge
        Assert.True(rep.MaxCrossDiff < k);
    }

    [Theory]
    [InlineData(2)] [InlineData(4)] [InlineData(8)]
    public void P2_BigStep_Diverges(int k)
    {
        var p0 = new int[Pr.W * Pr.H];
        var full = PlaqueSim.Full(PlaqueRule.Spread, Pr, p0, 240);
        var big = PlaqueSim.BigStep(PlaqueRule.Spread, Pr, p0, 240, k, flush: true);
        var rep = PlaqueSim.Compare("p2-big", full, big, 240);
        Assert.False(rep.Exact);
        Assert.True(rep.DiffCells > 0);
        Assert.True(rep.MaxAbsDiff > 0);
        Assert.True(full.P.Zip(big.P).All(z => z.Second <= z.First)); // frozen flags only ever under-count
    }

    [Theory]
    [InlineData(2, 240)] [InlineData(4, 240)] [InlineData(8, 240)]
    [InlineData(2, 241)] [InlineData(4, 241)] [InlineData(8, 241)]
    [InlineData(8, 2400)]
    public void SubStepped_Matches_Full_With_Flush_For_Both_Rules(int k, int n)
    {
        var p0 = PlaqueParams.RandomInitial(Pr, 4);
        foreach (var rule in new[] { PlaqueRule.Accumulator, PlaqueRule.Spread })
        {
            var full = PlaqueSim.Full(rule, Pr, p0, n);
            var sub = PlaqueSim.SubStepped(rule, Pr, p0, n, k, flush: true);
            Assert.True(PlaqueSim.Compare("sub", full, sub, n).Exact);
            Assert.True(PlaqueSim.Compare("sub", full, sub, n).CrossDiffCells == 0);
        }
    }

    [Fact]
    public void SubStepped_WithoutFlush_Lags_When_N_Not_Multiple_Of_K()
    {
        var p0 = new int[Pr.W * Pr.H];
        int k = 8, n = 241;
        var noflush = PlaqueSim.SubStepped(PlaqueRule.Spread, Pr, p0, n, k, flush: false);
        Assert.Equal(PlaqueSim.Full(PlaqueRule.Spread, Pr, p0, 240).P, noflush.P);   // behind by n % k = 1 tick
        Assert.False(PlaqueSim.Compare("x", PlaqueSim.Full(PlaqueRule.Spread, Pr, p0, n), noflush, n).Exact);
    }

    [Fact]
    public void Stateful_Rng_Diverges_Under_Batching_Counter_Rng_Does_Not_And_Neither_Skips()
    {
        const int walkers = 8, steps = 64, k = 8;
        var stateful = Lint.StatefulWalk(42, walkers, steps, batch: 1);
        var statefulBatched = Lint.StatefulWalk(42, walkers, steps, batch: k);
        Assert.NotEqual(stateful, statefulBatched);
        var counter = Lint.CounterWalk(42, walkers, steps, batch: 1);
        var counterBatched = Lint.CounterWalk(42, walkers, steps, batch: k);
        Assert.Equal(counter, counterBatched);
        // Lint verdict: a hash-keyed walk is order independent but still has no closed form: MustSimulate (observed-only).
    }
}
