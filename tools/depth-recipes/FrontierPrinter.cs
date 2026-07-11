using Pockets.Core.Cosmology;
using Pockets.Core.Cosmology.Recipes;

namespace Pockets.DepthRecipes;

/// <summary>
/// Renders the pure <see cref="Reachability"/> projection as a human-readable
/// progression report: what the player has reached, the immediate craftable
/// frontier (nodes + hero gates), and the full reachable closure. This is the
/// design-analysis face of the same function the radar minimap will call.
/// </summary>
public static class FrontierPrinter
{
    /// <summary>Print the frontier report for a state to the given writer.</summary>
    public static void Print(RecipeBook book, ProgressionState state, TextWriter w)
    {
        var result = Reachability.Project(book, state);

        w.WriteLine("Pockets · depth-recipe progression frontier");
        w.WriteLine("============================================");
        w.WriteLine();

        w.WriteLine($"Reached ({state.Reached.Count}):");
        WriteNodes(book, state.Reached.OrderBy(Order), w);
        w.WriteLine();

        w.WriteLine($"Frontier — craftable next ({result.Frontier.Length}):");
        if (result.Frontier.IsEmpty)
            w.WriteLine("  (none — dead end from this state)");
        else
            foreach (var n in result.Frontier.OrderBy(Order))
                w.WriteLine($"  → {Describe(book, n)}");
        w.WriteLine();

        if (!result.FrontierGates.IsEmpty)
        {
            w.WriteLine($"Hero gates unlockable now ({result.FrontierGates.Length}):");
            foreach (var g in result.FrontierGates)
                w.WriteLine($"  ★ {g.Name} — {g.Beat.Title} [{g.Beat.Type}]");
            w.WriteLine();
        }

        int newlyReachable = result.Reachable.Count - state.Reached.Count;
        w.WriteLine($"Eventually reachable from here: {result.Reachable.Count}/{book.Nodes.Length} nodes "
            + $"(+{newlyReachable} beyond current)");
        w.WriteLine($"Hero gates reachable in closure: {result.UnlockedGates.Length}/{book.Gates.Length}");

        var unreachable = book.Nodes.Where(n => !result.Reachable.Contains(n)).ToImmutableArray();
        if (!unreachable.IsEmpty)
        {
            w.WriteLine();
            w.WriteLine($"Still out of reach ({unreachable.Length}):");
            WriteNodes(book, unreachable.OrderBy(Order), w);
        }
    }

    private static void WriteNodes(RecipeBook book, IEnumerable<ZoneDepth> nodes, TextWriter w)
    {
        var grouped = nodes.GroupBy(n => n.Quadrant);
        foreach (var g in grouped.OrderBy(g => (int)g.Key))
            w.WriteLine($"  {g.Key,-7} {string.Join(", ", g.OrderBy(n => n.Depth).Select(n => n.ToString()))}");
        if (!nodes.Any())
            w.WriteLine("  (none)");
    }

    private static string Describe(RecipeBook book, ZoneDepth node)
    {
        var recipe = book.RecipeFor(node);
        string via = recipe is null
            ? "root"
            : $"{recipe.Kind}: {string.Join(" + ", recipe.Ingredients.Select(FormatIngredient))}";
        return $"{node}  ({book.MaterialAt(node).Name})  ← {via}";
    }

    private static string FormatIngredient(Ingredient i) =>
        i.Quantity > 1 ? $"{i.Quantity}× {i.Material.Name}" : i.Material.Name;

    // Stable ordering key: quadrant, then depth.
    private static (int, int) Order(ZoneDepth n) => ((int)n.Quadrant, n.Depth);
}
