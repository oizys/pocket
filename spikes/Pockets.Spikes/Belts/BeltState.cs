using System.Text;

namespace Pockets.Spikes.Belts;

/// <summary>A grid coordinate (X right, Y down).</summary>
public readonly record struct Pos(int X, int Y)
{
    /// <summary>Compact "(x,y)" form.</summary>
    public override string ToString() => $"({X},{Y})";
}

/// <summary>What occupies a grid cell.</summary>
public enum CellKind : byte { None, Belt, Sink, Source }

/// <summary>Direction helpers. Index order is the fixed tie-break order: 0=N, 1=E, 2=S, 3=W.</summary>
public static class Dirs
{
    /// <summary>X deltas for N,E,S,W.</summary>
    public static readonly int[] Dx = { 0, 1, 0, -1 };
    /// <summary>Y deltas for N,E,S,W.</summary>
    public static readonly int[] Dy = { -1, 0, 1, 0 };
    /// <summary>Belt glyphs for N,E,S,W.</summary>
    public const string BeltGlyphs = "▲▶▼◀";
    /// <summary>Source glyphs for N,E,S,W.</summary>
    public const string SourceGlyphs = "⇑⇒⇓⇐";
    /// <summary>The opposite direction index.</summary>
    public static int Opposite(int d) => (d + 2) & 3;
}

/// <summary>Plain data used to build a <see cref="BeltState"/> cell-by-cell (order of specs is irrelevant).</summary>
public readonly record struct CellSpec(int X, int Y, CellKind Kind, int Dir, char Item, int Period);

/// <summary>
/// Belt world state (treated as immutable by the step functions: they clone).
/// ASCII form is TWO LAYERS, rows separated by '/' (or newline):
///   belts layer: '.' nothing; ▲▶▼◀ belt; 'K' sink; ⇑⇒⇓⇐ source (arrow = out direction).
///   items layer: '.' empty; a letter = item sitting on that belt; on a SOURCE cell the letter is the item it emits.
/// Example: Parse("⇒▶▶▶K", "A.....") is a saturated source 'A' feeding a 3-belt line into a sink.
/// Render() is the single-layer view used in traces: items show as their letter, empty belts as arrows.
/// Per-cell integer memory: Period (source emit period / sink consume period, fires when Tick % Period == 0).
/// Merges are stateless (fixed side priority, see BeltSim), so there is no per-merge memory.
/// </summary>
public sealed class BeltState
{
    /// <summary>Grid width.</summary>
    public int W { get; }
    /// <summary>Grid height.</summary>
    public int H { get; }
    /// <summary>Steps executed so far.</summary>
    public int Tick { get; init; }
    /// <summary>Cell kinds, row-major.</summary>
    public CellKind[] Kind { get; }
    /// <summary>Direction index (N,E,S,W) of belts and sources.</summary>
    public int[] Dir { get; }
    /// <summary>Item letter on a belt ('\0' = empty) or emitted letter on a source.</summary>
    public char[] Item { get; }
    /// <summary>Source emit period / sink consume period (>= 1).</summary>
    public int[] Period { get; }
    private BeltState(int w, int h, int tick, CellKind[] k, int[] d, char[] i, int[] p)
    { W = w; H = h; Tick = tick; Kind = k; Dir = d; Item = i; Period = p; }

    /// <summary>Create an empty world.</summary>
    public static BeltState Empty(int w, int h) => new(w, h, 0, new CellKind[w * h], new int[w * h],
        new char[w * h], Enumerable.Repeat(1, w * h).ToArray());

    /// <summary>Deep copy, optionally with a new tick.</summary>
    public BeltState Clone(int? tick = null) => new(W, H, tick ?? Tick, (CellKind[])Kind.Clone(), (int[])Dir.Clone(),
        (char[])Item.Clone(), (int[])Period.Clone());

    /// <summary>Copy with a different tick counter (phase of source/sink periods).</summary>
    public BeltState WithTick(int tick) => Clone(tick);

    /// <summary>Copy with the period of the cell at (x,y) changed.</summary>
    public BeltState WithPeriod(int x, int y, int period)
    {
        var c = Clone();
        c.Period[y * W + x] = period;
        return c;
    }

    /// <summary>Build from specs in any order; unspecified cells are empty.</summary>
    public static BeltState FromSpecs(int w, int h, IEnumerable<CellSpec> specs) =>
        specs.OrderBy(s => s.Y).ThenBy(s => s.X).Aggregate(Empty(w, h), (st, s) =>
        {
            int i = s.Y * w + s.X;
            st.Kind[i] = s.Kind; st.Dir[i] = s.Dir; st.Item[i] = s.Item; st.Period[i] = s.Period;
            return st;
        });

    /// <summary>Parse the two-layer ASCII form (see class docs). Items layer may be null (all empty).</summary>
    public static BeltState Parse(string belts, string? items = null)
    {
        var br = belts.Split('/', '\n');
        var ir = (items ?? string.Join("/", br.Select(r => new string('.', r.Length)))).Split('/', '\n');
        if (br.Length != ir.Length || br.Select((r, y) => r.Length != ir[y].Length).Any(b => b))
            throw new ArgumentException("belts/items layers differ in shape");
        var st = Empty(br[0].Length, br.Length);
        for (int y = 0; y < br.Length; y++)
            for (int x = 0; x < br[y].Length; x++)
            {
                char b = br[y][x], it = ir[y][x];
                int i = y * st.W + x;
                int bd = Dirs.BeltGlyphs.IndexOf(b), sd = Dirs.SourceGlyphs.IndexOf(b);
                if (bd >= 0) { st.Kind[i] = CellKind.Belt; st.Dir[i] = bd; st.Item[i] = it == '.' ? '\0' : it; }
                else if (sd >= 0) { st.Kind[i] = CellKind.Source; st.Dir[i] = sd; st.Item[i] = it == '.' ? 'S' : it; }
                else if (b == 'K') st.Kind[i] = CellKind.Sink;
                else if (b == '.') { if (it != '.') throw new ArgumentException($"item on empty cell ({x},{y})"); }
                else throw new ArgumentException($"bad belt glyph '{b}'");
            }
        return st;
    }

    /// <summary>Single-layer view: rows joined by '|'. Items as letters, empty belts as arrows, 'K' sinks, source arrows, '.' none.</summary>
    public string Render() => string.Join("|", Enumerable.Range(0, H).Select(y =>
        new string(Enumerable.Range(0, W).Select(x => Glyph(y * W + x)).ToArray())));

    private char Glyph(int i) => Kind[i] switch
    {
        CellKind.Belt => Item[i] != '\0' ? Item[i] : Dirs.BeltGlyphs[Dir[i]],
        CellKind.Source => Dirs.SourceGlyphs[Dir[i]],
        CellKind.Sink => 'K',
        _ => '.',
    };

    /// <summary>Number of items currently riding belts.</summary>
    public int ItemCount() => Enumerable.Range(0, Kind.Length).Count(i => Kind[i] == CellKind.Belt && Item[i] != '\0');

    /// <summary>Cell index to coordinate.</summary>
    public Pos PosOf(int i) => new(i % W, i / W);
}

/// <summary>Base of everything a belt step can report.</summary>
public abstract record BeltEvent;
/// <summary>An item advanced one cell.</summary>
public sealed record Moved(char Item, Pos From, Pos To, string Cause) : BeltEvent;
/// <summary>A sink removed an item.</summary>
public sealed record Consumed(char Item, Pos At) : BeltEvent;
/// <summary>A source created an item at the cell it points to.</summary>
public sealed record Emitted(char Item, Pos At) : BeltEvent;
/// <summary>An item (or firing source) wanted to move but could not. Reason: "lost-merge", "full", or "no-target".</summary>
public sealed record Blocked(Pos From, Pos To, string Reason) : BeltEvent;

/// <summary>New state plus the ordered (by cell index) events of one step.</summary>
public sealed record StepResult(BeltState State, IReadOnlyList<BeltEvent> Events);
