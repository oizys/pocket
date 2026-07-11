namespace Pockets.Core.Cosmology.Recipes;

/// <summary>
/// A crafting material with <b>zone×depth provenance</b>: it is found in the
/// wilderness at <see cref="Source"/>. Recipes for depth <i>n+1</i> consume
/// materials whose <see cref="Source"/> is depth <i>n</i> of the same quadrant
/// chain (within-zone), plus — for gated entries and hero pieces — materials
/// sourced elsewhere (cross-zone edges / gate requirements).
///
/// <para>
/// Materials are DATA keyed by <see cref="Id"/>. The starter set (one signature
/// material per node) is generated in <see cref="DepthRecipeData"/> from the
/// <see cref="EntropyMatrix"/> flavor nouns; quantities and multi-material recipes
/// are tunable design knobs, not final balance.
/// </para>
/// </summary>
public sealed record Material(string Id, string Name, ZoneDepth Source)
{
    /// <summary>Convenience: the material's provenance zone.</summary>
    public Zone SourceZone => Source.Zone;

    /// <summary>Convenience: the material's provenance depth.</summary>
    public int SourceDepth => Source.Depth;
}

/// <summary>A quantity of a specific material required by a recipe.</summary>
public sealed record Ingredient(Material Material, int Quantity)
{
    /// <summary>The node the ingredient material is harvested from.</summary>
    public ZoneDepth Source => Material.Source;
}
