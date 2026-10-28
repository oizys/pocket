using System.Globalization;
using Pockets.Core.Sim;

namespace Pockets.Realization;

/// <summary>
/// The canonical one-line text of a <see cref="Pose"/>:
/// <c>entity=Body(B) bag=bag#1 col=2 row=1 subX=3 subY=-4 height=- facing=S</c>, invariant culture, null as <c>-</c>.
/// A stack key's cell is written <c>bag#1[4]</c>. Sharing one <see cref="BagNames"/> with the run's
/// <see cref="EventText"/> makes event and pose lines name a bag the same way; naming order is first
/// appearance across the line, left to right.
/// </summary>
public sealed class PoseText
{
    private readonly BagNames _names;

    /// <summary>A pose formatter with its own fresh bag naming.</summary>
    public PoseText() : this(new BagNames()) { }

    /// <summary>A pose formatter that names bags through <paramref name="names"/>.</summary>
    public PoseText(BagNames names) => _names = names;

    /// <summary>The canonical lines of <paramref name="poses"/>, naming bags from <c>bag#1</c> in a fresh stream.</summary>
    public static ImmutableArray<string> Lines(IEnumerable<Pose> poses)
    {
        var text = new PoseText();
        return poses.Select(text.Line).ToImmutableArray();
    }

    /// <summary>The canonical line of one pose, continuing this formatter's bag naming.</summary>
    public string Line(Pose p)
    {
        var entity = Entity(p.Entity);
        var bag = _names.Name(p.Bag);
        return $"entity={entity} bag={bag} col={Int(p.CellCol)} row={Int(p.CellRow)} " +
               $"subX={Int(p.SubX)} subY={Int(p.SubY)} " +
               $"height={(p.Height is { } h ? Int(h) : "-")} facing={(p.Facing is { } f ? f.ToString() : "-")}";
    }

    /// <summary>An entity key as <c>Body(B)</c> or <c>Stack(bag#1[4])</c>.</summary>
    private string Entity(EntityKey key) => key switch
    {
        EntityKey.Body b => $"Body({b.Loc})",
        EntityKey.Cursor c => $"Cursor({c.Loc})",
        EntityKey.Stack s => $"Stack({_names.Name(s.At.Bag)}[{Int(s.At.Index)}])",
        _ => "-"
    };

    /// <summary>An integer in invariant culture.</summary>
    private static string Int(int n) => n.ToString(CultureInfo.InvariantCulture);
}
