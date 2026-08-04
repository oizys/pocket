using Pockets.Core.Models;

namespace Pockets.Core.Tests.Models;

/// <summary>
/// The runtime dialogue queue: fire-once semantics, the unique-inspect counter (rapid scans can't
/// double-count), and line advance/dismiss. These are the pure-state guarantees the beat-keyed
/// trigger conditions rely on; the GameSession/GameController tests exercise them end-to-end.
/// </summary>
public class DialogueStateTests
{
    [Fact]
    public void Empty_IsInactive()
    {
        Assert.False(DialogueState.Empty.IsActive);
        Assert.Null(DialogueState.Empty.ActiveBeatId);
        Assert.Equal(0, DialogueState.Empty.UniqueInspectCount);
    }

    [Fact]
    public void Enqueue_ActivatesBeat_AndMarksFired()
    {
        var d = DialogueState.Empty.Enqueue("opening");

        Assert.True(d.IsActive);
        Assert.Equal("opening", d.ActiveBeatId);
        Assert.True(d.HasFired("opening"));
        Assert.Equal(0, d.LineIndex);
    }

    [Fact]
    public void Enqueue_IsFireOnce_SameInstanceWhenAlreadyFired()
    {
        var d = DialogueState.Empty.Enqueue("amnesia");
        var (dismissed, _) = d.Advance(1);   // dismiss it — queue empties but it stays fired

        // Re-enqueuing a fired beat is a no-op: prevents re-fire even after dismissal.
        Assert.Same(dismissed, dismissed.Enqueue("amnesia"));
        Assert.False(dismissed.IsActive);
    }

    [Fact]
    public void Inspect_CountsUniqueTypesOnly()
    {
        var d = DialogueState.Empty
            .Inspect("Forest Bag")
            .Inspect("Plain Rock")
            .Inspect("Forest Bag");   // repeat — no growth

        Assert.Equal(2, d.UniqueInspectCount);
        Assert.Same(d, d.Inspect("Plain Rock")); // already seen → same instance
    }

    [Fact]
    public void Advance_WalksLines_ThenDismisses()
    {
        var d = DialogueState.Empty.Enqueue("beat"); // 3-line beat

        var (afterLine1, dismissed1) = d.Advance(3);
        Assert.Null(dismissed1);
        Assert.Equal(1, afterLine1.LineIndex);
        Assert.True(afterLine1.IsActive);

        var (afterLine2, dismissed2) = afterLine1.Advance(3);
        Assert.Null(dismissed2);
        Assert.Equal(2, afterLine2.LineIndex);

        var (afterLast, dismissed3) = afterLine2.Advance(3);
        Assert.Equal("beat", dismissed3);
        Assert.False(afterLast.IsActive);
        Assert.Equal(0, afterLast.LineIndex);
    }

    [Fact]
    public void Advance_SingleLineBeat_DismissesImmediately()
    {
        var d = DialogueState.Empty.Enqueue("opening");

        var (after, dismissed) = d.Advance(1);

        Assert.Equal("opening", dismissed);
        Assert.False(after.IsActive);
    }

    [Fact]
    public void Enqueue_WhileActive_QueuesBehind_HeadUnchanged()
    {
        var d = DialogueState.Empty.Enqueue("first").Enqueue("second");

        Assert.Equal("first", d.ActiveBeatId);

        var (afterDismiss, _) = d.Advance(1); // dismiss "first"
        Assert.Equal("second", afterDismiss.ActiveBeatId);
    }
}
