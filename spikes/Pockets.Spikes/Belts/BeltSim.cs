using System.Text;

namespace Pockets.Spikes.Belts;

/// <summary>
/// Pure belt step functions (propose -> resolve -> commit).
/// NaiveStep: pure last-frame CA (Rule 184): an item enters a cell only if that cell was empty in the previous frame.
/// ChainStep: an item enters a cell if it is empty OR its occupant moves out this same step; resolved through memoized
///   recursion Vacates(cell). CYCLE CHOICE: a hit on a cell already on the recursion stack counts as "not vacating", so a
///   fully saturated ring stalls (no cell can start the chain); a ring with at least one gap rotates.
/// MERGE TIE-BREAK: contenders into a target cell are identified by the SIDE they sit on (N,E,S,W). The target keeps
///   LastWinner; the next contested decision picks the first contender at or after side (LastWinner+1) cyclically
///   (first decision: fixed order N,E,S,W). Memory is only updated when a contest (>=2 contenders) is actually accepted.
///   Sources are ordinary contenders. Nothing depends on dictionary order or on listing order of cells.
/// </summary>
public static class BeltSim
{
    /// <summary>Rule-184 style step: moves only into cells empty in the previous frame.</summary>
    public static StepResult NaiveStep(BeltState s) => Resolve(s, chain: false);

    /// <summary>Chain-resolving step: moves into cells that are empty or being vacated this step.</summary>
    public static StepResult ChainStep(BeltState s) => Resolve(s, chain: true);

    /// <summary>Conservation check: items(after) == items(before) + emitted - consumed.</summary>
    public static bool Conserved(BeltState before, BeltState after, IEnumerable<BeltEvent> events) =>
        after.ItemCount() == before.ItemCount() + events.OfType<Emitted>().Count() - events.OfType<Consumed>().Count();

    private static int Neighbor(BeltState s, int c, int d)
    {
        int x = c % s.W + Dirs.Dx[d], y = c / s.W + Dirs.Dy[d];
        return x < 0 || y < 0 || x >= s.W || y >= s.H ? -1 : y * s.W + x;
    }

    private static StepResult Resolve(BeltState s, bool chain)
    {
        int n = s.W * s.H;
        var next = Enumerable.Range(0, n)
            .Select(c => s.Kind[c] is CellKind.Belt or CellKind.Source ? Neighbor(s, c, s.Dir[c]) : -1).ToArray();
        bool Ready(int c) => s.Tick % s.Period[c] == 0;
        bool Wants(int c) => s.Kind[c] switch
        {
            CellKind.Belt => s.Item[c] != '\0',
            CellKind.Source => Ready(c),
            _ => false,
        };

        // contenders of target t: sides d (N,E,S,W) whose neighbour wants to move into t
        var contMemo = new int[n][];
        int[] Contenders(int t) => contMemo[t] ??= Enumerable.Range(0, 4)
            .Where(d => { int nb = Neighbor(s, t, d); return nb >= 0 && next[nb] == t && Wants(nb); }).ToArray();

        int Winner(int t)
        {
            var sides = Contenders(t);
            if (sides.Length == 0) return -1;
            if (sides.Length == 1) return sides[0];
            int start = (s.LastWinner[t] + 1) & 3;   // LastWinner -1 => start 0 => fixed order N,E,S,W
            return Enumerable.Range(0, 4).Select(i => (start + i) & 3).First(sides.Contains);
        }

        var memo = new byte[n]; // 0 unknown, 1 on stack, 2 false, 3 true
        bool Accept(int t) => s.Kind[t] switch
        {
            CellKind.Belt => s.Item[t] == '\0' || (chain && Moves(t)),
            CellKind.Sink => Ready(t),
            _ => false,
        };
        bool Moves(int c)
        {
            if (memo[c] == 3) return true;
            if (memo[c] != 0) return false; // decided false, or on-stack (cycle) => not vacating
            memo[c] = 1;
            int t = next[c];
            bool r = t >= 0 && Winner(t) == Dirs.Opposite(s.Dir[c]) && Accept(t);
            memo[c] = (byte)(r ? 3 : 2);
            return r;
        }

        var events = new List<BeltEvent>();
        var after = s.Clone(s.Tick + 1);
        var placements = new List<(int cell, char item)>();
        for (int c = 0; c < n; c++)
        {
            if (!Wants(c)) continue;
            bool isSource = s.Kind[c] == CellKind.Source;
            int t = next[c];
            char item = s.Item[c];
            var from = s.PosOf(c);
            if (t < 0 || s.Kind[t] is CellKind.None or CellKind.Source)
            {
                events.Add(new Blocked(from, new Pos(from.X + Dirs.Dx[s.Dir[c]], from.Y + Dirs.Dy[s.Dir[c]]), "no-target"));
                continue;
            }
            var to = s.PosOf(t);
            if (Moves(c))
            {
                if (!isSource) after.Item[c] = '\0';
                if (s.Kind[t] == CellKind.Sink) events.Add(new Consumed(item, to));
                else
                {
                    placements.Add((t, item));
                    events.Add(isSource ? new Emitted(item, to) : new Moved(item, from, to, "belt"));
                }
            }
            else
            {
                bool lost = Winner(t) != Dirs.Opposite(s.Dir[c]) && Accept(t);
                events.Add(new Blocked(from, to, lost ? "lost-merge" : "full"));
            }
        }
        foreach (var (cell, item) in placements) after.Item[cell] = item;
        for (int t = 0; t < n; t++)
            if (s.Kind[t] is CellKind.Belt or CellKind.Sink && Contenders(t).Length >= 2 && Accept(t))
                after.LastWinner[t] = Winner(t);
        return new StepResult(after, events);
    }
}

/// <summary>ASCII trace helper: one line per step, "tNN &lt;render&gt; out=&lt;cumulative consumed&gt;".</summary>
public static class BeltTrace
{
    /// <summary>Run <paramref name="steps"/> steps (plus the initial frame) and return the lines joined by '\n'.
    /// <paramref name="startLabel"/> overrides the first line's tNN label (default 0).</summary>
    public static string Run(BeltState init, int steps, Func<BeltState, StepResult> step, int startLabel = 0)
    {
        var sb = new List<string>();
        var cur = init;
        int out_ = 0;
        sb.Add(Line(startLabel, cur, out_));
        for (int i = 1; i <= steps; i++)
        {
            var r = step(cur);
            out_ += r.Events.OfType<Consumed>().Count();
            cur = r.State;
            sb.Add(Line(startLabel + i, cur, out_));
        }
        return string.Join("\n", sb);
    }

    private static string Line(int t, BeltState s, int outCount) => $"t{t:D2} {s.Render()} out={outCount}";
}
