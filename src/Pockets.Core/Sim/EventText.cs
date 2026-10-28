using System.Globalization;
using System.Reflection;
using System.Text;
using Pockets.Core.Models;

namespace Pockets.Core.Sim;

/// <summary>
/// The canonical one-line text of a <see cref="SimEvent"/> (Slice 1.5; the format Slice 1.7's event golden
/// and cross-driver diff will use): <c>step seq Kind name=value …</c>, fields in the record's declared order,
/// invariant culture. Bag GUIDs are renamed <c>bag#1</c>, <c>bag#2</c> … in order of first appearance in the
/// stream (P1-D8), so the text is identical across processes and drivers; in-memory events keep their GUIDs.
/// One instance names one stream: keep it for the whole run so a bag keeps its name.
/// <para>
/// Value forms: a cell is <c>bag#1[4]</c>, a position <c>(r,c)</c>, an item its quoted name, a placed stack
/// <c>3×"Rock"@bag#1[4]</c>, an array <c>[a, b]</c>, null <c>-</c>, an intent <c>Grab(B)</c>.
/// </para>
/// </summary>
public sealed class EventText
{
    private readonly Dictionary<Guid, int> _bags = new();

    /// <summary>The canonical lines of <paramref name="events"/>, naming bags from <c>bag#1</c> in a fresh stream.</summary>
    public static ImmutableArray<string> Lines(IEnumerable<SimEvent> events)
    {
        var text = new EventText();
        return events.Select(text.Line).ToImmutableArray();
    }

    /// <summary>The canonical line of one event, continuing this stream's bag naming.</summary>
    public string Line(SimEvent e)
    {
        var sb = new StringBuilder();
        sb.Append(e.Step.ToString(CultureInfo.InvariantCulture)).Append(' ')
          .Append(e.Seq.ToString(CultureInfo.InvariantCulture)).Append(' ')
          .Append(e.GetType().Name);
        foreach (var (name, value) in Fields(e).Where(f => f.Name is not ("Step" or "Seq")))
            sb.Append(' ').Append(Camel(name)).Append('=').Append(Format(value));
        return sb.ToString();
    }

    /// <summary>A bag's stream name, assigning the next number on first sight.</summary>
    private string Bag(Guid id)
    {
        if (!_bags.TryGetValue(id, out var n))
            _bags[id] = n = _bags.Count + 1;
        return "bag#" + n.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>One payload value in its canonical form (see the class summary).</summary>
    private string Format(object? value) => value switch
    {
        null => "-",
        Guid g => Bag(g),
        CellRef c => $"{Bag(c.Bag)}[{c.Index.ToString(CultureInfo.InvariantCulture)}]",
        Position p => $"({p.Row.ToString(CultureInfo.InvariantCulture)},{p.Col.ToString(CultureInfo.InvariantCulture)})",
        ItemType t => Quote(t.Name),
        Placed p => $"{p.Count.ToString(CultureInfo.InvariantCulture)}×{Quote(p.Item.Name)}@{Format(p.Cell)}",
        Intent i => Compact(i),
        string s => Quote(s),
        bool b => b ? "true" : "false",
        int n => n.ToString(CultureInfo.InvariantCulture),
        TimeSpan t => t.ToString("c", CultureInfo.InvariantCulture),
        Enum en => en.ToString(),
        ImmutableArray<int> a => List(a.IsDefault ? Enumerable.Empty<object?>() : a.Cast<object?>()),
        ImmutableArray<CellRef> a => List(a.IsDefault ? Enumerable.Empty<object?>() : a.Cast<object?>()),
        ImmutableArray<Placed> a => List(a.IsDefault ? Enumerable.Empty<object?>() : a.Cast<object?>()),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? "-"
    };

    /// <summary><c>[a, b, …]</c>.</summary>
    private string List(IEnumerable<object?> items) => "[" + string.Join(", ", items.Select(Format)) + "]";

    /// <summary>An intent as <c>Kind(arg, …)</c>, or bare <c>Kind</c> when it has no fields.</summary>
    private string Compact(Intent intent)
    {
        var args = Fields(intent).Select(f => Format(f.Value)).ToList();
        var name = intent.GetType().Name;
        return args.Count == 0 ? name : $"{name}({string.Join(", ", args)})";
    }

    /// <summary>A double-quoted string, with quotes and backslashes escaped.</summary>
    private static string Quote(string s) => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    /// <summary>A field name in lower camel case (<c>FacilityBagId</c> → <c>facilityBagId</c>).</summary>
    private static string Camel(string name) => char.ToLowerInvariant(name[0]) + name[1..];

    /// <summary>
    /// A record's fields in declared order: the parameters of its primary (widest public) constructor, read
    /// back through the same-named properties. Reflection's property order is not guaranteed; the
    /// constructor's is.
    /// </summary>
    private static IEnumerable<(string Name, object? Value)> Fields(object record)
    {
        var type = record.GetType();
        var ctor = type.GetConstructors(BindingFlags.Public | BindingFlags.Instance)
            .OrderByDescending(c => c.GetParameters().Length)
            .FirstOrDefault();
        if (ctor is null)
            yield break;
        foreach (var p in ctor.GetParameters())
            yield return (p.Name!, type.GetProperty(p.Name!)!.GetValue(record));
    }
}
