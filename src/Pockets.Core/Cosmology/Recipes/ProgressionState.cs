namespace Pockets.Core.Cosmology.Recipes;

/// <summary>
/// A snapshot of the player's progress for reachability queries: the set of
/// wildernesses they have <see cref="Reached"/>. Materials are harvested at reached
/// nodes, so "materials held" = the signature materials of the reached set. Every
/// recipe in the <see cref="RecipeBook"/> is considered known (the graph is the full
/// designed set); what gates progress is which ingredient <i>sources</i> you can
/// currently harvest. This is a pure value — the future radar minimap reads it.
/// </summary>
public sealed record ProgressionState(ImmutableHashSet<ZoneDepth> Reached)
{
    /// <summary>The world-start state: every root node reached, nothing else.</summary>
    public static ProgressionState Start(RecipeBook book) =>
        new(book.Roots.ToImmutableHashSet());

    /// <summary>A state that has reached exactly the given nodes.</summary>
    public static ProgressionState Of(IEnumerable<ZoneDepth> nodes) =>
        new(nodes.ToImmutableHashSet());

    /// <summary>Whether a node has been reached (its material is therefore held).</summary>
    public bool Has(ZoneDepth node) => Reached.Contains(node);

    /// <summary>The state extended by reaching one more node.</summary>
    public ProgressionState With(ZoneDepth node) => new(Reached.Add(node));
}
