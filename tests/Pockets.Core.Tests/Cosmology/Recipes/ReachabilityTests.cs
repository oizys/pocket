using System.Collections.Immutable;
using Pockets.Core.Cosmology;
using Pockets.Core.Cosmology.Recipes;

namespace Pockets.Core.Tests.Cosmology.Recipes;

/// <summary>
/// The reachability projection behaves as a sound crafting closure: from the world
/// start everything is eventually reachable, quadrants are gated until their
/// cross-zone material is in hand, the immediate frontier is exactly the craftable
/// next step, and the closure is monotone in the input state.
/// </summary>
public class ReachabilityTests
{
    private static readonly RecipeBook Book = DepthRecipeData.Book;

    [Fact]
    public void From_Start_Everything_Is_Reachable()
    {
        var result = Reachability.Project(Book, ProgressionState.Start(Book));
        Assert.Equal(Book.Nodes.Length, result.Reachable.Count);
        foreach (var n in Book.Nodes)
            Assert.Contains(n, result.Reachable);
    }

    [Fact]
    public void From_Start_Both_Hero_Gates_Eventually_Unlock()
    {
        var result = Reachability.Project(Book, ProgressionState.Start(Book));
        Assert.Equal(Book.Gates.Length, result.UnlockedGates.Length);
    }

    [Fact]
    public void Start_Frontier_Is_Only_Quiet_Two()
    {
        // At the world start (just Quiet 1) the sole craftable next step is Quiet 2:
        // every other quadrant is still gated behind a Quiet material.
        var frontier = Reachability.Frontier(Book, ProgressionState.Start(Book));
        var node = Assert.Single(frontier);
        Assert.Equal(new ZoneDepth(Zone.QuietPositive, 2), node);
    }

    [Fact]
    public void Gloam_Entry_Is_Craftable_Exactly_When_Quiet_Five_Is_In_Hand()
    {
        // Semi-linearization gating shows in *immediate* craftability: Gloam 1 needs a
        // Quiet-5 material directly. (From the world start the closure still climbs the
        // Quiet chain to 5 and opens Gloam — that's the "all reachable" property; this
        // test isolates the one-step gate.)
        var gloam1 = new ZoneDepth(Zone.GloamPositive, 1);
        var quietUpToFour = ProgressionState.Of(
            Enumerable.Range(1, 4).Select(d => new ZoneDepth(Zone.QuietPositive, d)));
        Assert.False(Reachability.IsCraftable(Book, gloam1, quietUpToFour.Reached));

        var withFive = quietUpToFour.With(new ZoneDepth(Zone.QuietPositive, 5));
        Assert.True(Reachability.IsCraftable(Book, gloam1, withFive.Reached));
    }

    [Fact]
    public void Frontier_Nodes_Are_Not_Already_Reached_And_Are_Craftable()
    {
        var state = ProgressionState.Of(new[]
        {
            new ZoneDepth(Zone.QuietPositive, 1),
            new ZoneDepth(Zone.QuietPositive, 2),
            new ZoneDepth(Zone.QuietPositive, 3),
        });
        foreach (var n in Reachability.Frontier(Book, state))
        {
            Assert.DoesNotContain(n, state.Reached);
            Assert.True(Reachability.IsCraftable(Book, n, state.Reached));
        }
    }

    [Fact]
    public void Barrenhold_Unlocks_Exactly_When_Quiet_Three_Is_Reached()
    {
        var beforeThree = ProgressionState.Of(new[]
        {
            new ZoneDepth(Zone.QuietPositive, 1),
            new ZoneDepth(Zone.QuietPositive, 2),
        });
        Assert.Empty(Reachability.FrontierGates(Book, beforeThree));

        var withThree = beforeThree.With(new ZoneDepth(Zone.QuietPositive, 3));
        var ready = Reachability.FrontierGates(Book, withThree);
        Assert.Contains(ready, g => g.Id == "barrenhold");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(42)]
    [InlineData(1234)]
    [InlineData(99999)]
    public void Reachability_Is_Monotone_In_The_State(int seed)
    {
        // Property: for random reached sets A ⊆ B, closure(A) ⊆ closure(B).
        var rng = new Random(seed);
        var all = Book.Nodes;

        for (int trial = 0; trial < 20; trial++)
        {
            var b = all.Where(_ => rng.NextDouble() < 0.5).ToImmutableHashSet();
            // A is a random subset of B.
            var a = b.Where(_ => rng.NextDouble() < 0.5).ToImmutableHashSet();

            var reachA = Reachability.Reachable(Book, new ProgressionState(a));
            var reachB = Reachability.Reachable(Book, new ProgressionState(b));

            Assert.True(reachA.IsSubsetOf(reachB),
                $"closure(A) must be a subset of closure(B) for A ⊆ B (seed {seed}, trial {trial})");
        }
    }

    [Fact]
    public void Closure_Is_Idempotent()
    {
        // Projecting an already-projected state adds nothing new.
        var once = Reachability.Reachable(Book, ProgressionState.Start(Book));
        var twice = Reachability.Reachable(Book, new ProgressionState(once));
        Assert.Equal(once, twice);
    }
}
