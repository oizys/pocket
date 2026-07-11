namespace Pockets.Core.Cosmology.Recipes;

/// <summary>
/// The fully-assembled progression graph: every <see cref="ZoneDepth"/> node, the
/// one recipe that unlocks it (within-zone or cross-zone entry), the tunable
/// cross-zone edges, and the hero-piece gates. Built from a set of
/// <see cref="QuadrantChain"/>s plus edges/gates — the game and the design tools
/// both consume this immutable structure; nothing here duplicates the
/// <see cref="EntropyMatrix"/> SSOT, it derives from it.
/// </summary>
public sealed class RecipeBook
{
    private readonly ImmutableDictionary<ZoneDepth, Material> _materials;
    private readonly ImmutableDictionary<ZoneDepth, DepthRecipe> _recipesByTarget;

    /// <summary>The quadrant chains this book was assembled from.</summary>
    public ImmutableArray<QuadrantChain> Chains { get; }

    /// <summary>All nodes, ascending by quadrant order then depth.</summary>
    public ImmutableArray<ZoneDepth> Nodes { get; }

    /// <summary>The tunable cross-zone (semi-linearization) edges.</summary>
    public ImmutableArray<CrossZoneEdge> Edges { get; }

    /// <summary>The hero-piece gates.</summary>
    public ImmutableArray<HeroGate> Gates { get; }

    /// <summary>Nodes with no recipe — the world's freely-accessible starts.</summary>
    public ImmutableArray<ZoneDepth> Roots { get; }

    /// <summary>All node-unlocking recipes (excludes roots, which have none).</summary>
    public IEnumerable<DepthRecipe> Recipes => _recipesByTarget.Values;

    /// <summary>The signature material harvested at a node.</summary>
    public Material MaterialAt(ZoneDepth node) => _materials[node];

    /// <summary>The recipe that unlocks a node, or null for a root.</summary>
    public DepthRecipe? RecipeFor(ZoneDepth node) =>
        _recipesByTarget.TryGetValue(node, out var r) ? r : null;

    /// <summary>The chain for a quadrant.</summary>
    public QuadrantChain Chain(Quadrant quadrant) =>
        Chains.First(c => c.Quadrant == quadrant);

    private RecipeBook(
        ImmutableArray<QuadrantChain> chains,
        ImmutableArray<ZoneDepth> nodes,
        ImmutableDictionary<ZoneDepth, Material> materials,
        ImmutableDictionary<ZoneDepth, DepthRecipe> recipes,
        ImmutableArray<ZoneDepth> roots,
        ImmutableArray<CrossZoneEdge> edges,
        ImmutableArray<HeroGate> gates)
    {
        Chains = chains;
        Nodes = nodes;
        _materials = materials;
        _recipesByTarget = recipes;
        Roots = roots;
        Edges = edges;
        Gates = gates;
    }

    /// <summary>
    /// Assemble a book. Each node's recipe is derived structurally so the graph is
    /// orphan-free and acyclic by construction: depth <i>d≥2</i> draws its spine
    /// ingredient from depth <i>d-1</i> of the same chain; a depth-1 entry draws
    /// only from the cross-zone edges targeting it; a depth-1 entry with no incoming
    /// edge is a Root (free start). Edges targeting a mid-depth node are layered on
    /// top of that node's within-zone spine.
    /// </summary>
    public static RecipeBook Assemble(
        IEnumerable<QuadrantChain> chains,
        IEnumerable<CrossZoneEdge> edges,
        IEnumerable<HeroGate> gates)
    {
        var chainArray = chains.ToImmutableArray();
        var edgeArray = edges.ToImmutableArray();
        var gateArray = gates.ToImmutableArray();

        var nodes = chainArray.SelectMany(c => c.Nodes).ToImmutableArray();
        var materials = nodes.ToImmutableDictionary(n => n, QuadrantChain.MaterialAt);

        // Incoming cross-zone edges per target node.
        var edgesByTarget = edgeArray
            .GroupBy(e => e.Target)
            .ToImmutableDictionary(g => g.Key, g => g.ToImmutableArray());

        var recipes = ImmutableDictionary.CreateBuilder<ZoneDepth, DepthRecipe>();
        var roots = ImmutableArray.CreateBuilder<ZoneDepth>();

        foreach (var chain in chainArray)
        {
            for (int d = 1; d <= chain.TotalDepths; d++)
            {
                var node = chain.NodeAt(d);
                // Dangling edge sources are tolerated here and flagged by
                // RecipeValidation.ReferencesResolve rather than crashing assembly.
                var crossIngredients = edgesByTarget.TryGetValue(node, out var incoming)
                    ? incoming
                        .Where(e => materials.ContainsKey(e.Source))
                        .Select(e => new Ingredient(materials[e.Source], e.Quantity))
                    : Enumerable.Empty<Ingredient>();

                if (d == 1)
                {
                    var entryIngredients = crossIngredients.ToImmutableArray();
                    if (entryIngredients.IsEmpty)
                        roots.Add(node);                       // free world-start
                    else
                        recipes[node] = new DepthRecipe(node, entryIngredients, RecipeKind.Entry);
                }
                else
                {
                    // Within-zone spine: the previous chain depth, plus any cross edges.
                    var spine = new Ingredient(materials[chain.NodeAt(d - 1)], 1);
                    var all = new[] { spine }.Concat(crossIngredients).ToImmutableArray();
                    recipes[node] = new DepthRecipe(node, all, RecipeKind.WithinZone);
                }
            }
        }

        return new RecipeBook(
            chainArray, nodes, materials, recipes.ToImmutable(),
            roots.ToImmutable(), edgeArray, gateArray);
    }
}
