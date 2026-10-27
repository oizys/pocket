using Pockets.Spikes.Belts;

namespace Pockets.Spikes.Plaque;

/// <summary>Straight belt of Length cells: a source (period SourcePeriod) in front, a sink (period SinkPeriod) at the end.</summary>
public sealed record LineConfig(int Length, int SourcePeriod, int SinkPeriod);

/// <summary>Observable state of a line: '#'/'.' per belt cell, cumulative consumed/emitted, and cost (steps, jumps or items).</summary>
public sealed record LineSnapshot(string Occupancy, int Consumed, int Emitted, int Cost);

/// <summary>Comparison of a candidate line snapshot against FULL.</summary>
public sealed record LineReport(string Label, bool Exact, int OccDiffCells, int ConsumedFull, int ConsumedCand,
    int EmittedFull, int EmittedCand, int Cost)
{
    /// <summary>One-line summary.</summary>
    public override string ToString() =>
        $"{Label}: exact={Exact} occDiff={OccDiffCells} consumed {ConsumedFull}/{ConsumedCand} emitted {EmittedFull}/{EmittedCand} cost={Cost}";
}

/// <summary>
/// Belt-line rate/catch-up models. Time semantics (all models): step t (t = 0,1,..) runs with Tick = t; the source fires when
/// t % s == 0 and emits only if its target cell is free after resolution (otherwise that emission is SKIPPED, not queued);
/// the sink consumes the head item at step t iff t % c == 0. State "after tau steps" is what is compared.
/// </summary>
public static class LineModels
{
    private static BeltState Build(LineConfig cfg) =>
        BeltState.Parse("⇒" + new string('▶', cfg.Length) + "K", "A" + new string('.', cfg.Length + 1))
            .WithPeriod(0, 0, cfg.SourcePeriod).WithPeriod(cfg.Length + 1, 0, cfg.SinkPeriod);

    private static string Occ(BeltState s, int len) =>
        new(Enumerable.Range(1, len).Select(x => s.Item[x] != '\0' ? '#' : '.').ToArray());

    /// <summary>Reference: n real ChainSteps.</summary>
    public static LineSnapshot Full(LineConfig cfg, int n) => Run(cfg, n, n);

    private static LineSnapshot Run(LineConfig cfg, int steps, int cost)
    {
        var s = Build(cfg); int cons = 0, em = 0;
        for (int i = 0; i < steps; i++)
        {
            var r = BeltSim.ChainStep(s); s = r.State;
            cons += r.Events.OfType<Consumed>().Count(); em += r.Events.OfType<Emitted>().Count();
        }
        return new LineSnapshot(Occ(s, cfg.Length), cons, em, cost);
    }

    /// <summary>Rate 1/k sub-stepped: batches of k ChainSteps every k-th outer step; flush runs the leftover n mod k.</summary>
    public static LineSnapshot SubStepped(LineConfig cfg, int n, int k, bool flush)
    {
        int steps = flush ? n : n / k * k;
        return Run(cfg, steps, steps);
    }

    /// <summary>
    /// Rate 1/k NAIVE jump: every k-th outer step (leftover as a short window when flushed) each item advances up to w cells
    /// at once, blocked by the item ahead; THEN the sink consumes at most one head item per jump if any sink tick fell in the
    /// window; THEN the source places one item per fire time in the window (earliest first), advanced by the remaining ticks,
    /// blocked by the nearest item. It ignores intra-window ordering (sink frees space mid-window, items trailing the head
    /// could have followed it) and so diverges when the belt is backlogged.
    /// </summary>
    public static LineSnapshot NaiveJump(LineConfig cfg, int n, int k, bool flush)
    {
        var pos = new List<int>(); // head first (descending positions)
        int L = cfg.Length, cons = 0, em = 0, t0 = 0, cost = 0;
        int Count(int from, int to, int period) => Enumerable.Range(from, to - from).Count(t => t % period == 0);
        void Jump(int w)
        {
            cost++;
            var moved = new List<int>();
            for (int i = 0; i < pos.Count; i++)
                moved.Add(Math.Min(pos[i] + w, i == 0 ? L - 1 : moved[i - 1] - 1));
            pos = moved;
            if (Count(t0, t0 + w, cfg.SinkPeriod) > 0 && pos.Count > 0 && pos[0] == L - 1) { pos.RemoveAt(0); cons++; }
            foreach (var f in Enumerable.Range(t0, w).Where(t => t % cfg.SourcePeriod == 0))
            {
                int limit = pos.Count == 0 ? L - 1 : pos[^1] - 1;
                int p = Math.Min(t0 + w - f - 1, limit);
                if (p < 0) continue;
                pos.Add(p); em++;
            }
            t0 += w;
        }
        for (int o = 1; o <= n / k; o++) Jump(k);
        if (flush && n % k > 0) Jump(n % k);
        var occ = Enumerable.Range(0, L).Select(p => pos.Contains(p) ? '#' : '.').ToArray();
        return new LineSnapshot(new string(occ), cons, em, cost);
    }

    /// <summary>
    /// Event-driven closed form over ITEMS (not steps). Let T_i(p) = first tau at which item i is at cell >= p (T_i(L) = tau
    /// after it was consumed). With E_i = step at which item i is emitted:
    ///   T_i(0) = E_i + 1;  T_i(p) = max(E_i + 1 + p, T_{i-1}(p+1))  (free flow, or follow the item ahead into the cell it vacates);
    ///   T_i(L) = ceilMultiple(T_i(L-1), c) + 1;  E_i = first multiple f of s with f > E_{i-1} and f + 1 >= T_{i-1}(1).
    /// Position at n = max p with T_i(p) &lt;= n. Exact for the empty-start line; cost = items visited.
    /// </summary>
    public static LineSnapshot CatchUp(LineConfig cfg, int n)
    {
        int L = cfg.Length, s = cfg.SourcePeriod, c = cfg.SinkPeriod;
        var prev = new long[L + 1]; // T_{-1}: no constraint
        long prevE = -1; int cons = 0, items = 0;
        var occ = new char[L]; Array.Fill(occ, '.');
        while (true)
        {
            long lo = Math.Max(prevE + 1, items == 0 ? 0 : prev[1] - 1);
            long e = (lo + s - 1) / s * s;
            if (e + 1 > n) break;
            var t = new long[L + 1];
            t[0] = e + 1;
            for (int p = 1; p < L; p++) t[p] = Math.Max(e + 1 + p, prev[p + 1]);
            t[L] = (t[L - 1] + c - 1) / c * c + 1;
            if (t[L] <= n) cons++;
            else occ[Enumerable.Range(0, L).Last(p => t[p] <= n)] = '#';
            prev = t; prevE = e; items++;
        }
        return new LineSnapshot(new string(occ), cons, items, items);
    }

    /// <summary>Compare a candidate to the FULL reference.</summary>
    public static LineReport Compare(string label, LineSnapshot full, LineSnapshot cand)
    {
        int diff = full.Occupancy.Zip(cand.Occupancy).Count(z => z.First != z.Second);
        return new LineReport(label, diff == 0 && full.Consumed == cand.Consumed && full.Emitted == cand.Emitted, diff,
            full.Consumed, cand.Consumed, full.Emitted, cand.Emitted, cand.Cost);
    }
}

/// <summary>Builds the human-readable Spike C comparison tables.</summary>
public static class ComparisonTable
{
    /// <summary>Plaque (fresh all-zero 16x16 bag, corner rates, Cap 600, T 50) and belt (L=12) tables for each n and k.</summary>
    public static string Build(int[] ns, int[] ks)
    {
        var pr = PlaqueParams.Corner();
        var p0 = new int[pr.W * pr.H];
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("PLAQUE 16x16, Cap=600 T=50, zero start, corner rate max(0,6-(x+y)/2). cost = step calls / jumps. xDiff = threshold-crossing step diffs.");
        sb.AppendLine($"{"rule",-4} {"mode",-14} {"N",5} {"k",2} | {"exact",-5} {"diffCells",9} {"maxAbs",6} {"sumAbs",7} {"xDiffCells",10} {"maxXDiff",8} | {"cost",5}");
        void Row(string rule, string mode, int n, int k, CatchUpReport r) =>
            sb.AppendLine($"{rule,-4} {mode,-14} {n,5} {(k == 0 ? "-" : k.ToString()),2} | {r.Exact,-5} {r.DiffCells,9} {r.MaxAbsDiff,6} {r.SumAbsDiff,7} {r.CrossDiffCells,10} {r.MaxCrossDiff,8} | {r.Cost,5}");
        foreach (var (rule, name) in new[] { (PlaqueRule.Accumulator, "P1"), (PlaqueRule.Spread, "P2") })
            foreach (var n in ns)
            {
                var full = PlaqueSim.Full(rule, pr, p0, n);
                foreach (var k in ks)
                {
                    Row(name, "bigstep", n, k, PlaqueSim.Compare("", full, PlaqueSim.BigStep(rule, pr, p0, n, k, true), n));
                    Row(name, "substep", n, k, PlaqueSim.Compare("", full, PlaqueSim.SubStepped(rule, pr, p0, n, k, true), n));
                    if (n % k != 0)
                        Row(name, "substep-noflush", n, k, PlaqueSim.Compare("", full, PlaqueSim.SubStepped(rule, pr, p0, n, k, false), n));
                }
                Row(name, name == "P1" ? "p1-catchup" : "p2-catchup", n, 0, PlaqueSim.Compare("", full, PlaqueSim.CatchUp(rule, pr, p0, n), n));
            }
        sb.AppendLine();
        sb.AppendLine("BELT L=12 (ChainStep). s=source period, c=sink period. occDiff = cells with different occupancy; cons/emit = full/cand.");
        sb.AppendLine($"{"cfg",-12} {"mode",-14} {"N",5} {"k",2} | {"exact",-5} {"occDiff",7} {"cons f/c",9} {"emit f/c",9} | {"cost",5}");
        void BRow(string cfg, string mode, int n, int k, LineReport r) =>
            sb.AppendLine($"{cfg,-12} {mode,-14} {n,5} {(k == 0 ? "-" : k.ToString()),2} | {r.Exact,-5} {r.OccDiffCells,7} {r.ConsumedFull + "/" + r.ConsumedCand,9} {r.EmittedFull + "/" + r.EmittedCand,9} | {r.Cost,5}");
        foreach (var (s, c, name) in new[] { (3, 1, "s3c1 under"), (1, 3, "s1c3 backlog") })
        {
            var cfg = new LineConfig(12, s, c);
            foreach (var n in ns)
            {
                var full = LineModels.Full(cfg, n);
                foreach (var k in ks)
                {
                    BRow(name, "substep", n, k, LineModels.Compare("", full, LineModels.SubStepped(cfg, n, k, true)));
                    BRow(name, "naive-jump", n, k, LineModels.Compare("", full, LineModels.NaiveJump(cfg, n, k, true)));
                }
                BRow(name, "belt-catchup", n, 0, LineModels.Compare("", full, LineModels.CatchUp(cfg, n)));
            }
        }
        return sb.ToString();
    }
}
