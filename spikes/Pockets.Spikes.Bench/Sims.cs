using System.Collections.Immutable;
using System.Runtime.CompilerServices;

namespace Pockets.Spikes.Bench;

/// <summary>Kinds of events emitted by the simulation (shared by every variant).</summary>
public enum EventKind { PlaqueCrossed = 0, Moved = 1, Emitted = 2, Consumed = 3 }

/// <summary>Compact value-type event used by the mutable SoA variant (20 bytes).</summary>
public readonly record struct SimEvent(int Step, EventKind Kind, int A, int B, int C);

/// <summary>Reference-type event record, the way the codebase would write it in the immutable style.</summary>
public sealed record SimEventObj(int Step, EventKind Kind, int A, int B, int C);

/// <summary>Immutable per-cell record (reference type, like today's Cell).</summary>
public sealed record CellRec(int Plaque, int Rate, int? Item, sbyte BeltDir, byte Kind);

/// <summary>Immutable bag state for the immutable variants.</summary>
public sealed record BagState(int Step, int NextItemId, ImmutableArray<CellRec> Cells);

/// <summary>Result of the pure-looking facade Step: new version plus the range of events it appended.</summary>
public readonly record struct StepResult(int Version, long FirstEvent, int EventCount);

/// <summary>Shared deterministic rules, geometry and hashing used by all variants.</summary>
public static class Rules
{
    public const int W = 32, N = W * W, BeltLen = 28, BeltStartCol = 2, HeadCol = BeltStartCol + BeltLen - 1;
    /// <summary>Rows (2,6,...,30) that carry an east-pointing belt.</summary>
    public static readonly int[] BeltRows = Enumerable.Range(0, 8).Select(k => 2 + 4 * k).ToArray();
    /// <summary>Toroidal Moore-neighbour table: 8 entries per cell.</summary>
    public static readonly int[] Nbr = BuildNbr();

    static int[] BuildNbr()
    {
        var t = new int[N * 8];
        for (int i = 0; i < N; i++)
        {
            int x = i % W, y = i / W, k = 0;
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                    if (dx != 0 || dy != 0)
                        t[i * 8 + k++] = ((y + dy + W) % W) * W + (x + dx + W) % W;
        }
        return t;
    }

    /// <summary>Integer avalanche hash.</summary>
    public static uint Mix(uint x) { x ^= x >> 16; x *= 0x7feb352d; x ^= x >> 15; x *= 0x846ca68b; x ^= x >> 16; return x; }
    /// <summary>Per-cell plaque growth rate 0..3 from position and seed.</summary>
    public static int Rate(int i, int seed) => (int)((Mix((uint)(i * 2 + 1) ^ ((uint)seed * 0x9E3779B9u)) >> 7) & 3u);
    /// <summary>Initial plaque value 0..255 from position and seed.</summary>
    public static int InitPlaque(int i, int seed) => (int)(Mix((uint)(i * 31 + 12345) ^ ((uint)seed * 0x85EBCA6Bu)) & 255u);
    /// <summary>True when the cell index lies on a belt line.</summary>
    public static bool OnBelt(int i) { int c = i % W, r = i / W; return c >= BeltStartCol && c <= HeadCol && Array.IndexOf(BeltRows, r) >= 0; }
    /// <summary>FNV-style fold of one value into a rolling hash.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong Fold(ulong h, int v) => (h ^ (uint)v) * 1099511628211UL;
    /// <summary>Fold a whole event into a rolling hash.</summary>
    public static ulong FoldEvent(ulong h, int step, EventKind k, int a, int b, int c)
        => Fold(Fold(Fold(Fold(Fold(h, step), (int)k), a), b), c);
}

/// <summary>Common harness surface of every variant.</summary>
public abstract class Sim
{
    /// <summary>Advance the world by one 125 ms step.</summary>
    public abstract void Step();
    /// <summary>Hash of final plaque, items, step and next item id.</summary>
    public abstract ulong StateHash();
    /// <summary>Total emitted events and their hash (hash valid = false when events are not retained).</summary>
    public abstract (long Count, ulong Hash, bool HashValid) Events();
}

/// <summary>Variant a1: immutable records, next state built with ImmutableArray builder + with-expressions.</summary>
public sealed class ImmutableBuilderSim : Sim
{
    BagState s;
    readonly List<ImmutableArray<SimEventObj>> log = new();
    long count;

    /// <summary>Create the initial state for a seed.</summary>
    public ImmutableBuilderSim(int seed) { s = Initial(seed); }

    /// <summary>Build the initial immutable bag state.</summary>
    public static BagState Initial(int seed)
    {
        var b = ImmutableArray.CreateBuilder<CellRec>(Rules.N);
        for (int i = 0; i < Rules.N; i++)
        {
            bool belt = Rules.OnBelt(i); int c = i % Rules.W;
            byte kind = !belt ? (byte)0 : c == Rules.BeltStartCol ? (byte)2 : c == Rules.HeadCol ? (byte)3 : (byte)1;
            b.Add(new CellRec(Rules.InitPlaque(i, seed), Rules.Rate(i, seed), null, (sbyte)(belt ? 1 : 0), kind));
        }
        return new BagState(0, 1, b.MoveToImmutable());
    }

    /// <inheritdoc/>
    public override void Step()
    {
        var prev = s.Cells; int step = s.Step, nid = s.NextItemId;
        var b = ImmutableArray.CreateBuilder<CellRec>(Rules.N); b.Count = Rules.N;
        var ev = ImmutableArray.CreateBuilder<SimEventObj>();
        var nbr = Rules.Nbr;
        for (int i = 0; i < Rules.N; i++)
        {
            int cnt = 0;
            for (int k = 0; k < 8; k++) if (prev[nbr[i * 8 + k]].Plaque >= 128) cnt++;
            var c = prev[i]; int p = c.Plaque, np = (p + c.Rate + cnt) & 255;
            b[i] = c with { Plaque = np };
            if ((np >> 6) != (p >> 6)) ev.Add(new SimEventObj(step, EventKind.PlaqueCrossed, i, (np >> 6) > (p >> 6) ? 1 : 0, np >> 6));
        }
        foreach (var row in Rules.BeltRows)
        {
            int baseI = row * Rules.W, head = baseI + Rules.HeadCol;
            var h = b[head];
            if (h.Item is int hid) { ev.Add(new SimEventObj(step, EventKind.Consumed, hid, head, 0)); b[head] = h with { Item = null }; }
            for (int col = Rules.HeadCol - 1; col >= Rules.BeltStartCol; col--)
            {
                int i = baseI + col; var cur = b[i];
                if (cur.Item is int id && b[i + 1].Item is null)
                {
                    b[i + 1] = b[i + 1] with { Item = id }; b[i] = cur with { Item = null };
                    ev.Add(new SimEventObj(step, EventKind.Moved, id, i, i + 1));
                }
            }
            int t = baseI + Rules.BeltStartCol;
            if ((step & 1) == 0 && b[t].Item is null)
            {
                int id = nid++; b[t] = b[t] with { Item = id };
                ev.Add(new SimEventObj(step, EventKind.Emitted, id, t, 0));
            }
        }
        s = new BagState(step + 1, nid, b.MoveToImmutable());
        var evi = ev.ToImmutable(); count += evi.Length; log.Add(evi);
    }

    /// <inheritdoc/>
    public override ulong StateHash() => StateHashOf(s);
    /// <summary>Hash a BagState.</summary>
    public static ulong StateHashOf(BagState s)
    {
        ulong h = 14695981039346656037UL; h = Rules.Fold(h, s.Step); h = Rules.Fold(h, s.NextItemId);
        foreach (var c in s.Cells) { h = Rules.Fold(h, c.Plaque); h = Rules.Fold(h, c.Item ?? 0); }
        return h;
    }
    /// <inheritdoc/>
    public override (long, ulong, bool) Events()
    {
        ulong h = 14695981039346656037UL;
        foreach (var arr in log) foreach (var e in arr) h = Rules.FoldEvent(h, e.Step, e.Kind, e.A, e.B, e.C);
        return (count, h, true);
    }
}

/// <summary>Variant a2: same records but every change goes through ImmutableArray.SetItem (copies the array each call).</summary>
public sealed class ImmutableNaiveSim : Sim
{
    BagState s;
    readonly List<System.Collections.Immutable.ImmutableList<SimEventObj>> log = new();
    long count;

    /// <summary>Create the initial state for a seed.</summary>
    public ImmutableNaiveSim(int seed) { s = ImmutableBuilderSim.Initial(seed); }

    /// <inheritdoc/>
    public override void Step()
    {
        var prev = s.Cells; var cur = prev; int step = s.Step, nid = s.NextItemId;
        var ev = System.Collections.Immutable.ImmutableList<SimEventObj>.Empty;
        var nbr = Rules.Nbr;
        for (int i = 0; i < Rules.N; i++)
        {
            int cnt = 0;
            for (int k = 0; k < 8; k++) if (prev[nbr[i * 8 + k]].Plaque >= 128) cnt++;
            var c = prev[i]; int p = c.Plaque, np = (p + c.Rate + cnt) & 255;
            if (np != p) cur = cur.SetItem(i, c with { Plaque = np });
            if ((np >> 6) != (p >> 6)) ev = ev.Add(new SimEventObj(step, EventKind.PlaqueCrossed, i, (np >> 6) > (p >> 6) ? 1 : 0, np >> 6));
        }
        foreach (var row in Rules.BeltRows)
        {
            int baseI = row * Rules.W, head = baseI + Rules.HeadCol;
            if (cur[head].Item is int hid) { ev = ev.Add(new SimEventObj(step, EventKind.Consumed, hid, head, 0)); cur = cur.SetItem(head, cur[head] with { Item = null }); }
            for (int col = Rules.HeadCol - 1; col >= Rules.BeltStartCol; col--)
            {
                int i = baseI + col;
                if (cur[i].Item is int id && cur[i + 1].Item is null)
                {
                    cur = cur.SetItem(i + 1, cur[i + 1] with { Item = id });
                    cur = cur.SetItem(i, cur[i] with { Item = null });
                    ev = ev.Add(new SimEventObj(step, EventKind.Moved, id, i, i + 1));
                }
            }
            int t = baseI + Rules.BeltStartCol;
            if ((step & 1) == 0 && cur[t].Item is null)
            {
                int id = nid++; cur = cur.SetItem(t, cur[t] with { Item = id });
                ev = ev.Add(new SimEventObj(step, EventKind.Emitted, id, t, 0));
            }
        }
        s = new BagState(step + 1, nid, cur); count += ev.Count; log.Add(ev);
    }

    /// <inheritdoc/>
    public override ulong StateHash() => ImmutableBuilderSim.StateHashOf(s);
    /// <inheritdoc/>
    public override (long, ulong, bool) Events()
    {
        ulong h = 14695981039346656037UL;
        foreach (var l in log) foreach (var e in l) h = Rules.FoldEvent(h, e.Step, e.Kind, e.A, e.B, e.C);
        return (count, h, true);
    }
}

/// <summary>Immutable point-in-time copy of the mutable world (what the outside world sees).</summary>
public sealed record Snapshot(int Step, int NextItemId, ImmutableArray<int> Plaque, ImmutableArray<int> Item);

/// <summary>Append-only chunked event log: appends never copy; optionally recycles one chunk (floor variant).</summary>
public sealed class EventLog
{
    public const int ChunkSize = 4096;
    readonly bool retain; readonly List<SimEvent[]> chunks = new();
    SimEvent[] cur; int pos;
    /// <summary>Total events ever appended.</summary>
    public long Count { get; private set; }

    /// <summary>Create a log; retain=false reuses a single chunk and drops old events.</summary>
    public EventLog(bool retain) { this.retain = retain; cur = new SimEvent[ChunkSize]; chunks.Add(cur); }
    /// <summary>Append one event.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Add(int step, EventKind k, int a, int b, int c)
    {
        if (pos == ChunkSize) Next();
        cur[pos++] = new SimEvent(step, k, a, b, c); Count++;
    }
    void Next() { if (retain) { cur = new SimEvent[ChunkSize]; chunks.Add(cur); } pos = 0; }
    /// <summary>Enumerate retained events in order (only meaningful when retain = true).</summary>
    public IEnumerable<SimEvent> All() => chunks.SelectMany((ch, ci) => ch.Take(ci == chunks.Count - 1 ? pos : ChunkSize));
}

/// <summary>Variant b / b0: mutable SoA hot set, double-buffered, events to an append-only log, periodic immutable snapshots.</summary>
public sealed class BagSim : Sim
{
    int[] plaque = new int[Rules.N], plaqueNext = new int[Rules.N];
    readonly int[] rate = new int[Rules.N]; readonly int[] item = new int[Rules.N];
    readonly sbyte[] belt = new sbyte[Rules.N];
    int step, nextId = 1; readonly int snapEvery;
    /// <summary>Event log (append-only).</summary>
    public readonly EventLog Log;
    /// <summary>Snapshots taken so far.</summary>
    public readonly List<Snapshot> Snapshots = new();

    /// <summary>Create a sim; snapEvery = 0 means no snapshots; retain = false drops events (floor).</summary>
    public BagSim(int seed, int snapEvery, bool retain)
    {
        this.snapEvery = snapEvery; Retains = retain; Log = new EventLog(retain);
        for (int i = 0; i < Rules.N; i++) { plaque[i] = Rules.InitPlaque(i, seed); rate[i] = Rules.Rate(i, seed); belt[i] = (sbyte)(Rules.OnBelt(i) ? 1 : 0); }
    }

    /// <summary>Pure-looking facade: (version, intents) -> (version', event range). Only the current version is steppable.</summary>
    public StepResult Step(int version, ReadOnlySpan<int> intents)
    {
        if (version != step) throw new InvalidOperationException("stale version; rebuild from snapshot");
        long first = Log.Count; Step();
        return new StepResult(step, first, (int)(Log.Count - first));
    }

    /// <inheritdoc/>
    public override void Step()
    {
        var p0 = plaque; var p1 = plaqueNext; var nbr = Rules.Nbr; var log = Log; int st = step;
        for (int i = 0; i < Rules.N; i++)
        {
            int o = i * 8;
            int cnt = (p0[nbr[o]] >> 7) + (p0[nbr[o + 1]] >> 7) + (p0[nbr[o + 2]] >> 7) + (p0[nbr[o + 3]] >> 7)
                    + (p0[nbr[o + 4]] >> 7) + (p0[nbr[o + 5]] >> 7) + (p0[nbr[o + 6]] >> 7) + (p0[nbr[o + 7]] >> 7);
            int p = p0[i], np = (p + rate[i] + cnt) & 255;
            p1[i] = np;
            if ((np >> 6) != (p >> 6)) log.Add(st, EventKind.PlaqueCrossed, i, (np >> 6) > (p >> 6) ? 1 : 0, np >> 6);
        }
        plaque = p1; plaqueNext = p0;
        foreach (var row in Rules.BeltRows)
        {
            int baseI = row * Rules.W, head = baseI + Rules.HeadCol;
            if (item[head] != 0) { log.Add(st, EventKind.Consumed, item[head], head, 0); item[head] = 0; }
            for (int col = Rules.HeadCol - 1; col >= Rules.BeltStartCol; col--)
            {
                int i = baseI + col;
                if (item[i] != 0 && item[i + 1] == 0) { log.Add(st, EventKind.Moved, item[i], i, i + 1); item[i + 1] = item[i]; item[i] = 0; }
            }
            int t = baseI + Rules.BeltStartCol;
            if ((st & 1) == 0 && item[t] == 0) { int id = nextId++; item[t] = id; log.Add(st, EventKind.Emitted, id, t, 0); }
        }
        step = st + 1;
        if (snapEvery > 0 && step % snapEvery == 0)
            Snapshots.Add(new Snapshot(step, nextId, ImmutableArray.Create(plaque), ImmutableArray.Create(item)));
    }

    /// <inheritdoc/>
    public override ulong StateHash()
    {
        ulong h = 14695981039346656037UL; h = Rules.Fold(h, step); h = Rules.Fold(h, nextId);
        for (int i = 0; i < Rules.N; i++) { h = Rules.Fold(h, plaque[i]); h = Rules.Fold(h, item[i]); }
        return h;
    }
    /// <inheritdoc/>
    public override (long, ulong, bool) Events()
    {
        if (!Retains) return (Log.Count, 0, false);
        ulong h = 14695981039346656037UL;
        foreach (var e in Log.All()) h = Rules.FoldEvent(h, e.Step, e.Kind, e.A, e.B, e.C);
        return (Log.Count, h, true);
    }
    /// <summary>True when the log keeps all events.</summary>
    public bool Retains { get; }
}
