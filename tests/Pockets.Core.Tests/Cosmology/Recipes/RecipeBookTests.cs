using Pockets.Core.Cosmology;
using Pockets.Core.Cosmology.Recipes;

namespace Pockets.Core.Tests.Cosmology.Recipes;

/// <summary>
/// The starter depth-recipe graph loads and has the shape the cosmology dictates:
/// eight zone chains derived from the <see cref="EntropyMatrix"/> SSOT, one signature
/// material per node with correct provenance, a single Quiet-1 world start, the cited
/// Bloom-11 ← Quiet-10 boundary, the Gloam-1 ← Quiet-5 cross-zone edge, and the
/// Barrenhold hero gate sourced from Quiet 3.
/// </summary>
public class RecipeBookTests
{
    private static readonly RecipeBook Book = DepthRecipeData.Book;

    [Fact]
    public void Covers_All_Eight_Zones()
    {
        var zones = Book.Nodes.Select(n => n.Zone).Distinct().ToHashSet();
        Assert.Equal(8, zones.Count);
        foreach (var z in EntropyMatrix.Zones.Select(z => z.Zone))
            Assert.Contains(z, zones);
    }

    [Fact]
    public void Quiet_Positive_Runs_To_Ten_So_Bloom_Starts_At_Eleven()
    {
        var quiet = Book.Chain(Quadrant.Quiet);
        Assert.Equal(10, quiet.PositiveDepths);
        Assert.Equal(11, quiet.NegativeStartDepth);

        var bloom11 = new ZoneDepth(Zone.QuietNegative, 11);
        Assert.Contains(bloom11, Book.Nodes);
        Assert.Equal(Aspect.Negative, bloom11.Aspect);
    }

    [Fact]
    public void Bloom_Eleven_Recipe_Extends_From_Quiet_Ten()
    {
        var bloom11 = new ZoneDepth(Zone.QuietNegative, 11);
        var recipe = Book.RecipeFor(bloom11);
        Assert.NotNull(recipe);
        Assert.Equal(RecipeKind.WithinZone, recipe!.Kind);
        Assert.Contains(new ZoneDepth(Zone.QuietPositive, 10), recipe.Sources);
    }

    [Fact]
    public void Every_Material_Has_Its_Own_Node_As_Provenance()
    {
        foreach (var n in Book.Nodes)
        {
            var mat = Book.MaterialAt(n);
            Assert.Equal(n, mat.Source);
            Assert.Equal(n.Key, mat.Id);
        }
    }

    [Fact]
    public void Single_World_Start_Is_Quiet_Positive_One()
    {
        var root = Assert.Single(Book.Roots);
        Assert.Equal(new ZoneDepth(Zone.QuietPositive, 1), root);
        Assert.Null(Book.RecipeFor(root));
    }

    [Fact]
    public void Within_Zone_Recipe_Draws_From_Previous_Depth()
    {
        // A representative interior node: Quiet 4 draws from Quiet 3.
        var recipe = Book.RecipeFor(new ZoneDepth(Zone.QuietPositive, 4));
        Assert.NotNull(recipe);
        Assert.Contains(new ZoneDepth(Zone.QuietPositive, 3), recipe!.Sources);
    }

    [Fact]
    public void Gloam_Entry_Is_Gated_On_A_Quiet_Five_Material()
    {
        var gloam1 = new ZoneDepth(Zone.GloamPositive, 1);
        var recipe = Book.RecipeFor(gloam1);
        Assert.NotNull(recipe);
        Assert.Equal(RecipeKind.Entry, recipe!.Kind);
        Assert.Contains(new ZoneDepth(Zone.QuietPositive, 5), recipe.Sources);

        // And it is expressed as a declared, tunable cross-zone edge.
        Assert.Contains(Book.Edges, e =>
            e.Source == new ZoneDepth(Zone.QuietPositive, 5) && e.Target == gloam1);
    }

    [Fact]
    public void Barrenhold_Gate_Requires_Three_Quiet_Three_Parts()
    {
        var gate = Assert.Single(Book.Gates, g => g.Id == "barrenhold");
        Assert.Equal("Barrenhold", gate.Name);
        var req = Assert.Single(gate.Requires);
        Assert.Equal(new ZoneDepth(Zone.QuietPositive, 3), req.Source);
        Assert.Equal(3, req.Quantity);
        Assert.Equal(BeatType.Character, gate.Beat.Type);
    }

    [Fact]
    public void Node_ToString_Reads_As_Quadrant_Sign_Depth()
    {
        Assert.Equal("Quiet− 11", new ZoneDepth(Zone.QuietNegative, 11).ToString());
        Assert.Equal("Quiet+ 1", new ZoneDepth(Zone.QuietPositive, 1).ToString());
    }
}
