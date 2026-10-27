namespace Pockets.Spikes.Plaque;

/// <summary>P1 = pure per-cell accumulator; P2 = spread (adds count of Moore neighbours with p >= T in the previous buffer).</summary>
public enum PlaqueRule { Accumulator, Spread }

/// <summary>Bag geometry and constants. Rate[i] is the per-cell rate r_i. Requires Cap >= T.</summary>
public sealed record PlaqueParams(int W, int H, int Cap, int T, int[] Rate)
{
    /// <summary>Corner accumulator: rate = max(0, 6 - (x+y)/2), so it is largest at (0,0) and zero far away.</summary>
    public static PlaqueParams Corner(int w = 16, int h = 16, int cap = 600, int t = 50) =>
        new(w, h, cap, t, Enumerable.Range(0, w * h).Select(i => Math.Max(0, 6 - (i % w + i / w) / 2)).ToArray());

    /// <summary>Deterministic pseudo-random initial grid in [0, 2T) from a seed (counter-hash based).</summary>
    public static int[] RandomInitial(PlaqueParams pr, int seed) =>
        Enumerable.Range(0, pr.W * pr.H).Select(i => (int)(Lint.Hash(seed, 0, i) % (uint)(2 * pr.T))).ToArray();
}

/// <summary>Result of a run: final values, first tick each cell was observed >= T (-1 = never), and cost (step calls / jumps).</summary>
public sealed record PlaqueRun(int[] P, int[] Cross, int Cost);

/// <summary>Comparison of a candidate run against the FULL reference.</summary>
public sealed record CatchUpReport(string Label, bool Exact, int DiffCells, int MaxAbsDiff, long SumAbsDiff,
    int CrossDiffCells, int MaxCrossDiff, int Cost)
{
    /// <summary>One-line summary.</summary>
    public override string ToString() =>
        $"{Label}: exact={Exact} diffCells={DiffCells} maxAbs={MaxAbsDiff} sumAbs={SumAbsDiff} crossDiffCells={CrossDiffCells} maxCrossDiff={MaxCrossDiff} cost={Cost}";
}

/// <summary>Plaque simulation modes (FULL, rate 1/k big-step, rate 1/k sub-stepped, catch-up).</summary>
public static class PlaqueSim
{
    private static int Count(PlaqueParams pr, int[] prev, int i)
    {
        int x = i % pr.W, y = i / pr.W, c = 0;
        for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dy == 0) continue;
                int nx = x + dx, ny = y + dy;
                if (nx >= 0 && ny >= 0 && nx < pr.W && ny < pr.H && prev[ny * pr.W + nx] >= pr.T) c++;
            }
        return c;
    }

    /// <summary>One double-buffered step; <paramref name="mult"/> = k gives the naive big step p' = min(Cap, p + k*(r + count)).</summary>
    public static int[] Step(PlaqueRule rule, PlaqueParams pr, int[] prev, int mult = 1) =>
        prev.Select((p, i) => Math.Min(pr.Cap, p + mult * (pr.Rate[i] + (rule == PlaqueRule.Spread ? Count(pr, prev, i) : 0)))).ToArray();

    private static int[] InitCross(PlaqueParams pr, int[] p0) => p0.Select(p => p >= pr.T ? 0 : -1).ToArray();

    private static void Observe(PlaqueParams pr, int[] p, int[] cross, int tick)
    {
        for (int i = 0; i < p.Length; i++) if (cross[i] < 0 && p[i] >= pr.T) cross[i] = tick;
    }

    /// <summary>Reference: step every outer step.</summary>
    public static PlaqueRun Full(PlaqueRule rule, PlaqueParams pr, int[] p0, int n)
    {
        var p = p0; var cross = InitCross(pr, p0);
        for (int t = 1; t <= n; t++) { p = Step(rule, pr, p); Observe(pr, p, cross, t); }
        return new PlaqueRun(p, cross, n);
    }

    /// <summary>
    /// Rate 1/k naive big step: an integer accumulator counts outer steps; at k it applies ONE step with k-times rates.
    /// With <paramref name="flush"/> the leftover n mod k ticks are applied as a final big step with multiplier = leftover.
    /// </summary>
    public static PlaqueRun BigStep(PlaqueRule rule, PlaqueParams pr, int[] p0, int n, int k, bool flush)
    {
        var p = p0; var cross = InitCross(pr, p0); int acc = 0, ticks = 0, cost = 0;
        for (int o = 1; o <= n; o++)
        {
            if (++acc < k) continue;
            p = Step(rule, pr, p, k); ticks += k; acc = 0; cost++; Observe(pr, p, cross, ticks);
        }
        if (flush && acc > 0) { p = Step(rule, pr, p, acc); ticks += acc; cost++; Observe(pr, p, cross, ticks); }
        return new PlaqueRun(p, cross, cost);
    }

    /// <summary>Rate 1/k sub-stepped: every k-th outer step runs k ordinary steps (same total work). Flush runs the leftover.</summary>
    public static PlaqueRun SubStepped(PlaqueRule rule, PlaqueParams pr, int[] p0, int n, int k, bool flush)
    {
        var p = p0; var cross = InitCross(pr, p0); int acc = 0, ticks = 0, cost = 0;
        void Run(int m) { for (int j = 0; j < m; j++) { p = Step(rule, pr, p); ticks++; cost++; Observe(pr, p, cross, ticks); } }
        for (int o = 1; o <= n; o++) { if (++acc < k) continue; Run(k); acc = 0; }
        if (flush && acc > 0) Run(acc);
        return new PlaqueRun(p, cross, cost);
    }

    /// <summary>
    /// Offline catch-up. P1: closed form min(Cap, p0 + n*r) (cost 1). P2: threshold-event fast-forward: flags are frozen
    /// between crossings so every cell's increment inc_i = r_i + flaggedNeighbours is constant; jump
    /// m = min(remaining, min over unflagged cells with inc>0 of ceil((T-p)/inc)), recompute flags, repeat. Cost = jumps.
    /// Exactness argument: steps 1..d use flags derived from p_0..p_{d-1}, which for the crossing cell are all &lt; T.
    /// </summary>
    public static PlaqueRun CatchUp(PlaqueRule rule, PlaqueParams pr, int[] p0, int n)
    {
        var cross = InitCross(pr, p0);
        if (rule == PlaqueRule.Accumulator)
        {
            var fin = p0.Select((p, i) => (int)Math.Min(pr.Cap, p + (long)n * pr.Rate[i])).ToArray();
            for (int i = 0; i < p0.Length; i++)
                if (cross[i] < 0 && pr.Rate[i] > 0)
                {
                    long d = (pr.T - p0[i] + pr.Rate[i] - 1) / pr.Rate[i];
                    if (d <= n) cross[i] = (int)d;
                }
            return new PlaqueRun(fin, cross, 1);
        }
        var cur = p0; int done = 0, jumps = 0;
        while (done < n)
        {
            var inc = cur.Select((_, i) => pr.Rate[i] + Count(pr, cur, i)).ToArray();
            long dmin = cur.Select((p, i) => (p, i)).Where(z => z.p < pr.T && inc[z.i] > 0)
                .Select(z => (long)(pr.T - z.p + inc[z.i] - 1) / inc[z.i]).DefaultIfEmpty(long.MaxValue).Min();
            int m = (int)Math.Min(n - done, dmin);
            cur = cur.Select((p, i) => (int)Math.Min(pr.Cap, p + (long)m * inc[i])).ToArray();
            done += m; jumps++;
            Observe(pr, cur, cross, done);
        }
        return new PlaqueRun(cur, cross, jumps);
    }

    /// <summary>Compare candidate to reference. Cells never crossing within <paramref name="horizon"/> count as horizon+1.</summary>
    public static CatchUpReport Compare(string label, PlaqueRun reference, PlaqueRun cand, int horizon)
    {
        var d = reference.P.Zip(cand.P, (a, b) => Math.Abs(a - b)).ToArray();
        var x = reference.Cross.Zip(cand.Cross, (a, b) => Math.Abs((a < 0 ? horizon + 1 : a) - (b < 0 ? horizon + 1 : b))).ToArray();
        return new CatchUpReport(label, d.All(v => v == 0), d.Count(v => v > 0), d.Max(), d.Sum(v => (long)v),
            x.Count(v => v > 0), x.Max(), cand.Cost);
    }
}

/// <summary>Counter-based RNG vs stateful RNG demo ("lint": neither admits a closed-form skip => MustSimulate).</summary>
public static class Lint
{
    /// <summary>SplitMix-style counter hash keyed (seed, step, entityId); order independent.</summary>
    public static uint Hash(int seed, int step, int id)
    {
        unchecked
        {
            ulong z = (ulong)(uint)seed * 0x9E3779B97F4A7C15UL + (ulong)(uint)step * 0xBF58476D1CE4E5B9UL + (ulong)(uint)id * 0x94D049BB133111EBUL;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return (uint)((z ^ (z >> 31)) >> 16);
        }
    }

    /// <summary>
    /// Random walkers using ONE stateful System.Random. batch=1 is step-major (reference); batch=k processes k steps per
    /// walker in turn (entity-major within a batch), which permutes the draw order and so changes the result.
    /// </summary>
    public static int[] StatefulWalk(int seed, int walkers, int steps, int batch) =>
        Walk(walkers, steps, batch, new Random(seed), null, seed);

    /// <summary>Same walkers with draws = Hash(seed, step, id): identical under any batching.</summary>
    public static int[] CounterWalk(int seed, int walkers, int steps, int batch) =>
        Walk(walkers, steps, batch, null, null, seed);

    private static int[] Walk(int walkers, int steps, int batch, Random? rng, object? _, int seed)
    {
        var pos = new int[walkers];
        int Draw(int step, int id) => rng != null ? rng.Next(2) * 2 - 1 : (int)(Hash(seed, step, id) & 1) * 2 - 1;
        for (int b = 0; b < steps; b += batch)
            for (int id = 0; id < walkers; id++)
                for (int s = b; s < Math.Min(steps, b + batch); s++)
                    pos[id] += Draw(s, id);
        return pos;
    }
}
