namespace Pockets.Core.Cosmology.Recipes;

/// <summary>How a node's recipe is structured — for diagnostics, diagrams, and tests.</summary>
public enum RecipeKind
{
    /// <summary>A quadrant's freely-accessible entry (no ingredients — the world start).</summary>
    Root,
    /// <summary>Depth <i>n+1</i> crafted from depth <i>n</i> of the same quadrant chain.</summary>
    WithinZone,
    /// <summary>A quadrant entry gated by cross-zone material(s) — the semi-linearization step.</summary>
    Entry
}

/// <summary>
/// The recipe that unlocks one <see cref="ZoneDepth"/> node. Navigation IS crafting:
/// reaching a wilderness means holding its recipe's ingredients. A within-zone recipe
/// draws its single ingredient from the previous depth of the same quadrant chain
/// (crossing the +/− boundary continuously — Bloom 11's recipe draws from Quiet 10).
/// An entry recipe instead draws from a <b>different</b> quadrant per the tunable
/// cross-zone edges (Gloam 1 ← a Quiet 5 material).
///
/// <para>
/// Roots (each world-start entry) have no recipe and are reachable for free; every
/// other node is the <see cref="Target"/> of exactly one recipe, which keeps the
/// graph orphan-free and acyclic by construction.
/// </para>
/// </summary>
public sealed record DepthRecipe(
    ZoneDepth Target,
    ImmutableArray<Ingredient> Ingredients,
    RecipeKind Kind)
{
    /// <summary>The distinct source nodes whose materials this recipe consumes.</summary>
    public IEnumerable<ZoneDepth> Sources => Ingredients.Select(i => i.Source).Distinct();
}
