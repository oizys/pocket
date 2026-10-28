using Pockets.Spikes.Belts;

namespace Pockets.Spikes.Tests;

/// <summary>Spike A tests: naive (Rule-184) vs chain belt stepping. Trace strings double as documentation.</summary>
public class BeltTests
{
    private static string Trace(BeltState s, int steps, Func<BeltState, StepResult> step) =>
        BeltTrace.Run(s, steps, step);

    private static string Lines(params string[] l) => string.Join("\n", l);

    [Fact]
    public void Parse_And_Render_RoundTrip()
    {
        var s = BeltState.Parse("⇒▶▶▼K/..▲◀.", "A.B../...C.");
        Assert.Equal("⇒▶B▼K|..▲C.", s.Render());
    }

    [Fact]
    public void Naive_FullBelt_StartupRipple_Is_Rule184()
    {
        var s = BeltState.Parse("▶▶▶▶▶K", "AAAAA.");
        var expected = Lines(
            "t00 AAAAAK out=0",
            "t01 AAAA▶K out=1",
            "t02 AAA▶AK out=1",
            "t03 AA▶A▶K out=2",
            "t04 A▶A▶AK out=2",
            "t05 ▶A▶A▶K out=3",
            "t06 ▶▶A▶AK out=3",
            "t07 ▶▶▶A▶K out=4",
            "t08 ▶▶▶▶AK out=4",
            "t09 ▶▶▶▶▶K out=5");
        Assert.Equal(expected, Trace(s, 9, BeltSim.NaiveStep));
    }

    [Fact]
    public void Chain_FullBelt_MovesAsAUnit()
    {
        var s = BeltState.Parse("▶▶▶▶▶K", "AAAAA.");
        var expected = Lines(
            "t00 AAAAAK out=0",
            "t01 ▶AAAAK out=1",
            "t02 ▶▶AAAK out=2",
            "t03 ▶▶▶AAK out=3",
            "t04 ▶▶▶▶AK out=4",
            "t05 ▶▶▶▶▶K out=5");
        Assert.Equal(expected, Trace(s, 5, BeltSim.ChainStep));
    }

    private static (int total, int lastHalf) Throughput(Func<BeltState, StepResult> step, int steps)
    {
        var s = BeltState.Parse("⇒▶▶▶▶K", "A.....");
        int total = 0, last = 0;
        for (int t = 0; t < steps; t++)
        {
            var r = step(s);
            s = r.State;
            int c = r.Events.OfType<Consumed>().Count();
            total += c;
            if (t >= steps / 2) last += c;
        }
        return (total, last);
    }

    [Fact]
    public void Naive_SaturatedSource_Throughput_Is_Half()
    {
        var (total, last) = Throughput(BeltSim.NaiveStep, 100);
        Assert.Equal(25, last);   // exactly 1/2 per step over the last 50 steps
        Assert.InRange(total, 48, 50);
    }

    [Fact]
    public void Chain_SaturatedSource_Throughput_Is_One_And_No_Gaps()
    {
        var (total, last) = Throughput(BeltSim.ChainStep, 100);
        Assert.Equal(50, last);
        Assert.Equal(96, total); // 4 belt cells of latency
        var s = BeltState.Parse("⇒▶▶▶▶K", "A.....");
        for (int t = 0; t < 30; t++)
        {
            s = BeltSim.ChainStep(s).State;
            if (t >= 4) Assert.Equal("⇒AAAAK", s.Render());
        }
    }

    [Fact]
    public void Chain_SaturatedSource_Trace()
    {
        var s = BeltState.Parse("⇒▶▶▶▶K", "A.....");
        var expected = Lines(
            "t00 ⇒▶▶▶▶K out=0",
            "t01 ⇒A▶▶▶K out=0",
            "t02 ⇒AA▶▶K out=0",
            "t03 ⇒AAA▶K out=0",
            "t04 ⇒AAAAK out=0",
            "t05 ⇒AAAAK out=1",
            "t06 ⇒AAAAK out=2");
        Assert.Equal(expected, Trace(s, 6, BeltSim.ChainStep));
    }

    [Fact]
    public void Merge_SidePriority_SaturatedMain_Starves_Side()
    {
        // merge cell (1,1) flows east: A from the west is straight-through (main), B from the north is a side input.
        var s = BeltState.Parse(".⇓./⇒▶K", ".B./A..");
        var expected = Lines(
            "t00 .⇓.|⇒▶K out=0",
            "t01 .⇓.|⇒AK out=0",
            "t02 .⇓.|⇒AK out=1",
            "t03 .⇓.|⇒AK out=2",
            "t04 .⇓.|⇒AK out=3");
        Assert.Equal(expected, Trace(s, 4, BeltSim.ChainStep));
    }

    [Fact]
    public void Merge_SidePriority_Over_2N_Main_Gets_All_Side_Starves_And_Repeatable()
    {
        const int N = 50;
        int Count(char who)
        {
            var s = BeltState.Parse(".⇓./⇒▶K", ".B./A..");
            int c = 0;
            for (int t = 0; t < 2 * N; t++)
            {
                var r = BeltSim.ChainStep(s);
                s = r.State;
                c += r.Events.OfType<Emitted>().Count(e => e.Item == who);
            }
            return c;
        }
        Assert.Equal(2 * N, Count('A'));
        Assert.Equal(0, Count('B'));
        Assert.Equal(Count('A'), Count('A'));
    }

    [Fact]
    public void Merge_SidePriority_Side_Enters_Only_When_Main_Leaves_Room()
    {
        // main source A emits every 2nd step; the side input B fills exactly the gaps.
        var specs = new List<CellSpec>
        {
            new(1, 0, CellKind.Source, 2, 'B', 1),
            new(0, 1, CellKind.Source, 1, 'A', 2),
            new(1, 1, CellKind.Belt, 1, '\0', 1),
            new(2, 1, CellKind.Sink, 0, '\0', 1),
        };
        var expected = Lines(
            "t00 .⇓.|⇒▶K out=0",
            "t01 .⇓.|⇒AK out=0",
            "t02 .⇓.|⇒BK out=1",
            "t03 .⇓.|⇒AK out=2",
            "t04 .⇓.|⇒BK out=3");
        Assert.Equal(expected, Trace(BeltState.FromSpecs(3, 2, specs), 4, BeltSim.ChainStep));
    }

    [Fact]
    public void Merge_ThreeInputs_SidePriority_Straight_Then_Left_Then_Right()
    {
        // east-flowing merge: W = straight (period 3), N = left side, S = right side. Left beats right in the gaps.
        var specs = new List<CellSpec>
        {
            new(1, 0, CellKind.Source, 2, 'B', 1),
            new(0, 1, CellKind.Source, 1, 'A', 3),
            new(1, 1, CellKind.Belt, 1, '\0', 1),
            new(2, 1, CellKind.Sink, 0, '\0', 1),
            new(1, 2, CellKind.Source, 0, 'C', 1),
        };
        var expected = Lines(
            "t00 .⇓.|⇒▶K|.⇑. out=0",
            "t01 .⇓.|⇒AK|.⇑. out=0",
            "t02 .⇓.|⇒BK|.⇑. out=1",
            "t03 .⇓.|⇒BK|.⇑. out=2",
            "t04 .⇓.|⇒AK|.⇑. out=3",
            "t05 .⇓.|⇒BK|.⇑. out=4",
            "t06 .⇓.|⇒BK|.⇑. out=5");
        var trace = Trace(BeltState.FromSpecs(3, 3, specs), 6, BeltSim.ChainStep);
        Assert.Equal(expected, trace);
        Assert.DoesNotContain('C', trace.Replace("⇑", ""));
    }

    [Fact]
    public void Merge_Result_Independent_Of_Cell_Listing_Order()
    {
        var specs = new List<CellSpec>
        {
            new(1, 0, CellKind.Source, 2, 'B', 1),
            new(0, 1, CellKind.Source, 1, 'A', 1),
            new(1, 1, CellKind.Belt, 1, '\0', 1),
            new(2, 1, CellKind.Sink, 0, '\0', 1),
            new(1, 2, CellKind.Source, 0, 'C', 1),
        };
        var reference = BeltTrace.Run(BeltState.FromSpecs(3, 3, specs), 12, BeltSim.ChainStep);
        var rng = new Random(1234);
        for (int i = 0; i < 10; i++)
        {
            var shuffled = specs.OrderBy(_ => rng.Next()).ToList();
            Assert.Equal(reference, BeltTrace.Run(BeltState.FromSpecs(3, 3, shuffled), 12, BeltSim.ChainStep));
        }
    }

    [Fact]
    public void Merge_Loser_Chain_Stays_Put_NoCompression()
    {
        // north chain of 3 B's (side input) and one A from the west (straight-through) contest the merge cell (1,3).
        var s = BeltState.Parse(".▼./.▼./.▼./▶▶K", ".B./.B./.B./A..");
        var expected = Lines(
            "t00 .B.|.B.|.B.|A▶K out=0",
            "t01 .B.|.B.|.B.|▶AK out=0",
            "t02 .▼.|.B.|.B.|▶BK out=1",
            "t03 .▼.|.▼.|.B.|▶BK out=2",
            "t04 .▼.|.▼.|.▼.|▶BK out=3");
        var trace = Trace(s, 4, BeltSim.ChainStep);
        Assert.Equal(expected, trace);
    }

    [Fact]
    public void Merge_Loser_Gets_LostMerge_Event()
    {
        var s = BeltState.Parse(".▼./▶▶K", ".B./A..");
        var r = BeltSim.ChainStep(s);              // A (straight-through) wins; B (side) loses
        Assert.Contains(r.Events, e => e is Blocked { Reason: "lost-merge" } b && b.From == new Pos(1, 0));
        Assert.Contains(r.Events, e => e is Moved { Cause: "belt" } m && m.Item == 'A');
    }

    [Fact]
    public void Ring_WithOneGap_Rotates()
    {
        var s = BeltState.Parse("▶▼/▲◀", "AB/C.");
        var expected = Lines(
            "t00 AB|C◀ out=0",
            "t01 CA|▲B out=0",
            "t02 ▶C|BA out=0",
            "t03 B▼|AC out=0");
        Assert.Equal(expected, Trace(s, 3, BeltSim.ChainStep));
    }

    [Fact]
    public void Ring_Saturated_Stalls_ByDesign()
    {
        // Choice: a full cycle has no empty cell to start the chain => on-stack hit => "not vacating".
        var s = BeltState.Parse("▶▼/▲◀", "AB/DC");
        var r = BeltSim.ChainStep(s);
        Assert.Equal("AB|DC", r.State.Render());
        Assert.Equal(4, r.Events.OfType<Blocked>().Count());
        Assert.DoesNotContain(r.Events, e => e is Moved);
    }

    [Fact]
    public void Ring_WithOneGap_Naive_Moves_Only_Into_PrevFrame_Gap()
    {
        var s = BeltState.Parse("▶▼/▲◀", "AB/C.");
        var r = BeltSim.NaiveStep(s);
        Assert.Equal("A▼|CB", r.State.Render());
        Assert.Single(r.Events.OfType<Moved>());
    }

    [Fact]
    public void BlockedHead_NoSink_Chain_Stays_Compressed_And_Conserves()
    {
        var s = BeltState.Parse("▶▶▶▶", "AAAA");
        var r = BeltSim.ChainStep(s);
        Assert.Equal("AAAA", r.State.Render());
        Assert.Equal(4, r.Events.OfType<Blocked>().Count());
        Assert.True(BeltSim.Conserved(s, r.State, r.Events));
    }

    [Fact]
    public void BlockedHead_SlowSink_Trace_And_Conservation()
    {
        var s = BeltState.Parse("▶▶▶K", "AAA.").WithPeriod(3, 0, 4).WithTick(1);
        var expected = Lines(
            "t01 AAAK out=0",
            "t02 AAAK out=0",
            "t03 AAAK out=0",
            "t04 AAAK out=0",
            "t05 ▶AAK out=1");
        var tr = BeltTrace.Run(s, 4, BeltSim.ChainStep, startLabel: s.Tick);
        Assert.Equal(expected, tr);
        var cur = s;
        for (int i = 0; i < 12; i++)
        {
            var r = BeltSim.ChainStep(cur);
            Assert.True(BeltSim.Conserved(cur, r.State, r.Events));
            cur = r.State;
        }
    }

    [Fact]
    public void Conservation_Holds_Over_Random_Soup()
    {
        
        var s = BeltState.Parse("⇒▶▶▼K/.▲.▼./⇓◀◀◀.", "A.B../.C.D./E....");
        foreach (var step in new Func<BeltState, StepResult>[] { BeltSim.NaiveStep, BeltSim.ChainStep })
        {
            var cur = s;
            for (int t = 0; t < 200; t++)
            {
                var r = step(cur);
                Assert.True(BeltSim.Conserved(cur, r.State, r.Events), $"t={t}");
                cur = r.State;
            }
        }

    }
}
