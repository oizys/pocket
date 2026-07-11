namespace Pockets.Core.Cosmology.Recipes;

/// <summary>What kind of story beat a hero-piece map hosts.</summary>
public enum BeatType
{
    /// <summary>Meeting one of the ~8 living people (an arc marker).</summary>
    Character,
    /// <summary>A boss encounter.</summary>
    Boss,
    /// <summary>Finding a major relic / ruins.</summary>
    Relic
}

/// <summary>
/// The narrative payload attached to a <see cref="HeroGate"/> — a metadata slot so
/// narrative can key beats to progression without the mechanics layer owning story
/// text. Deliberately thin: a title, a one-line summary, and the beat's flavor.
/// </summary>
public sealed record StoryBeat(string Title, string Summary, BeatType Type);

/// <summary>
/// A first-class <b>hero-piece gate</b>: a special recipe whose ingredients unlock a
/// portal/bag to a <i>deterministic</i> hero-piece wilderness (vs. the random
/// wildernesses at ordinary nodes). Cosmology's example: <b>Quiet 3 parts unlock
/// "Barrenhold"</b>, an old city that hosts a story beat.
///
/// <para>
/// A gate is not a <see cref="ZoneDepth"/> node — it hangs off the graph as its own
/// unlockable, sourced from <see cref="Requires"/> materials (with provenance) and
/// carrying a <see cref="Beat"/> metadata slot. Gates are TUNABLE design data.
/// </para>
/// </summary>
public sealed record HeroGate(
    string Id,
    string Name,
    ImmutableArray<Ingredient> Requires,
    StoryBeat Beat)
{
    /// <summary>The distinct source nodes whose materials this gate consumes.</summary>
    public IEnumerable<ZoneDepth> Sources => Requires.Select(i => i.Source).Distinct();
}
