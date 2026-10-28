using System.Collections.Immutable;
using Pockets.Core.Models;
using Pockets.Core.Rendering;
using Pockets.Core.Sim;

namespace Pockets.Core.Tests.Sim;

/// <summary>
/// Slice 1.3: the paths that used to bypass <see cref="Simulation.Step"/> (cursor, modal split, DSL, the
/// clock) now go through it as data intents and keep their old semantics — no TickCount bump and no undo
/// frame where there were none — and the shell's intent log records exactly the intents that changed the session.
/// </summary>
public class IntentStepTests
{
    private static readonly ItemType Rock = new("Plain Rock", Category.Material, IsStackable: true, MaxStackSize: 20);
    private static readonly ItemType Wood = new("Rough Wood", Category.Material, IsStackable: true, MaxStackSize: 20);

    /// <summary>A rogue-mode session whose root bag holds 8 Rock at cell 0 and 3 Wood at cell 1.</summary>
    private static GameSession NewSession(TickMode mode = TickMode.Rogue)
    {
        var state = GameState.CreateStage1(
            ImmutableArray.Create(Rock, Wood),
            new[] { new ItemStack(Rock, 8), new ItemStack(Wood, 3) });
        return GameSession.New(state, ImmutableArray<Recipe>.Empty, mode);
    }

    /// <summary>A cursor move steps through Step but is not a timed action: no tick, no undo frame, no log line.</summary>
    [Fact]
    public void Move_ChangesCursorOnly_NoTickNoUndoNoLog()
    {
        var session = NewSession();
        var after = session.Apply(new Intent.Move(LocationId.B, Direction.Right));

        Assert.Equal(new Position(0, 1), after.Current.Cursor.Position);
        Assert.Equal(session.TickCount, after.TickCount);
        Assert.Equal(0, after.UndoDepth);
        Assert.Empty(after.ActionLog);
        Assert.Equal(new[] { new Intent.Move(LocationId.B, Direction.Right) }, after.IntentLog.Select(e => e.Intent));
    }

    /// <summary>Begin/adjust only change the split editor; commit is one undoable timed split, and closes the editor.</summary>
    [Fact]
    public void SplitModal_OnlyCommitStepsTimeAndPushesUndo()
    {
        var session = NewSession()
            .Apply(new Intent.BeginSplit(LocationId.B))
            .Apply(new Intent.AdjustSplit(+1));

        Assert.Equal(5, session.SplitMode!.GrabCount);
        Assert.Equal(0, session.TickCount);
        Assert.Equal(0, session.UndoDepth);

        var committed = session.Apply(new Intent.CommitSplit());

        Assert.Null(committed.SplitMode);
        Assert.Equal(1, committed.TickCount);
        Assert.Equal(1, committed.UndoDepth);
        Assert.Equal(3, committed.Current.CurrentCell.Stack!.Count);
        Assert.Equal("Split: 8 Plain Rock → 3/5", committed.ActionLog[^1]);
        Assert.IsType<Intent.CommitSplit>(committed.IntentLog[^1].Intent);
    }

    /// <summary>An intent that changes nothing returns the same session and is left out of the intent log.</summary>
    [Fact]
    public void NoOpIntent_ReturnsSameSession_AndIsNotLogged()
    {
        var session = NewSession();

        Assert.Same(session, session.Apply(new Intent.CancelSplit()));
        Assert.Same(session, session.Apply(new Intent.SetElapsed(TimeSpan.Zero)));
        Assert.Same(session, session.Apply(new Intent.Undo()));
        Assert.Empty(session.IntentLog);
    }

    /// <summary>The clock readout is an intent too, and sets only Elapsed.</summary>
    [Fact]
    public void SetElapsed_SetsTheClockOnly()
    {
        var session = NewSession(TickMode.Realtime);
        var after = session.Apply(new Intent.SetElapsed(TimeSpan.FromSeconds(3)));

        Assert.Equal(TimeSpan.FromSeconds(3), after.Elapsed);
        Assert.Same(session.Current, after.Current);
        Assert.Equal(0, after.TickCount);
    }

    /// <summary>
    /// A DSL program runs inside Step: its random draw comes from the Dsl stream at the step and the advanced
    /// counter is committed with the state (1.2/1.1 ran it before Step and dropped the counter).
    /// </summary>
    [Fact]
    public void Dsl_RunsInsideStep_AndCommitsItsDraw()
    {
        var s = NewSession(TickMode.Realtime).ToSimState();
        var r = Simulation.Step(s, new[] { new Intent.Dsl("acquire-random") });

        Assert.IsType<SimEvent.IntentApplied>(r.Events[0]);
        Assert.Equal(1, r.State.Rng.DrawCount(SimStream.Dsl));
    }

    /// <summary>A DSL parse failure is a rejected intent, logged exactly as before (no state change).</summary>
    [Fact]
    public void Dsl_ParseFailure_IsRejected_WithTheOldLogLine()
    {
        var session = NewSession();
        var after = session.Apply(new Intent.Dsl("((("));

        Assert.Same(session.Current, after.Current);
        Assert.Equal(0, after.TickCount);
        Assert.StartsWith("FAILED: ((( — ", after.ActionLog.Single());
    }

    /// <summary>
    /// Focus never leaks into replay: the same intents produce the same state whatever panel the controller
    /// had focused, because each intent names its target location.
    /// </summary>
    [Fact]
    public void SameIntents_SameState_RegardlessOfControllerFocus()
    {
        var a = new GameController(NewSession());
        var b = new GameController(NewSession() with { Current = a.Session.Current });
        b.SetFocus(LocationId.T);

        a.HandleKey(GameKey.Right);
        a.HandleKey(GameKey.Primary);

        var replayed = a.Session.IntentLog.Aggregate(b.Session, (s, e) => s.Apply(e.Intent));
        Assert.Equal(ViewModelSerializer.SerializeToString(a.Session), ViewModelSerializer.SerializeToString(replayed));
        Assert.Equal(a.Session.ActionLog, replayed.ActionLog);
    }
}
