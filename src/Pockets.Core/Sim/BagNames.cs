using System.Globalization;

namespace Pockets.Core.Sim;

/// <summary>
/// A stream's bag naming (P1-D8): bag GUIDs are renamed <c>bag#1</c>, <c>bag#2</c> … in order of first sight.
/// Share one instance between text formatters (event lines, pose lines) so they name a bag the same way.
/// </summary>
public sealed class BagNames
{
    private readonly Dictionary<Guid, int> _bags = new();

    /// <summary>A bag's stream name, assigning the next number on first sight.</summary>
    public string Name(Guid id)
    {
        if (!_bags.TryGetValue(id, out var n))
            _bags[id] = n = _bags.Count + 1;
        return "bag#" + n.ToString(CultureInfo.InvariantCulture);
    }
}
