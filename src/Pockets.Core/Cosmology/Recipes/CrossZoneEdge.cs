namespace Pockets.Core.Cosmology.Recipes;

/// <summary>
/// A designed cross-zone dependency — the "around-the-circle" semi-linearization.
/// Crafting the <see cref="Target"/> node's recipe additionally requires a material
/// sourced from the <see cref="Source"/> node in another quadrant (cosmology's
/// example: "a Quiet 5 material is required to craft Gloam 1"). This is the lever
/// that sequences the four quadrants into a partial order rather than four
/// independent ladders.
///
/// <para>
/// <b>TUNABLE design data, not final balance.</b> The source depth on each edge is a
/// pacing knob: raise it to make a quadrant demand deeper progress in its predecessor
/// before it opens. <see cref="Quantity"/> defaults to 1.
/// </para>
/// </summary>
public sealed record CrossZoneEdge(
    ZoneDepth Source,
    ZoneDepth Target,
    int Quantity = 1,
    string Note = "");
