using Pockets.Core.Sim;

namespace Pockets.Realization;

/// <summary>The phase of a rogue turn an event is staged in (slice 2.3). No zero member.</summary>
public enum TurnPhase
{
    /// <summary>The player's own action: the intent envelope and the changes it makes directly. Plays at 0 ms.</summary>
    Player = 1,

    /// <summary>The world's reaction to the action (bumps, harvests, rule events). Starts at the policy's overlap of the player phase.</summary>
    Reaction,

    /// <summary>The clock's consequences: a time advance and everything after it in the same envelope. Starts with the reactions.</summary>
    Ambient
}

/// <summary>Classifies the events of a commit into turn phases and decides whether a commit is a clock commit (P2-D13).</summary>
public static class Choreography
{
    /// <summary>True when <paramref name="ev"/> opens a new step relative to <paramref name="prev"/> (or is the first event, or has Seq 0).</summary>
    private static bool StartsStep(SimEvent? prev, SimEvent ev) => prev is null || prev.Step != ev.Step || ev.Seq == 0;

    /// <summary>True for the envelope events that open an intent's span.</summary>
    private static bool IsEnvelope(SimEvent ev) => ev is SimEvent.IntentApplied or SimEvent.IntentRejected or SimEvent.Rewound;

    /// <summary>
    /// The phase of each event, in order. Envelopes are Player and end any ambient run; a <see cref="SimEvent.TimeAdvanced"/>
    /// and everything after it in the same step are Ambient; otherwise the kind decides (own changes are Player, the rest Reaction).
    /// </summary>
    public static ImmutableArray<TurnPhase> Phases(IReadOnlyList<SimEvent> events)
    {
        var ambient = false;
        SimEvent? prev = null;
        var result = new List<TurnPhase>();
        foreach (var ev in events)
        {
            if (StartsStep(prev, ev)) ambient = false;
            prev = ev;
            if (IsEnvelope(ev)) { ambient = false; result.Add(TurnPhase.Player); continue; }
            if (ev is SimEvent.TimeAdvanced) { ambient = true; result.Add(TurnPhase.Ambient); continue; }
            result.Add(ambient ? TurnPhase.Ambient : ev switch
            {
                SimEvent.CursorMoved or SimEvent.BagEntered or SimEvent.BagLeft or SimEvent.PanelOpened
                    or SimEvent.PanelClosed or SimEvent.StackMoved or SimEvent.StackSplit or SimEvent.StackMerged
                    or SimEvent.StackRemoved or SimEvent.BagRearranged or SimEvent.Acquired
                    or SimEvent.RecipeSelected => TurnPhase.Player,
                _ => TurnPhase.Reaction
            });
        }
        return result.ToImmutableArray();
    }

    /// <summary>
    /// True when the commit is a clock commit: non-empty and every event lies in a span opened by a Tick or SetElapsed
    /// envelope or by a <see cref="SimEvent.TimeAdvanced"/>. An empty commit is input, not clock.
    /// </summary>
    public static bool IsClock(IReadOnlyList<SimEvent> events)
    {
        if (events.Count == 0) return false;
        var clock = false;
        SimEvent? prev = null;
        foreach (var ev in events)
        {
            if (StartsStep(prev, ev)) clock = false;
            prev = ev;
            if (ev is SimEvent.IntentApplied { Intent: Intent.Tick or Intent.SetElapsed }
                or SimEvent.IntentRejected { Intent: Intent.Tick or Intent.SetElapsed }) clock = true;
            else if (IsEnvelope(ev)) clock = false;
            else if (ev is SimEvent.TimeAdvanced) clock = true;
            if (!clock) return false;
        }
        return true;
    }
}
