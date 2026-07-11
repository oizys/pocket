namespace Pockets.Core.Cosmology.Recipes;

/// <summary>The result of projecting reachability from a <see cref="ProgressionState"/>.</summary>
/// <param name="Reachable">The closure — every node eventually reachable from the input.</param>
/// <param name="UnlockedGates">Hero gates whose materials all come from the closure.</param>
/// <param name="Frontier">
/// The <b>immediate</b> next nodes: not yet reached in the input state, but craftable
/// now from currently-held materials. This is what the design tool prints and the
/// radar minimap highlights.
/// </param>
/// <param name="FrontierGates">Hero gates unlockable right now from the input state.</param>
public sealed record ReachabilityResult(
    ImmutableHashSet<ZoneDepth> Reachable,
    ImmutableArray<HeroGate> UnlockedGates,
    ImmutableArray<ZoneDepth> Frontier,
    ImmutableArray<HeroGate> FrontierGates);

/// <summary>
/// The pure reachability projection over a <see cref="RecipeBook"/>: given what the
/// player has reached, which zone×depths can they get to? This is both the radar
/// minimap's future data source and the design-analysis tool that prints the
/// progression frontier. All functions are side-effect-free and deterministic.
/// </summary>
public static class Reachability
{
    /// <summary>
    /// A node is craftable from a reached set when it is not already reached and its
    /// recipe exists with every ingredient source already reached (materials in hand).
    /// Roots have no recipe, so they are never "craftable" — they enter only as starts.
    /// </summary>
    public static bool IsCraftable(RecipeBook book, ZoneDepth node, IImmutableSet<ZoneDepth> reached)
    {
        if (reached.Contains(node)) return false;
        var recipe = book.RecipeFor(node);
        return recipe is not null && recipe.Sources.All(reached.Contains);
    }

    /// <summary>Whether a hero gate can be unlocked from a reached set.</summary>
    public static bool IsUnlockable(HeroGate gate, IImmutableSet<ZoneDepth> reached) =>
        gate.Sources.All(reached.Contains);

    /// <summary>The immediate craftable frontier of a state (one crafting step away).</summary>
    public static ImmutableArray<ZoneDepth> Frontier(RecipeBook book, ProgressionState state) =>
        book.Nodes.Where(n => IsCraftable(book, n, state.Reached)).ToImmutableArray();

    /// <summary>The hero gates unlockable right now from a state.</summary>
    public static ImmutableArray<HeroGate> FrontierGates(RecipeBook book, ProgressionState state) =>
        book.Gates.Where(g => IsUnlockable(g, state.Reached)).ToImmutableArray();

    /// <summary>
    /// Project the full reachable closure from a state: repeatedly craft everything
    /// craftable until nothing new appears (a monotone fixpoint). Returns the closure,
    /// the gates it unlocks, and the immediate frontier/gates of the <i>input</i> state.
    /// </summary>
    public static ReachabilityResult Project(RecipeBook book, ProgressionState state)
    {
        var reached = state.Reached;
        bool grew;
        do
        {
            grew = false;
            foreach (var node in book.Nodes)
            {
                if (IsCraftable(book, node, reached))
                {
                    reached = reached.Add(node);
                    grew = true;
                }
            }
        }
        while (grew);

        var unlocked = book.Gates.Where(g => IsUnlockable(g, reached)).ToImmutableArray();
        return new ReachabilityResult(
            reached,
            unlocked,
            Frontier(book, state),
            FrontierGates(book, state));
    }

    /// <summary>The reachable-node closure only (convenience over <see cref="Project"/>).</summary>
    public static ImmutableHashSet<ZoneDepth> Reachable(RecipeBook book, ProgressionState state) =>
        Project(book, state).Reachable;
}
