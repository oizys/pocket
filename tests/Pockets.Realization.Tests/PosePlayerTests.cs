using System.Collections.Immutable;
using Pockets.Core.Models;
using Pockets.Core.Sim;

namespace Pockets.Realization.Tests;

/// <summary>
/// Slice 2.5: the engine-neutral half of the Godot frame loop. <see cref="FrameClock"/> turns a monotonic integer
/// ms reading into per-frame deltas, <see cref="PosePlayer"/> accumulates them against one timeline, and
/// <see cref="Timeline.SampleDrawn"/> carries each drawn pose's rest key so a frontend knows what to draw there.
/// </summary>
public class PosePlayerTests
{
    private static readonly ItemType Rock = new("Plain Rock", Category.Material, true);

    /// <summary>A world-ish scene: the body of B at (0, <paramref name="col"/>) and one centred stack in cell 3.</summary>
    private static Scene SceneOf(Guid bag, int col) =>
        new(ImmutableArray.Create(
            new Pose(new EntityKey.Body(LocationId.B), bag, col, 0, 0, 0, null, Facing.S),
            new Pose(new EntityKey.Stack(new CellRef(bag, 3)), bag, 3, 0, 0, 0, null, null)))
        {
            Columns = ImmutableDictionary<Guid, int>.Empty.Add(bag, 4)
        };

    /// <summary>A player walk of B from (0, <paramref name="from"/>) to (0, <paramref name="to"/>).</summary>
    private static SimEvent[] Walk(Guid bag, int from, int to) =>
    [
        new SimEvent.IntentApplied(1, 0, new Intent.Move(LocationId.B, Direction.Right)),
        new SimEvent.CursorMoved(1, 1, LocationId.B, bag, new Position(0, from), new Position(0, to))
    ];

    // ==================== FrameClock ====================

    /// <summary>The first reading gives 0; later readings give the integer difference.</summary>
    [Fact]
    public void FrameClock_FirstTickIsZero_ThenDifferences()
    {
        var clock = new FrameClock();
        Assert.Equal(0, clock.Tick(5000));
        Assert.Equal(16, clock.Tick(5016));
        Assert.Equal(17, clock.Tick(5033));
        Assert.Equal(0, clock.Tick(5033));
    }

    /// <summary>A reading that goes backwards gives 0 (never a negative delta), and a huge gap clamps to int range.</summary>
    [Fact]
    public void FrameClock_ClampsBackwardsAndHugeGaps()
    {
        var clock = new FrameClock();
        clock.Tick(1000);
        Assert.Equal(0, clock.Tick(900));
        Assert.Equal(100, clock.Tick(1000));
        Assert.Equal(int.MaxValue, clock.Tick(1000 + (long)int.MaxValue * 4));
    }

    // ==================== PosePlayer ====================

    /// <summary>A fresh player is at rest: no tracks, elapsed 0, the rest poses drawn.</summary>
    [Fact]
    public void Player_StartsAtRest()
    {
        var bag = Guid.NewGuid();
        var player = new PosePlayer(SceneOf(bag, 0));
        Assert.False(player.IsAnimating);
        Assert.Equal(0, player.ElapsedMs);
        Assert.Equal(SceneOf(bag, 0).Poses, player.Poses.Select(d => d.Pose));
    }

    /// <summary>
    /// Integer ms accumulate across frames, and the drawn poses are the timeline's samples at the accumulated time,
    /// exactly as the pose golden samples them.
    /// </summary>
    [Fact]
    public void Player_AccumulatesIntegerMs_AndSamplesTheTimeline()
    {
        var bag = Guid.NewGuid();
        var player = new PosePlayer(SceneOf(bag, 0));
        player.Commit(Walk(bag, 0, 1), SceneOf(bag, 1));
        Assert.True(player.IsAnimating);

        Assert.True(player.Advance(16));
        Assert.True(player.Advance(15));
        Assert.Equal(31, player.ElapsedMs);
        Assert.Equal(player.Timeline.Sample(31), player.Poses.Select(d => d.Pose));
        var body = player.Poses.Single(d => d.Pose.Entity is EntityKey.Body).Pose;
        Assert.True(body.CellCol * Pose.SubUnits + body.SubX is > 0 and < Pose.SubUnits);
    }

    /// <summary>Elapsed clamps at the span: the last advance lands on the rest frame, and later ones report no change.</summary>
    [Fact]
    public void Player_ClampsAtSpan_ThenStopsReportingChanges()
    {
        var bag = Guid.NewGuid();
        var player = new PosePlayer(SceneOf(bag, 0));
        player.Commit(Walk(bag, 0, 1), SceneOf(bag, 1));

        Assert.True(player.Advance(1000));
        Assert.Equal(RealizationPolicy.Default.WalkMs, player.ElapsedMs);
        Assert.False(player.IsAnimating);
        Assert.Equal(SceneOf(bag, 1).Poses.Select(p => (p.CellCol, p.SubX)),
            player.Poses.Select(d => (d.Pose.CellCol, d.Pose.SubX)));
        Assert.False(player.Advance(16));
        Assert.False(player.Advance(0));
    }

    /// <summary>Zero and negative deltas change nothing.</summary>
    [Fact]
    public void Player_IgnoresNonPositiveDeltas()
    {
        var bag = Guid.NewGuid();
        var player = new PosePlayer(SceneOf(bag, 0));
        player.Commit(Walk(bag, 0, 1), SceneOf(bag, 1));
        Assert.False(player.Advance(0));
        Assert.False(player.Advance(-5));
        Assert.Equal(0, player.ElapsedMs);
    }

    /// <summary>
    /// D7: new input mid-walk snaps. The commit resets elapsed to 0 and the old track is gone, so the body draws from
    /// the end of the first walk, not from where the frame happened to be.
    /// </summary>
    [Fact]
    public void Player_InputMidWalk_Snaps()
    {
        var bag = Guid.NewGuid();
        var player = new PosePlayer(SceneOf(bag, 0));
        player.Commit(Walk(bag, 0, 1), SceneOf(bag, 1));
        player.Advance(40);

        player.Commit(Walk(bag, 1, 2), SceneOf(bag, 2));
        Assert.Equal(0, player.ElapsedMs);
        var body = player.Poses.Single(d => d.Pose.Entity is EntityKey.Body).Pose;
        Assert.Equal((1, 0), (body.CellCol, body.SubX));
        Assert.Single(player.Timeline.Tracks);
    }

    /// <summary>A3: a commit carrying <c>Rewound</c> resets: no track survives it, the body is at its cell centre.</summary>
    [Fact]
    public void Player_Rewound_ResetsToRest()
    {
        var bag = Guid.NewGuid();
        var player = new PosePlayer(SceneOf(bag, 0));
        player.Commit(Walk(bag, 0, 1), SceneOf(bag, 1));
        player.Advance(40);

        player.Commit([new SimEvent.Rewound(2, 0, 0)], SceneOf(bag, 0));
        Assert.False(player.IsAnimating);
        var body = player.Poses.Single(d => d.Pose.Entity is EntityKey.Body).Pose;
        Assert.Equal((0, 0, 0), (body.CellCol, body.SubX, body.SubY));
    }

    /// <summary>The GameState overload builds the same scene the pose stream does, so a real walk draws like the golden.</summary>
    [Fact]
    public void Player_FromState_MatchesPoseStream()
    {
        var controller = PoseStreamTests.DemoController();
        var stream = new PoseStream(controller.Session.Current, new BagNames());
        var player = new PosePlayer(controller.Session.Current, FineView.Of(controller.Session.Fine));
        foreach (var key in new[] { GameKey.Primary, GameKey.Right, GameKey.Down, GameKey.Sort })
        {
            var r = controller.HandleKey(key);
            stream.Commit(r.Events, controller.Session.Current, 0, FineView.Of(controller.Session.Fine));
            player.Commit(r.Events, controller.Session.Current, 0, FineView.Of(controller.Session.Fine));
            Assert.Equal(stream.Timeline.SpanMs, player.Timeline.SpanMs);
            Assert.Equal(stream.Timeline.Tracks, player.Timeline.Tracks);
            Assert.Equal(stream.Timeline.Scene.Poses, player.Timeline.Scene.Poses);
            foreach (var q in Enumerable.Range(0, 5))
                Assert.Equal(stream.Timeline.Sample(stream.Timeline.SpanMs * q / 4),
                    player.Timeline.Sample(player.Timeline.SpanMs * q / 4));
        }
    }

    // ==================== SampleDrawn ====================

    /// <summary>SampleDrawn is Sample plus a rest key per pose, at every ms of a commit's span.</summary>
    [Fact]
    public void SampleDrawn_PosesEqualSample()
    {
        var bag = Guid.NewGuid();
        var t = Timeline.Rest(SceneOf(bag, 0)).Commit(Walk(bag, 0, 1), SceneOf(bag, 1), RealizationPolicy.Default);
        foreach (var ms in Enumerable.Range(0, t.SpanMs + 2))
            Assert.Equal(t.Sample(ms), t.SampleDrawn(ms).Select(d => d.Pose));
    }

    /// <summary>
    /// A stack in flight carries its departure key, but its rest key is the cell it lands in, which is where the
    /// frontend finds the stack's content in the new grid.
    /// </summary>
    [Fact]
    public void SampleDrawn_StackInFlight_RestKeyIsTheLandingCell()
    {
        var bag = Guid.NewGuid();
        Scene WithStackAt(int i) => new(ImmutableArray.Create(
            new Pose(new EntityKey.Stack(new CellRef(bag, i)), bag, i % 4, i / 4, 0, 0, null, null)))
        {
            Columns = ImmutableDictionary<Guid, int>.Empty.Add(bag, 4)
        };
        var t = Timeline.Rest(WithStackAt(1)).Commit(
            [new SimEvent.StackMoved(1, 0, Rock, 1, new CellRef(bag, 1), new CellRef(bag, 2), StackCause.Drop)],
            WithStackAt(2), RealizationPolicy.Default);

        var mid = Assert.Single(t.SampleDrawn(t.SpanMs / 2));
        Assert.Equal(new EntityKey.Stack(new CellRef(bag, 1)), mid.Pose.Entity);
        Assert.Equal(new EntityKey.Stack(new CellRef(bag, 2)), mid.Rest);
    }

    /// <summary>Over a real demo run, SampleDrawn's poses equal Sample's at the golden's quarter points.</summary>
    [Fact]
    public void SampleDrawn_DemoRun_EqualsSample()
    {
        var controller = PoseStreamTests.DemoController();
        var player = new PosePlayer(controller.Session.Current);
        var keys = new[] { GameKey.Primary, GameKey.Right, GameKey.Right, GameKey.Down, GameKey.Primary, GameKey.Sort };
        foreach (var key in keys)
        {
            var r = controller.HandleKey(key);
            player.Commit(r.Events, controller.Session.Current, 0, FineView.Of(controller.Session.Fine));
            var t = player.Timeline;
            foreach (var q in Enumerable.Range(0, 5))
                Assert.Equal(t.Sample(t.SpanMs * q / 4), t.SampleDrawn(t.SpanMs * q / 4).Select(d => d.Pose));
        }
    }

    // ==================== PoseDebugCommandHandler commit hook ====================

    /// <summary>
    /// The commit hook fires once per committing command with that command's events and clock delta, so the Godot
    /// scene can commit socket commands to its own drawing player. Queries and errors do not fire it.
    /// </summary>
    [Fact]
    public void Handler_OnCommit_FiresForCommitsOnly()
    {
        var controller = PoseStreamTests.DemoController();
        var seen = new List<(int Events, int AtMs)>();
        void Send(string json) =>
            PoseDebugCommandHandler.Handle(controller, json, out _, (result, atMs) => seen.Add((result.Events.Length, atMs)));

        Send("""{"action":"key","key":"Primary"}""");
        Send("""{"action":"state"}""");
        Send("""{"action":"fine"}""");
        Send("""{"action":"poses"}""");
        Send("""{"action":"advanceTime","ms":2500}""");
        Send("""{"action":"tick"}""");
        Send("""{"action":"key","key":"NoSuchKey"}""");
        Send("{not json");

        Assert.Equal(new[] { 0, 2500, GameController.TickLengthMs }, seen.Select(s => s.AtMs));
        Assert.True(seen[0].Events > 0);
    }

    /// <summary>The hook does not change the response: it is byte-identical to the hookless overload's.</summary>
    [Fact]
    public void Handler_OnCommit_LeavesTheResponseUnchanged()
    {
        var a = PoseStreamTests.DemoController();
        var b = PoseStreamTests.DemoController();
        foreach (var cmd in new[] { """{"action":"key","key":"Primary"}""", """{"action":"key","key":"Right"}""", """{"action":"state"}""" })
        {
            var plain = PoseDebugCommandHandler.Handle(a, cmd, out var m1);
            var hooked = PoseDebugCommandHandler.Handle(b, cmd, out var m2, (_, _) => { });
            Assert.Equal(StripBags(plain), StripBags(hooked));
            Assert.Equal(m1, m2);
        }
    }

    /// <summary>The response with bag GUIDs (fresh per controller) removed; bag#N names are kept.</summary>
    private static string StripBags(string json) =>
        System.Text.RegularExpressions.Regex.Replace(json, "[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}", "G");
}
