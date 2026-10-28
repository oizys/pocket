using System.Text;

namespace Pockets.Spikes.Belts;

/// <summary>One input of a merge network: its letter, cell, merge priority rank (0 = highest) and path length to the sink in steps.</summary>
public sealed record MergeSource(char Letter, int X, int Y, int Rank, int PathLen);

/// <summary>Periods for a feed scenario: one per source (in network order) and the sink period.</summary>
public sealed record FeedConfig(string Name, int[] SourcePeriods, int SinkPeriod);

/// <summary>A merge network: static cell specs (periods filled in by <see cref="Build"/>), its sources and sink.</summary>
public sealed record MergeNetwork(string Name, int W, int H, IReadOnlyList<CellSpec> Cells, IReadOnlyList<MergeSource> Sources, Pos Sink)
{
    /// <summary>Build the initial (empty) belt state with the periods of <paramref name="f"/>.</summary>
    public BeltState Build(FeedConfig f) => BeltState.FromSpecs(W, H, Cells.Select(c =>
    {
        int si = Sources.ToList().FindIndex(s => s.X == c.X && s.Y == c.Y);
        if (c.Kind == CellKind.Source) return c with { Period = f.SourcePeriods[si] };
        if (c.Kind == CellKind.Sink) return c with { Period = f.SinkPeriod };
        return c;
    }));

    /// <summary>Index of the source emitting <paramref name="letter"/>.</summary>
    public int IndexOf(char letter) => Sources.ToList().FindIndex(s => s.Letter == letter);
}

/// <summary>
/// The three networks. All main lines flow East along row 4; side inputs enter the merge cell from the north (LEFT of
/// east-flowing, priority 2) or south (RIGHT, priority 3); the main (from behind) has priority 1.
/// <code>
/// N1 (16x5)           N2 (16x9)           N3 (23x5)
///  . . . . . . . . B   B = (8,0) src S     B (8,0)         D (15,0)
///        . . . . . . ▼ ... 3 belts          ▼▼▼             ▼▼▼
///  A▶▶▶▶▶▶▶M▶▶▶▶▶▶K    A▶▶▶▶▶▶▶M▶▶▶▶▶▶K    A▶▶▶▶▶▶▶M▶▶▶▶▶▶M2▶▶▶▶▶▶K
///  A=(0,4) M=(8,4)      C (8,8) src N, 3 belts ▲▲▲ into M from the south
/// </code>
/// Source letters: A main, B left side, C right side (N2), D second left side at M2 (N3).
/// </summary>
public static class MergeNetworks
{
    private static MergeNetwork Make(string name, bool right, bool cascade)
    {
        const int my = 4;
        int sinkX = cascade ? 22 : 15, h = right ? 9 : 5;
        var cells = new List<CellSpec> { new(0, my, CellKind.Source, 1, 'A', 1) };
        cells.AddRange(Enumerable.Range(1, sinkX - 1).Select(x => new CellSpec(x, my, CellKind.Belt, 1, '\0', 1)));
        cells.Add(new CellSpec(sinkX, my, CellKind.Sink, 0, '\0', 1));
        void Side(int x, int y0, int dir, char letter, int dy)
        {
            cells.Add(new CellSpec(x, y0, CellKind.Source, dir, letter, 1));
            cells.AddRange(Enumerable.Range(1, 3).Select(i => new CellSpec(x, y0 + dy * i, CellKind.Belt, dir, '\0', 1)));
        }
        var srcs = new List<(char, int, int)> { ('A', 0, my) };
        Side(8, 0, 2, 'B', 1); srcs.Add(('B', 8, 0));
        if (right) { Side(8, 8, 0, 'C', -1); srcs.Add(('C', 8, 8)); }
        if (cascade) { Side(15, 0, 2, 'D', 1); srcs.Add(('D', 15, 0)); }
        var tmp = BeltState.FromSpecs(sinkX + 1, h, cells);
        int Path((char, int, int) s)
        {
            int x = s.Item2, y = s.Item3, n = 0;
            while (true)
            {
                int d = tmp.Dir[y * tmp.W + x]; x += Dirs.Dx[d]; y += Dirs.Dy[d]; n++;
                if (tmp.Kind[y * tmp.W + x] == CellKind.Sink) return n;
            }
        }
        var sources = srcs.Select((s, i) => new MergeSource(s.Item1, s.Item2, s.Item3, i, Path(s))).ToList();
        return new MergeNetwork(name, sinkX + 1, h, cells, sources, new Pos(sinkX, my));
    }

    /// <summary>Main + one left side input.</summary>
    public static readonly MergeNetwork N1 = Make("N1", false, false);
    /// <summary>Main + left + right side inputs at the same merge cell.</summary>
    public static readonly MergeNetwork N2 = Make("N2", true, false);
    /// <summary>Cascade: N1's output is the main input of a second merge that also takes side input D.</summary>
    public static readonly MergeNetwork N3 = Make("N3", false, true);
    /// <summary>All three networks.</summary>
    public static readonly MergeNetwork[] All = { N1, N2, N3 };
}

/// <summary>The feed configurations tried on every network (periods are by source order A,B,C|D).</summary>
public static class MergeFeeds
{
    /// <summary>under: sparse sources, sink 1. mainsat: main period 1, sink 1. backlog: sink 3, sources 2/2/2.
    /// backlog2: sink 2, main 3 and sides 2 (sides over-supply, main would not backlog alone). coprime: main 3, side 2, extra side 5, sink 1.</summary>
    public static IReadOnlyList<FeedConfig> For(MergeNetwork n)
    {
        int k = n.Sources.Count;
        FeedConfig F(string name, int sink, params int[] p) => new(name, p.Take(k).ToArray(), sink);
        return new[]
        {
            F("under", 1, 3, 4, 5), F("mainsat", 1, 1, 2, 3), F("backlog", 3, 2, 2, 2),
            F("backlog2", 2, 3, 2, 2), F("coprime", 1, 3, 2, 5),
        };
    }
}

/// <summary>Observable result of running a method: item layer (if any), tick, per-source counters, consumption timings, cost.</summary>
public sealed record MergeRun(char[]? Items, int Tick, int[] Consumed, int[]? Emitted, List<int>[]? ConsumeSteps,
    int Cost, int Transient = -1, int Cycle = 0);

/// <summary>One report row comparing a method against FULL. -1 in OccDiff / StepsLate means "not available" ('-').</summary>
public sealed record MergeRow(string Mode, int K, int N, bool Exact, int OccDiff, int MaxDelivDiff, string Deliv,
    int StepsLate, int Cost, int Transient, int Cycle);

/// <summary>Sweep outcome of CYCLESKIP over random period combinations.</summary>
public sealed record SweepSummary(int Runs, int Mismatches, int MaxTransient, int MaxCycle, int MaxCost, int MaxUnboundedTransient, int MaxUnboundedCycle);

/// <summary>
/// Catch-up methods for merge networks. Time semantics are those of <see cref="BeltSim.ChainStep"/>: step t runs with Tick = t.
/// FULL = N ChainSteps. SUBSTEP = the same N ChainSteps issued in batches of k (exact). BIGSTEP = coarse model (see
/// <see cref="BigStep"/>). CYCLESKIP = exact: state key (item layer, Tick mod P), P = lcm of all source/sink periods.
/// MAINONLY = FULL with side sources deleted (tests the "main keeps linear catch-up" claim). FLUID = O(1) rational estimate.
/// </summary>
public static class MergeCatchUp
{
    private static BeltState Tick0(MergeNetwork net, FeedConfig f) => net.Build(f);

    private static (int[] cons, int[] em, List<int>[] steps) Fresh(int k) =>
        (new int[k], new int[k], Enumerable.Range(0, k).Select(_ => new List<int>()).ToArray());

    private static BeltState Advance(MergeNetwork net, BeltState s, int[] cons, int[] em, List<int>[] steps, int stepIndex)
    {
        var r = BeltSim.ChainStep(s);
        foreach (var e in r.Events)
            if (e is Consumed c) { int i = net.IndexOf(c.Item); cons[i]++; steps[i].Add(stepIndex); }
            else if (e is Emitted m) em[net.IndexOf(m.Item)]++;
        return r.State;
    }

    /// <summary>Reference: n plain ChainSteps.</summary>
    public static MergeRun Full(MergeNetwork net, FeedConfig f, int n) => FullAt(net, f, new[] { n })[n];

    /// <summary>Reference trajectory sampled at several n (one simulation). ConsumeSteps hold ALL steps up to max n; use step &lt; n.</summary>
    public static Dictionary<int, MergeRun> FullAt(MergeNetwork net, FeedConfig f, int[] ns)
    {
        var (cons, em, steps) = Fresh(net.Sources.Count);
        var s = Tick0(net, f);
        var res = new Dictionary<int, MergeRun>();
        foreach (var n in ns.OrderBy(x => x))
        {
            while (s.Tick < n) s = Advance(net, s, cons, em, steps, s.Tick);
            res[n] = new MergeRun((char[])s.Item.Clone(), s.Tick, (int[])cons.Clone(), (int[])em.Clone(),
                steps.Select(l => l.ToList()).ToArray(), n);
        }
        return res;
    }

    /// <summary>Every k-th outer step run k ChainSteps; the remainder is flushed at n. Identical step sequence to FULL.</summary>
    public static MergeRun SubStep(MergeNetwork net, FeedConfig f, int n, int k)
    {
        var (cons, em, steps) = Fresh(net.Sources.Count);
        var s = Tick0(net, f);
        int cost = 0;
        for (int o = 1; o <= n; o++)
            if (o % k == 0) for (int j = 0; j < k; j++) { s = Advance(net, s, cons, em, steps, s.Tick); cost++; }
        for (int j = 0; j < n % k; j++) { s = Advance(net, s, cons, em, steps, s.Tick); cost++; }
        return new MergeRun(s.Item, s.Tick, cons, em, steps, cost);
    }

    /// <summary>
    /// Coarse model: ceil(n/k) ChainSteps; coarse step i covers ticks [i*k, i*k+k). A source/sink fires in that coarse step
    /// iff some tick of the window is a multiple of its period (at most once per window); items move ONE cell per coarse step.
    /// Consumption of coarse step i is timed at tick i*k. The state's Tick ends at ceil(n/k)*k.
    /// </summary>
    public static MergeRun BigStep(MergeNetwork net, FeedConfig f, int n, int k)
    {
        var (cons, em, steps) = Fresh(net.Sources.Count);
        var s = Tick0(net, f);
        var orig = (int[])s.Period.Clone();
        int count = (n + k - 1) / k;
        for (int i = 0; i < count; i++)
        {
            int t = i * k;
            var w = s.Clone(1);
            foreach (var c in Enumerable.Range(0, w.Kind.Length).Where(c => w.Kind[c] is CellKind.Source or CellKind.Sink))
                w.Period[c] = (orig[c] - 1 + t) / orig[c] * orig[c] < t + k ? 1 : int.MaxValue;
            var r = BeltSim.ChainStep(w);
            foreach (var e in r.Events)
                if (e is Consumed cc) { int x = net.IndexOf(cc.Item); cons[x]++; steps[x].Add(t); }
                else if (e is Emitted m) em[net.IndexOf(m.Item)]++;
            s = r.State; Array.Copy(orig, s.Period, orig.Length);
        }
        return new MergeRun(s.Item, count * k, cons, em, steps, count);
    }

    private static long Gcd(long a, long b) => b == 0 ? a : Gcd(b, a % b);
    private static int Lcm(IEnumerable<int> xs) => (int)xs.Aggregate(1L, (a, b) => a / Gcd(a, b) * b);

    /// <summary>
    /// Exact catch-up. Steps at full rate; whenever Tick % P == 0 (P = lcm of source/sink periods) the key (item layer, Tick mod P)
    /// is looked up. On the first repeat (t1 -> t2, cycle C = t2 - t1) whole cycles are skipped by adding per-cycle counter
    /// deltas (and re-timing the recorded cycle's consumptions at +j*C) and the clock jumps; the remainder is stepped normally.
    /// Cost = ChainSteps executed. Set <paramref name="n"/> large to measure the true transient/cycle independent of N.
    /// </summary>
    public static MergeRun CycleSkip(MergeNetwork net, FeedConfig f, int n)
    {
        int k = net.Sources.Count;
        var (cons, em, steps) = Fresh(k);
        var s = Tick0(net, f);
        int p = Lcm(f.SourcePeriods.Append(f.SinkPeriod));
        var seen = new Dictionary<string, (int step, int[] cons, int[] em, int[] idx)>();
        int cost = 0, t1 = -1, cyc = 0;
        bool jumped = false;
        while (s.Tick < n)
        {
            if (!jumped && s.Tick % p == 0)
            {
                var key = new string(s.Item) + "|" + (s.Tick % p);
                if (seen.TryGetValue(key, out var prev))
                {
                    t1 = prev.step; cyc = s.Tick - t1;
                    int m = (n - s.Tick) / cyc;
                    for (int i = 0; i < k; i++)
                    {
                        var cycle = steps[i].Skip(prev.idx[i]).ToList();
                        cons[i] += m * (cons[i] - prev.cons[i]);
                        em[i] += m * (em[i] - prev.em[i]);
                        steps[i].AddRange(Enumerable.Range(1, m).SelectMany(j => cycle.Select(x => x + j * cyc)));
                    }
                    s = s.WithTick(s.Tick + m * cyc);
                    jumped = true;
                    continue;
                }
                seen[key] = (s.Tick, (int[])cons.Clone(), (int[])em.Clone(), steps.Select(l => l.Count).ToArray());
            }
            s = Advance(net, s, cons, em, steps, s.Tick); cost++;
        }
        return new MergeRun(s.Item, s.Tick, cons, em, steps, cost, t1, cyc);
    }

    /// <summary>Same network with every non-main (rank &gt; 0) source deleted, run FULL for n steps.</summary>
    public static MergeRun MainOnly(MergeNetwork net, FeedConfig f, int n)
    {
        var (cons, em, steps) = Fresh(net.Sources.Count);
        var s = Tick0(net, f);
        foreach (var src in net.Sources.Where(x => x.Rank > 0)) s.Kind[src.Y * s.W + src.X] = CellKind.None;
        for (int i = 0; i < n; i++) s = Advance(net, s, cons, em, steps, s.Tick);
        return new MergeRun(s.Item, s.Tick, cons, em, steps, n);
    }

    /// <summary>
    /// O(1) steady-state estimate in integer units of 1/P' (P' = lcm of all periods): sink capacity P'/c is handed out in priority
    /// order (rank 0 first); each source gets min(P'/period, remaining); delivered = floor(rate * max(0, n - PathLen) / P').
    /// </summary>
    public static MergeRun Fluid(MergeNetwork net, FeedConfig f, int n)
    {
        long pp = Lcm(f.SourcePeriods.Append(f.SinkPeriod));
        var rem = pp / f.SinkPeriod;
        var cons = new int[net.Sources.Count];
        foreach (var i in Enumerable.Range(0, cons.Length).OrderBy(i => net.Sources[i].Rank))
        {
            long rate = Math.Min(pp / f.SourcePeriods[i], rem); rem -= rate;
            cons[i] = (int)(rate * Math.Max(0, n - net.Sources[i].PathLen) / pp);
        }
        return new MergeRun(null, n, cons, null, null, 1);
    }

    private static int Late(MergeRun full, MergeRun cand, int n, IEnumerable<int> srcs) =>
        srcs.Select(i => full.ConsumeSteps![i].Where(x => x < n).Zip(cand.ConsumeSteps![i], (a, b) => Math.Abs(a - b))
            .DefaultIfEmpty(0).Max()).DefaultIfEmpty(0).Max();

    /// <summary>Compare a method's result with FULL. mode "mainonly" compares only main-source items/deliveries/timings.</summary>
    public static MergeRow Compare(MergeNetwork net, string mode, int k, int n, MergeRun full, MergeRun cand)
    {
        int ns = net.Sources.Count;
        bool main = mode == "mainonly";
        var idx = main ? new[] { 0 } : Enumerable.Range(0, ns).ToArray();
        char a = net.Sources[0].Letter;
        int occ = cand.Items == null ? -1 : full.Items!.Zip(cand.Items, (x, y) => main ? (x == a) != (y == a) : x != y).Count(b => b);
        int dd = idx.Select(i => Math.Abs(full.Consumed[i] - cand.Consumed[i])).Max();
        string deliv = string.Join(" ", idx.Select(i => $"{net.Sources[i].Letter} {full.Consumed[i]}/{cand.Consumed[i]}"));
        int late = cand.ConsumeSteps == null ? -1 : Late(full, cand, n, idx);
        bool exact = mode switch
        {
            "fluid" => dd == 0,
            "mainonly" => dd == 0 && occ == 0 && late == 0,
            _ => occ == 0 && dd == 0 && full.Tick == cand.Tick && full.Emitted!.SequenceEqual(cand.Emitted!),
        };
        return new MergeRow(mode, k, n, exact, occ, dd, deliv, late, cand.Cost, cand.Transient, cand.Cycle);
    }

    /// <summary>Seeded sweep: 200 random period combos (1..6) x 3 networks, random N in 1..5000; CYCLESKIP must equal FULL.
    /// Also records the true transient/cycle (CYCLESKIP with N = 1,000,000, no FULL).</summary>
    public static SweepSummary Sweep(int combos, int seed)
    {
        var rng = new Random(seed);
        int runs = 0, bad = 0, mt = 0, mc = 0, mcost = 0, ut = 0, uc = 0;
        for (int i = 0; i < combos; i++)
            foreach (var net in MergeNetworks.All)
            {
                var f = new FeedConfig("sweep", net.Sources.Select(_ => rng.Next(1, 7)).ToArray(), rng.Next(1, 7));
                int n = rng.Next(1, 5001);
                var full = Full(net, f, n); var cs = CycleSkip(net, f, n);
                runs++;
                if (!Compare(net, "cycleskip", 0, n, full, cs).Exact || Late(full, cs, n, Enumerable.Range(0, net.Sources.Count)) != 0) bad++;
                mcost = Math.Max(mcost, cs.Cost);
                if (cs.Cycle > 0) { mt = Math.Max(mt, cs.Transient); mc = Math.Max(mc, cs.Cycle); }
                var u = CycleSkip(net, f, 1_000_000);
                ut = Math.Max(ut, u.Transient); uc = Math.Max(uc, u.Cycle);
            }
        return new SweepSummary(runs, bad, mt, mc, mcost, ut, uc);
    }

    /// <summary>The default sweep (200 combos, seed 20261027), computed once.</summary>
    public static readonly Lazy<SweepSummary> DefaultSweep = new(() => Sweep(200, 20261027));
}

/// <summary>Builds the BELT MERGE NETWORKS comparison table.</summary>
public static class MergeTable
{
    /// <summary>Table of every network x feed x N x method vs FULL, plus the sweep summary line.</summary>
    public static string Build(int[] ns)
    {
        var sb = new StringBuilder();
        sb.AppendLine("BELT MERGE NETWORKS (side priority: straight > left > right; ChainStep). N1 main+B, N2 main+B+C, N3 cascade main+B then +D. occDiff = cells with different item letter ('-' none; mainonly: A-vs-not-A cells); late = max |consume step full - method| per source ordinal ('-' no timings); deliv = letter full/method.");
        sb.AppendLine($"{"net",-3} {"cfg",-9} {"mode",-10} {"N",5} {"k",2} | {"exact",-5} {"occDiff",7} {"dDiff",5} {"late",5} | {"cost",5} {"t1",4} {"C",4} | deliv");
        var maxN = ns.Max();
        foreach (var net in MergeNetworks.All)
            foreach (var feed in MergeFeeds.For(net))
            {
                var full = MergeCatchUp.FullAt(net, feed, ns);
                foreach (var n in ns)
                {
                    void Row(string mode, int k, MergeRun r)
                    {
                        var w = MergeCatchUp.Compare(net, mode, k, n, full[n], r);
                        string o = w.OccDiff < 0 ? "-" : w.OccDiff.ToString(), l = w.StepsLate < 0 ? "-" : w.StepsLate.ToString();
                        string t1 = mode == "cycleskip" && w.Cycle > 0 ? w.Transient.ToString() : "", c = mode == "cycleskip" && w.Cycle > 0 ? w.Cycle.ToString() : "";
                        sb.AppendLine($"{net.Name,-3} {feed.Name,-9} {mode,-10} {n,5} {(k == 0 ? "-" : k.ToString()),2} | {w.Exact,-5} {o,7} {w.MaxDelivDiff,5} {l,5} | {w.Cost,5} {t1,4} {c,4} | {w.Deliv}");
                    }
                    foreach (var k in new[] { 2, 4, 8 }) Row("substep", k, MergeCatchUp.SubStep(net, feed, n, k));
                    foreach (var k in new[] { 2, 4, 8 }) Row("bigstep", k, MergeCatchUp.BigStep(net, feed, n, k));
                    Row("cycleskip", 0, MergeCatchUp.CycleSkip(net, feed, n));
                    Row("mainonly", 0, MergeCatchUp.MainOnly(net, feed, n));
                    Row("fluid", 0, MergeCatchUp.Fluid(net, feed, n));
                }
            }
        var s = MergeCatchUp.DefaultSweep.Value;
        sb.AppendLine($"CYCLESKIP SWEEP (seed 20261027, 200 period combos 1..6 x 3 networks, N random 1..5000): runs={s.Runs} mismatches={s.Mismatches} maxTransient(with N)={s.MaxTransient} maxCycle(with N)={s.MaxCycle} maxCost={s.MaxCost}; unbounded-N bound: transient<={s.MaxUnboundedTransient} cycle<={s.MaxUnboundedCycle}");
        return sb.ToString();
    }
}
