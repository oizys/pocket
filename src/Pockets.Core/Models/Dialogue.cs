using System.Collections.Immutable;

namespace Pockets.Core.Models;

/// <summary>
/// How a dialogue beat's scripted trigger condition is evaluated. Deterministic
/// event/counter conditions only — NO randomness, no cooldown machinery: the conditions
/// themselves prevent spam, and each beat fires at most once (see <see cref="DialogueState"/>).
/// New conditions slot in here as they're needed (first-failed-peek, tree-bump-count, first-nest…).
/// </summary>
public enum DialogueTriggerKind
{
    /// <summary>Seeded active at profile start — the opening beat, before the world exists.</summary>
    GameStart,

    /// <summary>Fires when the Nth unique item is inspected (cursor rests on a not-yet-seen item type).</summary>
    NthUniqueInspect,

    /// <summary>
    /// Fires the first time a look-in peek (<c>C</c>) is refused because the target bag is
    /// <see cref="Bag.EnterOnly"/> (journey 15:30 — "Can't just peek at this one."). The refused
    /// peek itself is the teaching moment; the beat names it once. Threshold is unused.
    /// </summary>
    FirstFailedPeek,

    /// <summary>
    /// Fires the first time a look-in peek SUCCEEDS while the hand is carrying something (Slice 5,
    /// journey 21:00 — "Which one has space? I'd have to look inside every single one."): the
    /// capacity-absence the eye core later answers. Deterministic; threshold unused.
    /// </summary>
    FirstPeekWhileCarrying,

    /// <summary>
    /// Fires when a core is slotted into a Shrine feature slot (Slice 5, journey 28:00 —
    /// "…Oh. Now I can see."): the resolution line paired with the eye-of-fullness unlock.
    /// Threshold unused.
    /// </summary>
    CoreSlotted
}

/// <summary>
/// A beat's scripted trigger condition: a kind plus an integer threshold (0 when unused, e.g.
/// <see cref="DialogueTriggerKind.GameStart"/>). Parsed from the data file's <c>Trigger:</c> field.
/// </summary>
public record DialogueTrigger(DialogueTriggerKind Kind, int Threshold = 0)
{
    /// <summary>
    /// Parses a trigger from its data-file spelling: the kind name optionally followed by an
    /// integer threshold, e.g. <c>"GameStart"</c> or <c>"NthUniqueInspect 3"</c>. Whitespace-
    /// or colon-separated.
    /// </summary>
    public static DialogueTrigger Parse(string raw)
    {
        var tokens = raw.Split(new[] { ' ', '\t', ':' }, StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0)
            throw new FormatException("Empty dialogue trigger");
        if (!Enum.TryParse<DialogueTriggerKind>(tokens[0], ignoreCase: true, out var kind))
            throw new FormatException($"Unknown dialogue trigger kind: {tokens[0]}");
        var threshold = tokens.Length > 1 && int.TryParse(tokens[1], out var n) ? n : 0;
        return new DialogueTrigger(kind, threshold);
    }
}

/// <summary>
/// One line of a beat: the spoken text plus a portrait-emotion tag (a free-form string from the
/// data file, e.g. "groggy", "puzzled") that the frontends map to a portrait placeholder.
/// </summary>
public record DialogueLine(string Text, string Portrait);

/// <summary>
/// A scripted dialogue beat: an id, its trigger condition, its ordered lines, and (optionally) a
/// chrome element it materializes when dismissed (e.g. the opening beat reveals the Grid — the
/// world fades in as the box drops). Data-driven — authored in <c>/data</c> markdown like item cards.
/// </summary>
public record DialogueBeat(
    string Id,
    DialogueTrigger Trigger,
    ImmutableArray<DialogueLine> Lines,
    ChromeElement? Reveals = null);

/// <summary>
/// The set of authored dialogue beats, keyed by id. Static content (like the recipe set) — loaded
/// once from <c>/data</c> and carried on <see cref="GameSession"/>. Runtime progression (which beat
/// is showing, which have fired) lives in <see cref="DialogueState"/> on <see cref="GameState"/>.
/// </summary>
public record DialogueBook(ImmutableDictionary<string, DialogueBeat> Beats)
{
    /// <summary>No beats — the default for non-demo profiles (no dialogue ever shows).</summary>
    public static readonly DialogueBook Empty = new(ImmutableDictionary<string, DialogueBeat>.Empty);

    /// <summary>The beat with this id, or null.</summary>
    public DialogueBeat? Get(string id) => Beats.TryGetValue(id, out var beat) ? beat : null;

    /// <summary>
    /// All beats using a given trigger kind, in stable (id-sorted) order so firing is deterministic
    /// when several share a condition.
    /// </summary>
    public IEnumerable<DialogueBeat> WithTrigger(DialogueTriggerKind kind) =>
        Beats.Values.Where(b => b.Trigger.Kind == kind).OrderBy(b => b.Id, StringComparer.Ordinal);
}
