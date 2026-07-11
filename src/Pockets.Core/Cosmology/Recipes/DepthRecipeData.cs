namespace Pockets.Core.Cosmology.Recipes;

/// <summary>
/// The starter depth-recipe dataset — the <b>single tunable source</b> the game and
/// the design tools load. Everything here is DESIGN DATA, not final balance: depth
/// counts, edge source-depths, and hero-gate quantities are pacing knobs. The only
/// pinned value is Quiet+ = 10, which realizes the cosmology's cited Bloom-11 ←
/// Quiet-10 boundary exactly.
///
/// <para>
/// The chirality/aspect/flavor of every zone comes from <see cref="EntropyMatrix"/>
/// (the SSOT); this file only chooses how <i>deep</i> each chain runs and how the
/// quadrants sequence around the circle.
/// </para>
/// </summary>
public static class DepthRecipeData
{
    // --- TUNABLE: within-zone chain lengths (positive depths, negative depths). ---
    // Quiet+ is pinned to 10 so Bloom (Quiet−) opens at 11, per the cosmology capture.
    private static readonly ImmutableArray<QuadrantChain> Chains = ImmutableArray.Create(
        new QuadrantChain(Quadrant.Quiet, PositiveDepths: 10, NegativeDepths: 6),
        new QuadrantChain(Quadrant.Gloam, PositiveDepths: 5, NegativeDepths: 4),
        new QuadrantChain(Quadrant.Flux, PositiveDepths: 5, NegativeDepths: 4),
        new QuadrantChain(Quadrant.Jitter, PositiveDepths: 5, NegativeDepths: 4));

    // --- TUNABLE: cross-zone semi-linearization edges (clockwise around the circle). ---
    // Each new quadrant's entry demands a mid-depth material from its predecessor, so
    // the four ladders resolve to a partial order rooted at Quiet 1.
    private static readonly ImmutableArray<CrossZoneEdge> Edges = ImmutableArray.Create(
        new CrossZoneEdge(
            new ZoneDepth(Zone.QuietPositive, 5), new ZoneDepth(Zone.GloamPositive, 1),
            Note: "cosmology example: a Quiet 5 material is required to craft Gloam 1"),
        new CrossZoneEdge(
            new ZoneDepth(Zone.GloamPositive, 3), new ZoneDepth(Zone.FluxPositive, 1),
            Note: "Flux opens once Gloam is mid-explored"),
        new CrossZoneEdge(
            new ZoneDepth(Zone.FluxPositive, 3), new ZoneDepth(Zone.JitterPositive, 1),
            Note: "Jitter (chaos) opens last, gated on Flux"));

    // --- TUNABLE: hero-piece gates (deterministic maps hung off the graph). ---
    private static readonly ImmutableArray<HeroGate> Gates = ImmutableArray.Create(
        new HeroGate(
            Id: "barrenhold",
            Name: "Barrenhold",
            Requires: ImmutableArray.Create(
                new Ingredient(QuadrantChain.MaterialAt(new ZoneDepth(Zone.QuietPositive, 3)), 3)),
            Beat: new StoryBeat(
                "Barrenhold — an old city",
                "Three Quiet-3 parts open a portal to a deterministic ruined city; " +
                "a living survivor has made it their holdout.",
                BeatType.Character)),
        new HeroGate(
            Id: "sunken-beacon",
            Name: "The Sunken Beacon",
            Requires: ImmutableArray.Create(
                new Ingredient(QuadrantChain.MaterialAt(new ZoneDepth(Zone.GloamNegative, 7)), 2)),
            Beat: new StoryBeat(
                "The Sunken Beacon",
                "Two Glow parts from deep Gloam light a drowned lighthouse where a boss waits.",
                BeatType.Boss)));

    /// <summary>The assembled starter progression graph.</summary>
    public static RecipeBook Book { get; } = RecipeBook.Assemble(Chains, Edges, Gates);
}
