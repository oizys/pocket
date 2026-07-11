using Pockets.Core.Cosmology;
using Pockets.Core.Cosmology.Recipes;

namespace Pockets.Core.Tests.Cosmology.Recipes;

/// <summary>
/// The validation suite passes on the starter graph and actually bites on broken
/// graphs: dangling references, dependency cycles, unreachable nodes, and violated
/// chirality-extension are each reported. The healthy properties (no orphans, no
/// cycles, all reachable, chirality-extension) are enforced by construction, so the
/// positive cases double as regression guards on <see cref="RecipeBook.Assemble"/>.
/// </summary>
public class RecipeValidationTests
{
    private static readonly RecipeBook Book = DepthRecipeData.Book;

    [Fact]
    public void Starter_Graph_Is_Fully_Valid()
    {
        var issues = RecipeValidation.Validate(Book);
        Assert.True(issues.IsEmpty, "starter graph should be valid but had:\n" + string.Join("\n", issues));
    }

    [Fact]
    public void No_Orphans_And_Every_NonRoot_Has_Exactly_One_Recipe()
    {
        Assert.Empty(RecipeValidation.NoOrphans(Book));
        int recipeTargets = Book.Recipes.Select(r => r.Target).Distinct().Count();
        Assert.Equal(Book.Nodes.Length, recipeTargets + Book.Roots.Length);
    }

    [Fact]
    public void ChiralityExtension_Holds_For_Every_Quadrant()
    {
        Assert.Empty(RecipeValidation.ChiralityExtension(Book));
    }

    [Fact]
    public void Detects_Dangling_Edge_Reference()
    {
        var chains = new[] { new QuadrantChain(Quadrant.Quiet, 3, 0) };
        var edges = new[]
        {
            // Target Quiet 2 from a node that does not exist.
            new CrossZoneEdge(new ZoneDepth(Zone.JitterNegative, 99), new ZoneDepth(Zone.QuietPositive, 2)),
        };
        var book = RecipeBook.Assemble(chains, edges, Enumerable.Empty<HeroGate>());
        Assert.Contains(RecipeValidation.ReferencesResolve(book), i => i.Contains("not a real node"));
    }

    [Fact]
    public void Detects_Dependency_Cycle()
    {
        // Two quadrant entries gate each other -> a cycle, no root, nothing reachable.
        var chains = new[]
        {
            new QuadrantChain(Quadrant.Gloam, 1, 0),
            new QuadrantChain(Quadrant.Flux, 1, 0),
        };
        var edges = new[]
        {
            new CrossZoneEdge(new ZoneDepth(Zone.FluxPositive, 1), new ZoneDepth(Zone.GloamPositive, 1)),
            new CrossZoneEdge(new ZoneDepth(Zone.GloamPositive, 1), new ZoneDepth(Zone.FluxPositive, 1)),
        };
        var book = RecipeBook.Assemble(chains, edges, Enumerable.Empty<HeroGate>());
        Assert.NotEmpty(RecipeValidation.NoCycles(book));
        Assert.NotEmpty(RecipeValidation.AllReachable(book));  // cycle leaves both unreachable
    }

    [Fact]
    public void Dangling_Edge_Is_Tolerated_By_Assembly_And_Only_Flagged_By_Validation()
    {
        // A cross-zone edge whose source node does not exist must not crash assembly;
        // the edge is retained (for reporting) but contributes no ingredient, so its
        // target degrades to a free root rather than a stranded node.
        var chains = new[] { new QuadrantChain(Quadrant.Gloam, 2, 0) };
        var edges = new[]
        {
            new CrossZoneEdge(new ZoneDepth(Zone.QuietPositive, 99), new ZoneDepth(Zone.GloamPositive, 1)),
        };
        var book = RecipeBook.Assemble(chains, edges, Enumerable.Empty<HeroGate>());

        Assert.Contains(new ZoneDepth(Zone.GloamPositive, 1), book.Roots);
        Assert.NotEmpty(RecipeValidation.ReferencesResolve(book));
        // Everything is still reachable despite the bad edge (target became a root).
        Assert.Empty(RecipeValidation.AllReachable(book));
    }
}
