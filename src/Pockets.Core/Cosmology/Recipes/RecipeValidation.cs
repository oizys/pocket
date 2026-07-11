namespace Pockets.Core.Cosmology.Recipes;

/// <summary>
/// Static integrity checks for a <see cref="RecipeBook"/>: the properties a depth
/// graph must hold for progression to be sound — no orphans, no cycles, everything
/// reachable, and the chirality-extension invariant. Each check returns human-readable
/// issue strings (empty = healthy) so both the test suite and the CLI can report them.
/// </summary>
public static class RecipeValidation
{
    /// <summary>Run every check; an empty result means the book is valid.</summary>
    public static ImmutableArray<string> Validate(RecipeBook book)
    {
        var issues = ImmutableArray.CreateBuilder<string>();
        issues.AddRange(ReferencesResolve(book));
        issues.AddRange(NoOrphans(book));
        issues.AddRange(NoCycles(book));
        issues.AddRange(AllReachable(book));
        issues.AddRange(ChiralityExtension(book));
        return issues.ToImmutable();
    }

    /// <summary>Every edge/gate/recipe ingredient must reference a node that exists.</summary>
    public static IEnumerable<string> ReferencesResolve(RecipeBook book)
    {
        var nodes = book.Nodes.ToImmutableHashSet();
        foreach (var e in book.Edges)
        {
            if (!nodes.Contains(e.Source))
                yield return $"edge source {e.Source.Key} is not a real node";
            if (!nodes.Contains(e.Target))
                yield return $"edge target {e.Target.Key} is not a real node";
        }
        foreach (var g in book.Gates)
            foreach (var s in g.Sources)
                if (!nodes.Contains(s))
                    yield return $"hero gate '{g.Id}' requires missing node {s.Key}";
        foreach (var r in book.Recipes)
            foreach (var s in r.Sources)
                if (!nodes.Contains(s))
                    yield return $"recipe for {r.Target.Key} requires missing node {s.Key}";
    }

    /// <summary>
    /// No orphan depths: every node is either a root or the target of exactly one
    /// recipe, and no chain has a gap in its 1..N depth sequence.
    /// </summary>
    public static IEnumerable<string> NoOrphans(RecipeBook book)
    {
        var roots = book.Roots.ToImmutableHashSet();
        foreach (var n in book.Nodes)
        {
            bool hasRecipe = book.RecipeFor(n) is not null;
            if (!hasRecipe && !roots.Contains(n))
                yield return $"orphan node {n.Key}: no recipe and not a root";
            if (hasRecipe && roots.Contains(n))
                yield return $"node {n.Key} is both a root and a recipe target";
        }

        foreach (var chain in book.Chains)
        {
            var depths = chain.Nodes.Select(n => n.Depth).OrderBy(d => d).ToArray();
            var expected = Enumerable.Range(1, chain.TotalDepths).ToArray();
            if (!depths.SequenceEqual(expected))
                yield return $"{chain.Quadrant} chain has a depth gap: [{string.Join(",", depths)}]";
        }
    }

    /// <summary>No dependency cycles: the recipe-dependency digraph is a DAG.</summary>
    public static IEnumerable<string> NoCycles(RecipeBook book)
    {
        // Edge node -> each source it depends on. DFS with a recursion stack finds a
        // back-edge (cycle). Colors: 0 unvisited, 1 on-stack, 2 done.
        var color = new Dictionary<ZoneDepth, int>();
        var issues = new List<string>();

        IEnumerable<ZoneDepth> Deps(ZoneDepth n) =>
            book.RecipeFor(n)?.Sources ?? Enumerable.Empty<ZoneDepth>();

        bool Visit(ZoneDepth n)
        {
            color[n] = 1;
            foreach (var dep in Deps(n))
            {
                int c = color.GetValueOrDefault(dep, 0);
                if (c == 1) { issues.Add($"dependency cycle through {n.Key} -> {dep.Key}"); return true; }
                if (c == 0 && Visit(dep)) return true;
            }
            color[n] = 2;
            return false;
        }

        foreach (var n in book.Nodes)
            if (color.GetValueOrDefault(n, 0) == 0 && Visit(n))
                break;

        return issues;
    }

    /// <summary>Every zone×depth is reachable from the world-start state.</summary>
    public static IEnumerable<string> AllReachable(RecipeBook book)
    {
        var reachable = Reachability.Reachable(book, ProgressionState.Start(book));
        foreach (var n in book.Nodes)
            if (!reachable.Contains(n))
                yield return $"unreachable node {n.Key}";
    }

    /// <summary>
    /// The chirality-extension invariant: in every quadrant the "−" zone's chain
    /// <b>continues</b> the "+" zone's numbering (min− = max+ + 1), and the recipe at
    /// that boundary draws its spine ingredient from the "+" zone's last depth
    /// (Bloom 11 ← Quiet 10).
    /// </summary>
    public static IEnumerable<string> ChiralityExtension(RecipeBook book)
    {
        foreach (var chain in book.Chains)
        {
            if (chain.NegativeDepths == 0) continue;
            var posDepths = chain.Nodes.Where(n => n.Aspect == Aspect.Positive).Select(n => n.Depth);
            var negDepths = chain.Nodes.Where(n => n.Aspect == Aspect.Negative).Select(n => n.Depth);
            int maxPos = posDepths.Max();
            int minNeg = negDepths.Min();
            if (minNeg != maxPos + 1)
            {
                yield return $"{chain.Quadrant}: negative starts at {minNeg}, expected {maxPos + 1}";
                continue;
            }

            var boundary = chain.NodeAt(minNeg);
            var recipe = book.RecipeFor(boundary);
            var expectedSource = new ZoneDepth(chain.PositiveZone, maxPos);
            if (recipe is null || !recipe.Sources.Contains(expectedSource))
                yield return
                    $"{chain.Quadrant}: boundary recipe {boundary.Key} does not extend from {expectedSource.Key}";
        }
    }
}
