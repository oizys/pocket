// ============================================================================
// Pockets.Spikes.Bench - immutable vs mutable-hot-set simulation cost at 8 Hz
//
// RUN:   dotnet run -c Release --project spikes/Pockets.Spikes.Bench [-- --quick] [--no-paced] [--paced-seconds N]
//        --quick        : fewer steps (300/1000), 2 trials, 5 s paced run
//        --no-paced     : skip the wall-paced multi-bag run
//
// WORKLOAD (identical in every variant): one 32x32 bag, all 1024 cells processed every step.
//   * plaque gather CA (double-buffered, toroidal Moore neighbourhood):
//       p' = (p + r_i + #neighbours with p>=128) mod 256 ; event PlaqueCrossed when p/64 changes.
//   * 8 east-pointing belts of 28 cells (rows 2,6,..,30; cols 2..29); source at tail emits every 2nd step
//     when empty, sink at head consumes 1/step; moves resolved head->tail. Events Moved/Emitted/Consumed.
// VARIANTS: a1 immutable records + builder; a2 immutable records + per-change SetItem;
//           b  mutable SoA + chunked append-only log + immutable snapshot every 8 / 64 steps;
//           b0 SoA floor (no snapshots, events dropped into a recycled chunk).
// MEASURED: per-step time (mean/p50/p99/max), alloc bytes/step, GC counts + pause time, retained heap growth,
//           events/step, and "bags per 125 ms period" budget. Every variant must end with identical checksums.
// ============================================================================
using System.Diagnostics;
using System.Runtime;
using System.Text;

namespace Pockets.Spikes.Bench;

/// <summary>Per-trial measurements for one variant.</summary>
public sealed record Trial(double MeanUs, double P50Us, double P99Us, double MaxUs, double AllocPerStep,
    int G0, int G1, int G2, double PauseMs, double HeapMb, double EventsPerStep, double RetainedMb,
    ulong StateHash, long EventCount, ulong EventHash, bool EventHashValid);

/// <summary>A named variant: factory plus the number of trials it should get.</summary>
public sealed record Variant(string Name, Func<int, Sim> Make, int TrialsFull);

/// <summary>Entry point and measurement harness.</summary>
public static class Program
{
    const int Warmup = 200;

    /// <summary>Run all measurements and print the report.</summary>
    public static void Main(string[] args)
    {
        bool quick = args.Contains("--quick"), paced = !args.Contains("--no-paced");
        int pacedSec = quick ? 5 : 60;
        int pi = Array.IndexOf(args, "--paced-seconds"); if (pi >= 0) pacedSec = int.Parse(args[pi + 1]);
        int[] stepCounts = quick ? new[] { 300, 1000 } : new[] { 1440, 4800 };
        var variants = new[]
        {
            new Variant("a1 immutable-builder", s => new ImmutableBuilderSim(s), 5),
            new Variant("a2 immutable-naive", s => new ImmutableNaiveSim(s), 3),
            new Variant("b SoA+log+snap/8", s => new BagSim(s, 8, true), 5),
            new Variant("b SoA+log+snap/64", s => new BagSim(s, 64, true), 5),
            new Variant("b0 SoA floor", s => new BagSim(s, 0, false), 5),
        };
        var sb = new StringBuilder();
        PrintEnv(sb);
        foreach (var steps in stepCounts)
        {
            Console.Error.WriteLine($"== {steps} steps");
            var res = variants.Select(v =>
            {
                int n = quick ? Math.Min(2, v.TrialsFull) : v.TrialsFull;
                Console.Error.WriteLine($"  {v.Name} x{n}");
                return Enumerable.Range(0, n).Select(_ => RunTrial(v, steps)).ToList();
            }).ToList();
            sb.AppendLine().AppendLine($"### {steps} steps = {steps / 8.0 / 60:0.0} min game time (+{Warmup} warm-up steps, as fast as possible)").AppendLine();
            sb.Append(Report(variants, res, steps));
        }
        if (paced) sb.Append(PacedReport(variants, pacedSec));
        sb.Append(Notes(quick, pacedSec, paced));
        Console.WriteLine(sb.ToString());
    }

    /// <summary>Print runtime/GC environment.</summary>
    static void PrintEnv(StringBuilder sb)
    {
#if DEBUG
        const string cfg = "Debug";
#else
        const string cfg = "Release";
#endif
        sb.AppendLine("## Environment").AppendLine();
        sb.AppendLine($"- .NET {Environment.Version} ({System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}), build: {cfg}, TieredCompilation: off");
        sb.AppendLine($"- GC: ServerGC={GCSettings.IsServerGC}, LatencyMode={GCSettings.LatencyMode}, concurrent=default");
        sb.AppendLine($"- ProcessorCount={Environment.ProcessorCount}, OS={System.Runtime.InteropServices.RuntimeInformation.OSDescription}");
    }

    /// <summary>Median of a sequence.</summary>
    static double Med(IEnumerable<double> xs) { var a = xs.OrderBy(x => x).ToArray(); return a.Length % 2 == 1 ? a[a.Length / 2] : (a[a.Length / 2 - 1] + a[a.Length / 2]) / 2; }

    /// <summary>Run one fresh warmed-up trial of a variant for the given number of timed steps.</summary>
    static Trial RunTrial(Variant v, int steps)
    {
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        var sim = v.Make(0);
        for (int i = 0; i < Warmup; i++) sim.Step();
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        long baseMem = GC.GetTotalMemory(true);
        var ticks = new long[steps];
        int g0 = GC.CollectionCount(0), g1 = GC.CollectionCount(1), g2 = GC.CollectionCount(2);
        var pause0 = GC.GetTotalPauseDuration(); long a0 = GC.GetAllocatedBytesForCurrentThread();
        long prev = Stopwatch.GetTimestamp();
        for (int i = 0; i < steps; i++)
        {
            sim.Step();
            long now = Stopwatch.GetTimestamp(); ticks[i] = now - prev; prev = now;
        }
        long a1 = GC.GetAllocatedBytesForCurrentThread();
        var pause = GC.GetTotalPauseDuration() - pause0;
        int d0 = GC.CollectionCount(0) - g0, d1 = GC.CollectionCount(1) - g1, d2 = GC.CollectionCount(2) - g2;
        double heap = GC.GetGCMemoryInfo().HeapSizeBytes / 1048576.0;
        long live = GC.GetTotalMemory(true) - baseMem;
        double us = 1e6 / Stopwatch.Frequency;
        var sorted = ticks.OrderBy(x => x).ToArray();
        var (cnt, eh, ehv) = sim.Events();
        long cntAtRun = cnt;
        var t = new Trial(ticks.Average() * us, sorted[(int)(0.5 * (steps - 1))] * us, sorted[(int)(0.99 * (steps - 1))] * us,
            sorted[^1] * us, (a1 - a0) / (double)steps, d0, d1, d2, pause.TotalMilliseconds, heap,
            0, live / 1048576.0, sim.StateHash(), cntAtRun, eh, ehv);
        GC.KeepAlive(sim);
        // events/step over the timed window ~ total / (warmup+steps)
        return t with { EventsPerStep = cnt / (double)(steps + Warmup) };
    }

    /// <summary>Build the markdown table (metrics as rows, variants as columns) plus checksum line and budget rows.</summary>
    static string Report(Variant[] vs, List<List<Trial>> res, int steps)
    {
        var all = res.SelectMany(r => r).ToList();
        var states = all.Select(t => t.StateHash).Distinct().ToList();
        var counts = all.Select(t => t.EventCount).Distinct().ToList();
        var hashes = all.Where(t => t.EventHashValid).Select(t => t.EventHash).Distinct().ToList();
        bool ok = states.Count == 1 && counts.Count == 1 && hashes.Count == 1;
        var sb = new StringBuilder();
        sb.AppendLine($"Checksum: state=0x{states[0]:x16} events={counts[0]} eventHash=0x{hashes[0]:x16} -> all variants/trials identical: **{(ok ? "YES" : "NO (MISMATCH)")}** (b0 compared on state+count only)").AppendLine();
        var rows = new List<(string, Func<List<Trial>, string>)>
        {
            ("trials", t => t.Count.ToString()),
            ("mean us/step (median; min-max)", t => $"{Med(t.Select(x => x.MeanUs)):0.0} ({t.Min(x => x.MeanUs):0.0}-{t.Max(x => x.MeanUs):0.0})"),
            ("p50 us/step", t => $"{Med(t.Select(x => x.P50Us)):0.0}"),
            ("p99 us/step", t => $"{Med(t.Select(x => x.P99Us)):0.0}"),
            ("max us/step", t => $"{Med(t.Select(x => x.MaxUs)):0.0}"),
            ("alloc bytes/step", t => $"{Med(t.Select(x => x.AllocPerStep)):N0}"),
            ("GC gen0/gen1/gen2 (median)", t => $"{Med(t.Select(x => (double)x.G0)):0}/{Med(t.Select(x => (double)x.G1)):0}/{Med(t.Select(x => (double)x.G2)):0}"),
            ("GC pause total ms", t => $"{Med(t.Select(x => x.PauseMs)):0.0}"),
            ("GC heap size at end MB", t => $"{Med(t.Select(x => x.HeapMb)):0.0}"),
            ("events/step", t => $"{Med(t.Select(x => x.EventsPerStep)):0.0}"),
            ("retained growth MB (live, measured)", t => $"{Med(t.Select(x => x.RetainedMb)):0.00}"),
            ("bags/125ms @100% (p50)", t => $"{125000 / Med(t.Select(x => x.P50Us)):N0}"),
            ("bags/125ms @10% (p50)", t => $"{12500 / Med(t.Select(x => x.P50Us)):N0}"),
            ("bags/125ms @100% (p99)", t => $"{125000 / Med(t.Select(x => x.P99Us)):N0}"),
            ("bags/125ms @10% (p99)", t => $"{12500 / Med(t.Select(x => x.P99Us)):N0}"),
        };
        sb.AppendLine("| metric | " + string.Join(" | ", vs.Select(v => v.Name)) + " |");
        sb.AppendLine("|---|" + string.Join("|", vs.Select(_ => "---")) + "|");
        rows.ForEach(r => sb.AppendLine($"| {r.Item1} | " + string.Join(" | ", res.Select(r.Item2)) + " |"));
        return sb.ToString();
    }

    /// <summary>Wall-paced 8 Hz run of many bags for variants a1 and b; returns markdown.</summary>
    static string PacedReport(Variant[] vs, int seconds)
    {
        const int Bags = 50;
        var picks = new[] { vs[0], vs[2] };
        var sb = new StringBuilder();
        sb.AppendLine().AppendLine($"### Paced run: {Bags} bags, {seconds} s wall, true 8 Hz (125 ms period)").AppendLine();
        sb.AppendLine("| metric | " + string.Join(" | ", picks.Select(p => p.Name)) + " |").AppendLine("|---|---|---|");
        var rs = picks.Select(p => { Console.Error.WriteLine($"  paced {p.Name}"); return Paced(p, Bags, seconds); }).ToList();
        string[] names = { "ticks", "missed deadlines", "work ms/tick p50", "work ms/tick p99", "work ms/tick max", "ticks with GC pause >1 ms", "max GC pause in a tick ms", "GC gen0/gen1/gen2", "total GC pause ms" };
        for (int i = 0; i < names.Length; i++) sb.AppendLine($"| {names[i]} | " + string.Join(" | ", rs.Select(r => r[i])) + " |");
        return sb.ToString();
    }

    /// <summary>Run one paced scenario and return the formatted metric strings.</summary>
    static string[] Paced(Variant v, int bags, int seconds)
    {
        GC.Collect();
        var sims = Enumerable.Range(0, bags).Select(i => v.Make(i)).ToArray();
        foreach (var s in sims) for (int i = 0; i < 50; i++) s.Step();
        GC.Collect();
        long period = Stopwatch.Frequency / 8; int nt = seconds * 8;
        var work = new double[nt]; int missed = 0, pauseTicks = 0; double maxPause = 0;
        int g0 = GC.CollectionCount(0), g1 = GC.CollectionCount(1), g2 = GC.CollectionCount(2);
        var pauseStart = GC.GetTotalPauseDuration(); long start = Stopwatch.GetTimestamp();
        for (int k = 0; k < nt; k++)
        {
            long tickStart = start + k * period; var p0 = GC.GetTotalPauseDuration();
            foreach (var s in sims) s.Step();
            long end = Stopwatch.GetTimestamp(); double dp = (GC.GetTotalPauseDuration() - p0).TotalMilliseconds;
            work[k] = (end - tickStart) * 1000.0 / Stopwatch.Frequency;
            if (dp > 1) pauseTicks++; maxPause = Math.Max(maxPause, dp);
            if (end > tickStart + period) missed++;
            long next = tickStart + period;
            while (Stopwatch.GetTimestamp() < next)
            {
                long left = (next - Stopwatch.GetTimestamp()) * 1000 / Stopwatch.Frequency;
                if (left > 2) Thread.Sleep((int)left - 1); else Thread.SpinWait(50);
            }
        }
        var tp = GC.GetTotalPauseDuration() - pauseStart; var sw = work.OrderBy(x => x).ToArray();
        GC.KeepAlive(sims);
        return new[] { nt.ToString(), missed.ToString(), $"{sw[(int)(0.5 * (nt - 1))]:0.00}", $"{sw[(int)(0.99 * (nt - 1))]:0.00}", $"{sw[^1]:0.00}",
            pauseTicks.ToString(), $"{maxPause:0.00}", $"{GC.CollectionCount(0) - g0}/{GC.CollectionCount(1) - g1}/{GC.CollectionCount(2) - g2}", $"{tp.TotalMilliseconds:0.0}" };
    }

    /// <summary>Static notes appended to the report.</summary>
    static string Notes(bool quick, int pacedSec, bool paced) => $@"
## Notes
- Times are Stopwatch ticks per Step() call; the per-step timestamp overhead (~25 ns) is included in every variant.
- Retained growth = live heap after forced GC minus live heap before the timed window (event log / snapshots / retained event objects). Immutable variants retain every event as a heap object (SimEventObj) in a per-step log; a1/a2 do NOT retain old states (only the current one).
- SimEvent (20 B struct) vs SimEventObj (record class, ~40 B + 8 B array slot): this alone changes the log footprint ~2.4x.
- a2 ran 3 trials (full mode) because it is slow; snapshots in b: ImmutableArray<int> x2 (plaque,item) = 8 KB each.
- BagSim.Step(version, intents) is the pure-looking facade (version, intents) -> (version', event range); intents are accepted but unused in this workload.
- Paced run: {(paced ? $"{pacedSec} s wall, 50 bags/variant, work measured per 125 ms tick; GC pauses attributed per tick via GC.GetTotalPauseDuration deltas" : "skipped")}.
{(quick ? "- QUICK mode: reduced steps/trials; rerun without --quick for the full report." : "")}
";
}
