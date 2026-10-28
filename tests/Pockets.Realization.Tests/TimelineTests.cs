using System.Collections.Immutable;
using Pockets.Core.Models;
using Pockets.Core.Sim;

namespace Pockets.Realization.Tests;

/// <summary>
/// Slice 2.2 timelines: one trace per P2-D3 row (scene + events → samples at u = 0, ¼, ½, ¾, 1 of the track,
/// spike A style), plus the D7 snap, the A3 reset, determinism and the P2-D2 departure-key pins.
/// </summary>
public class TimelineTests
{
    private static readonly ItemType Rock = new("Plain Rock", Category.Material, true);
    private static readonly ItemType Wood = new("Rough Wood", Category.Material, true);

    /// <summary>A policy with durations divisible by 4, so quarter samples land on exact table points.</summary>
    private static readonly RealizationPolicy P = RealizationPolicy.Default with { WalkMs = 128, SlideMs = 128, ShakeMs = 144 };

    /// <summary>A bag of <paramref name="cols"/>×<paramref name="rows"/> with the given cells filled (Rock, then Wood alternating).</summary>
    private static Bag BagWith(int cols, int rows, params int[] filled) =>
        new(filled.Select((idx, k) => (idx, k)).Aggregate(Grid.Create(cols, rows),
            (g, t) => g.SetCell(t.idx, new Cell(new ItemStack(t.k % 2 == 0 ? Rock : Wood, 5)))));

    /// <summary>The rest scene of one bag: an optional body of B at <paramref name="body"/>, then one centred stack per non-empty cell.</summary>
    private static Scene SceneOf(Bag bag, Position? body = null, params Bag[] more)
    {
        var bodies = body is { } b
            ? new[] { new Pose(new EntityKey.Body(LocationId.B), bag.Id, b.Col, b.Row, 0, 0, null, Facing.S) }
            : Array.Empty<Pose>();
        var stacks = new[] { bag }.Concat(more).SelectMany(bg => bg.Grid.Cells.Select((c, i) => (c, i)).Where(t => !t.c.IsEmpty)
            .Select(t => new Pose(new EntityKey.Stack(new CellRef(bg.Id, t.i)), bg.Id, t.i % bg.Grid.Columns,
                t.i / bg.Grid.Columns, 0, 0, null, null)));
        return new Scene(bodies.Concat(stacks).ToImmutableArray())
        {
            Columns = new[] { bag }.Concat(more).ToImmutableDictionary(bg => bg.Id, bg => bg.Grid.Columns)
        };
    }

    /// <summary>The sample times u = 0, ¼, ½, ¾, 1 of a track of <paramref name="dur"/> ms.</summary>
    private static int[] Quarters(int dur) => [0, dur / 4, dur / 2, dur * 3 / 4, dur];

    /// <summary>The pose of <paramref name="key"/> in a sample (the first one when a key repeats).</summary>
    private static Pose One(ImmutableArray<Pose> poses, EntityKey key) => poses.First(p => p.Entity == key);

    /// <summary>The centre band (sub-row 2) of a one-row bag's zoomed render at the given times.</summary>
    private static string[] Centre(Timeline t, Bag bag, int[] times) =>
        times.Select(ms => ZoomAscii.Render(t.Sample(ms), bag).Split('\n')[2]).ToArray();

    private static EntityKey Body => new EntityKey.Body(LocationId.B);
    private static EntityKey Stack(Bag bag, int i) => new EntityKey.Stack(new CellRef(bag.Id, i));

    // ==================== World-bag walk ====================

    /// <summary>Row 1: a world-bag CursorMoved walks the body cell to cell, eased, facing the move direction.</summary>
    [Fact]
    public void Walk_Trace_EasedAcrossTheBoundary()
    {
        var bag = BagWith(3, 1);
        var t = Timeline.Rest(SceneOf(bag, new Position(0, 1)))
            .Commit([new SimEvent.CursorMoved(1, 0, LocationId.B, bag.Id, new Position(0, 1), new Position(0, 2))],
                SceneOf(bag, new Position(0, 2)), P);

        Assert.Equal(
            [
                "....|..@.|....", // u=0: centre of (1,0)
                "....|..@.|....", // u=¼: smooth is slow in (+2/16)
                "....|....|@...", // u=½: on the boundary (two-layer.md §L2 example)
                "....|....|.@..", // u=¾: −3/16 of (2,0)
                "....|....|..@."  // u=1: rest at (2,0)
            ],
            Centre(t, bag, Quarters(128)));
        Assert.All(Quarters(128), ms => Assert.Equal(Facing.E, One(t.Sample(ms), Body).Facing));
        Assert.Equal(Facing.E, t.Facings[LocationId.B]);
        Assert.Equal(128, t.SpanMs);
    }

    /// <summary>Row 1: the walk duration comes from the policy (default 125 ms).</summary>
    [Fact]
    public void Walk_UsesPolicyDuration()
    {
        var bag = BagWith(3, 1);
        var t = Timeline.Rest(SceneOf(bag, new Position(0, 0)))
            .Commit([new SimEvent.CursorMoved(1, 0, LocationId.B, bag.Id, new Position(0, 0), new Position(0, 1))],
                SceneOf(bag, new Position(0, 1)), RealizationPolicy.Default);
        Assert.Equal(125, t.SpanMs);
        var track = Assert.Single(t.Tracks);
        Assert.Equal((Body, 0, 125, Curve.Smooth, Channel.Motion), (track.Entity, track.StartMs, track.DurMs, track.Curve, track.Channel));
    }

    /// <summary>Row 1: a vertical walk faces N and moves on SubY.</summary>
    [Fact]
    public void Walk_Up_FacesNorth_MovesOnY()
    {
        var bag = BagWith(1, 3);
        var t = Timeline.Rest(SceneOf(bag, new Position(2, 0)))
            .Commit([new SimEvent.CursorMoved(1, 0, LocationId.B, bag.Id, new Position(2, 0), new Position(1, 0))],
                SceneOf(bag, new Position(1, 0)), P);
        var mid = One(t.Sample(64), Body);
        Assert.Equal((0, 0, 24, Facing.N), (mid.CellCol, mid.SubX, mid.CellRow * 16 + mid.SubY, mid.Facing));
    }

    /// <summary>Row 1: a wrap (an edge jump) snaps; it does not slide across the grid. Facing is still the move direction.</summary>
    [Fact]
    public void Walk_Wrap_Snaps()
    {
        var bag = BagWith(3, 1);
        var next = SceneOf(bag, new Position(0, 0));
        var t = Timeline.Rest(SceneOf(bag, new Position(0, 2)))
            .Commit([new SimEvent.CursorMoved(1, 0, LocationId.B, bag.Id, new Position(0, 2), new Position(0, 0))], next, P);
        Assert.Empty(t.Tracks);
        Assert.Equal(0, t.SpanMs);
        var body = One(t.Sample(0), Body);
        Assert.Equal((0, 0, 0, Facing.E), (body.CellCol, body.SubX, body.SubY, body.Facing));
    }

    /// <summary>Two walks in one commit play back to back, not on top of each other.</summary>
    [Fact]
    public void Walk_TwoInOneCommit_PlayInSequence()
    {
        var bag = BagWith(3, 1);
        var t = Timeline.Rest(SceneOf(bag, new Position(0, 0)))
            .Commit([
                new SimEvent.CursorMoved(1, 0, LocationId.B, bag.Id, new Position(0, 0), new Position(0, 1)),
                new SimEvent.CursorMoved(1, 1, LocationId.B, bag.Id, new Position(0, 1), new Position(0, 2))
            ], SceneOf(bag, new Position(0, 2)), P);
        Assert.Equal(256, t.SpanMs);
        Assert.Equal(["....|@...|....", "....|..@.|....", "....|....|@...", "....|....|..@."], Centre(t, bag, [64, 128, 192, 256]));
    }

    // ==================== Inventory cursor glide ====================

    /// <summary>Row 2: a CursorMoved in an inventory bag is a linear 60 ms cursor glide, drawn only while live.</summary>
    [Fact]
    public void CursorGlide_Inventory_Linear60()
    {
        var bag = BagWith(3, 1, 0);
        var scene = SceneOf(bag);
        var t = Timeline.Rest(scene)
            .Commit([new SimEvent.CursorMoved(1, 0, LocationId.B, bag.Id, new Position(0, 1), new Position(0, 2))], scene,
                RealizationPolicy.Default);
        var cursor = new EntityKey.Cursor(LocationId.B);
        var track = Assert.Single(t.Tracks);
        Assert.Equal((cursor, 60, Curve.Linear), (track.Entity, track.DurMs, track.Curve));
        Assert.Equal([16, 20, 24, 28], new[] { 0, 15, 30, 45 }.Select(ms => One(t.Sample(ms), cursor))
            .Select(p => p.CellCol * 16 + p.SubX).ToArray());
        Assert.DoesNotContain(t.Sample(60), p => p.Entity == cursor);
        Assert.Equal(scene.Poses, t.Sample(60));
    }

    // ==================== Bumps ====================

    /// <summary>Row 3: Bumped(Unnavigable) lunges the body 4/16 toward dir and back over 120 ms, facing dir.</summary>
    [Fact]
    public void Bump_Unnavigable_Lunge()
    {
        var bag = BagWith(3, 1);
        var scene = SceneOf(bag, new Position(0, 0));
        var t = Timeline.Rest(scene)
            .Commit([new SimEvent.Bumped(1, 0, LocationId.B, bag.Id, new Position(0, 0), Direction.Right, BumpReason.Unnavigable)],
                scene, RealizationPolicy.Default);
        Assert.Equal(120, t.SpanMs);
        Assert.Equal([0, 2, 4, 2, 0], Quarters(120).Select(ms => One(t.Sample(ms), Body).SubX).ToArray());
        Assert.All(Quarters(120), ms => Assert.Equal(Facing.E, One(t.Sample(ms), Body).Facing));
        Assert.Equal(["..@.|....|....", "..@.|....|....", "...@|....|....", "..@.|....|....", "..@.|....|...."],
            Centre(t, bag, Quarters(120)));
    }

    /// <summary>Row 3: an upward bump lunges on −Y and faces N.</summary>
    [Fact]
    public void Bump_Up_LungesNegativeY()
    {
        var bag = BagWith(1, 2);
        var scene = SceneOf(bag, new Position(1, 0));
        var t = Timeline.Rest(scene)
            .Commit([new SimEvent.Bumped(1, 0, LocationId.B, bag.Id, new Position(1, 0), Direction.Up, BumpReason.Unnavigable)], scene, P);
        var peak = One(t.Sample(60), Body);
        Assert.Equal((0, -4, Facing.N), (peak.SubX, peak.SubY, peak.Facing));
    }

    /// <summary>Row 4: Bumped(FailedPeek) shakes the stack at that cell ±2/16 on X, 3 cycles, over 150 ms.</summary>
    [Fact]
    public void Bump_FailedPeek_Shake()
    {
        var bag = BagWith(3, 1, 1);
        var scene = SceneOf(bag);
        var t = Timeline.Rest(scene)
            .Commit([new SimEvent.Bumped(1, 0, LocationId.B, bag.Id, new Position(0, 1), null, BumpReason.FailedPeek)], scene, P);
        var key = Stack(bag, 1);
        Assert.Equal(144, t.SpanMs);
        Assert.Equal([0, -2, 0, 2, 0], Quarters(144).Select(ms => One(t.Sample(ms), key).SubX).ToArray());
        Assert.Equal([0, 2, 0, -2, 0], new[] { 0, 12, 24, 36, 48 }.Select(ms => One(t.Sample(ms), key).SubX).ToArray());
        Assert.All(Quarters(144), ms => Assert.Equal(0, One(t.Sample(ms), key).SubY));
        Assert.Equal(150, Timeline.Rest(scene).Commit(
            [new SimEvent.Bumped(1, 0, LocationId.B, bag.Id, new Position(0, 1), null, BumpReason.FailedPeek)], scene,
            RealizationPolicy.Default).SpanMs);
    }

    // ==================== Stack moves within one bag ====================

    /// <summary>Row 5: StackMoved within one bag slides, keyed by the departure cell until the track ends (P2-D2).</summary>
    [Fact]
    public void StackMoved_SameBag_Slides_DepartureKey()
    {
        var before = BagWith(3, 1, 0);
        var after = BagWith(3, 1, 2) with { Id = before.Id };
        var t = Timeline.Rest(SceneOf(before)).Commit(
            [new SimEvent.StackMoved(1, 0, Rock, 5, new CellRef(before.Id, 0), new CellRef(before.Id, 2), StackCause.Dsl)],
            SceneOf(after), P);

        var mid = Quarters(128).Take(4).Select(ms => Assert.Single(t.Sample(ms))).ToList();
        Assert.All(mid, p => Assert.Equal(Stack(before, 0), p.Entity));
        Assert.Equal([(0, 0), (0, 5), (1, 0), (2, -5)], mid.Select(p => (p.CellCol, p.SubX)).ToArray());
        var end = Assert.Single(t.Sample(128));
        Assert.Equal((Stack(before, 2), 2, 0), (end.Entity, end.CellCol, end.SubX));
    }

    /// <summary>
    /// Row 5, the risky case (P2-D2 pin): a split that moves. The moving part keeps the departure key, so mid-slide
    /// two poses share <c>Stack(from)</c>: the remainder at rest and the moving part. At the end it is <c>Stack(to)</c>.
    /// </summary>
    [Fact]
    public void StackSplit_SameBag_MovingPartKeepsDepartureKey()
    {
        var bag = BagWith(3, 1, 0, 2);
        var t = Timeline.Rest(SceneOf(BagWith(3, 1, 0) with { Id = bag.Id })).Commit(
            [new SimEvent.StackSplit(1, 0, Rock, new CellRef(bag.Id, 0), new CellRef(bag.Id, 2), 3, 2, StackCause.Dsl)],
            SceneOf(bag), P);

        var mid = t.Sample(64);
        Assert.Equal(2, mid.Length);
        Assert.All(mid, p => Assert.Equal(Stack(bag, 0), p.Entity));
        Assert.Equal([(0, 0), (1, 0)], mid.Select(p => (p.CellCol, p.SubX)).OrderBy(x => x).ToArray());
        Assert.Equal([Stack(bag, 0), Stack(bag, 2)], t.Sample(128).Select(p => p.Entity).ToArray());
    }

    /// <summary>Row 5: a merge slides the moving part as an extra pose; the stack it merges into stays drawn at rest.</summary>
    [Fact]
    public void StackMerged_SameBag_SlidesOverTheTarget()
    {
        var bag = BagWith(3, 1, 2);
        var t = Timeline.Rest(SceneOf(BagWith(3, 1, 0, 2) with { Id = bag.Id })).Commit(
            [new SimEvent.StackMerged(1, 0, Rock, 5, new CellRef(bag.Id, 0), new CellRef(bag.Id, 2), StackCause.Dsl)],
            SceneOf(bag), P);

        var mid = t.Sample(64);
        Assert.Equal(2, mid.Length);
        Assert.Equal((2, 0), (One(mid, Stack(bag, 2)).CellCol, One(mid, Stack(bag, 2)).SubX));
        Assert.Equal((1, 0), (One(mid, Stack(bag, 0)).CellCol, One(mid, Stack(bag, 0)).SubX));
        Assert.Equal(SceneOf(bag).Poses, t.Sample(128));
    }

    /// <summary>A stack moved twice in one commit chains: one departure key, back-to-back slides, ends re-keyed.</summary>
    [Fact]
    public void StackMoved_Twice_Chains()
    {
        var bag = BagWith(3, 1, 2);
        var t = Timeline.Rest(SceneOf(BagWith(3, 1, 0) with { Id = bag.Id })).Commit([
            new SimEvent.StackMoved(1, 0, Rock, 5, new CellRef(bag.Id, 0), new CellRef(bag.Id, 1), StackCause.Dsl),
            new SimEvent.StackMoved(1, 1, Rock, 5, new CellRef(bag.Id, 1), new CellRef(bag.Id, 2), StackCause.Dsl)
        ], SceneOf(bag), P);
        Assert.Equal(256, t.SpanMs);
        Assert.Equal([(Stack(bag, 0), 16), (Stack(bag, 0), 24), (Stack(bag, 2), 32)],
            new[] { 128, 192, 256 }.Select(ms => Assert.Single(t.Sample(ms))).Select(p => (p.Entity, p.CellCol * 16 + p.SubX)).ToArray());
    }

    // ==================== Across bags ====================

    /// <summary>Row 6: a move across bags snaps on a Fade-channel track; every sample is the end (rest) pose.</summary>
    [Fact]
    public void StackMoved_AcrossBags_SnapsWithFade()
    {
        var grid = BagWith(3, 1);
        var hand = BagWith(1, 1, 0);
        var next = SceneOf(grid, null, hand);
        var t = Timeline.Rest(SceneOf(BagWith(3, 1, 1) with { Id = grid.Id }, null, BagWith(1, 1) with { Id = hand.Id }))
            .Commit([new SimEvent.StackMoved(1, 0, Rock, 5, new CellRef(grid.Id, 1), new CellRef(hand.Id, 0), StackCause.Grab)], next, P);
        var track = Assert.Single(t.Tracks);
        Assert.Equal((Channel.Fade, Stack(hand, 0)), (track.Channel, track.To.Entity));
        Assert.Equal((grid.Id, 1), (track.From.Bag, track.From.CellCol));
        Assert.All(Quarters(128), ms => Assert.Equal(next.Poses, t.Sample(ms)));
    }

    /// <summary>Row 6: splits and merges across bags snap the same way.</summary>
    [Fact]
    public void SplitAndMerge_AcrossBags_Snap()
    {
        var grid = BagWith(3, 1, 0);
        var hand = BagWith(1, 1, 0);
        var next = SceneOf(grid, null, hand);
        var t = Timeline.Rest(next).Commit([
            new SimEvent.StackSplit(1, 0, Rock, new CellRef(grid.Id, 0), new CellRef(hand.Id, 0), 3, 2, StackCause.Split),
            new SimEvent.StackMerged(1, 1, Rock, 1, new CellRef(hand.Id, 0), new CellRef(grid.Id, 0), StackCause.Drop)
        ], next, P);
        Assert.All(t.Tracks, k => Assert.Equal(Channel.Fade, k.Channel));
        Assert.All(Quarters(128), ms => Assert.Equal(next.Poses, t.Sample(ms)));
    }

    // ==================== Sort ====================

    /// <summary>Row 7: BagRearranged slides every moved stack to its new cell in parallel over 200 ms; unmoved stacks get no track.</summary>
    [Fact]
    public void BagRearranged_SlidesInParallel()
    {
        var after = BagWith(4, 1, 0, 2, 3);
        // new 0 ← old 1, new 1 empty, new 2 ← old 0, new 3 ← old 3 (unmoved)
        var t = Timeline.Rest(SceneOf(BagWith(4, 1, 0, 1, 3) with { Id = after.Id })).Commit(
            [new SimEvent.BagRearranged(1, 0, after.Id, [1, -1, 0, 3])], SceneOf(after), RealizationPolicy.Default);
        Assert.Equal(2, t.Tracks.Length);
        Assert.All(t.Tracks, k => Assert.Equal((0, 200), (k.StartMs, k.DurMs)));
        Assert.Equal(200, t.SpanMs);

        var mid = t.Sample(100);
        Assert.Equal([Stack(after, 1), Stack(after, 0), Stack(after, 3)], mid.Select(p => p.Entity).ToArray());
        Assert.Equal([(1, -8), (1, 0), (3, 0)], mid.Select(p => (p.CellCol, p.SubX)).ToArray());
        Assert.Equal(SceneOf(after).Poses, t.Sample(200));
    }

    // ==================== Pop (height) ====================

    /// <summary>Row 8: Acquired pops the target cell's stack: Height 0 → 6 → 0 over 160 ms.</summary>
    [Fact]
    public void Acquired_Pops()
    {
        var bag = BagWith(3, 1, 1);
        var scene = SceneOf(bag);
        var t = Timeline.Rest(SceneOf(BagWith(3, 1) with { Id = bag.Id }))
            .Commit([new SimEvent.Acquired(1, 0, Rock, 5, new CellRef(bag.Id, 1), StackCause.Acquire)], scene, P);
        var track = Assert.Single(t.Tracks);
        Assert.Equal((Channel.Height, 160), (track.Channel, track.DurMs));
        Assert.Equal(new int?[] { 0, 3, 6, 3, null }, Quarters(160).Select(ms => One(t.Sample(ms), Stack(bag, 1)).Height).ToArray());
        Assert.All(Quarters(160), ms => Assert.Equal((1, 0, 0), (One(t.Sample(ms), Stack(bag, 1)).CellCol,
            One(t.Sample(ms), Stack(bag, 1)).SubX, One(t.Sample(ms), Stack(bag, 1)).SubY)));
    }

    /// <summary>Row 8: Harvested pops the harvested cell and every shown yield cell; unshown yields get no track.</summary>
    [Fact]
    public void Harvested_PopsCellAndShownYields()
    {
        var world = BagWith(3, 1, 0);
        var parent = BagWith(3, 1, 2);
        var unshown = Guid.NewGuid();
        var scene = SceneOf(world, new Position(0, 1), parent);
        var t = Timeline.Rest(scene).Commit([
            new SimEvent.Harvested(1, 0, new CellRef(world.Id, 0),
                [new Placed(Wood, 2, new CellRef(parent.Id, 2)), new Placed(Wood, 1, new CellRef(unshown, 0))])
        ], scene, P);
        Assert.Equal(2, t.Tracks.Length);
        Assert.Equal(6, One(t.Sample(80), Stack(world, 0)).Height);
        Assert.Equal(6, One(t.Sample(80), Stack(parent, 2)).Height);
        Assert.Null(One(t.Sample(80), Body).Height);
    }

    // ==================== Rewound (A3) and no-track events ====================

    /// <summary>Row 9 (A3): Rewound mid-walk drops every track; the body is at its restored cell centre.</summary>
    [Fact]
    public void Rewound_MidWalk_ResetsToCellCentre()
    {
        var bag = BagWith(3, 1);
        var walking = Timeline.Rest(SceneOf(bag, new Position(0, 1)))
            .Commit([new SimEvent.CursorMoved(1, 0, LocationId.B, bag.Id, new Position(0, 1), new Position(0, 2))],
                SceneOf(bag, new Position(0, 2)), P);
        Assert.NotEqual(0, One(walking.Sample(40), Body).SubX);

        var restored = SceneOf(bag, new Position(0, 1));
        var t = walking.Commit([new SimEvent.Rewound(2, 0, 0)], restored, P);
        Assert.Empty(t.Tracks);
        var body = One(t.Sample(0), Body);
        Assert.Equal((1, 0, 0, 0), (body.CellCol, body.CellRow, body.SubX, body.SubY));
        Assert.Equal(restored.Poses.Select(p => p with { Facing = p.Entity is EntityKey.Body ? Facing.E : null }), t.Sample(0));
    }

    /// <summary>Row 9: a Rewound later in the same commit also drops that commit's earlier tracks.</summary>
    [Fact]
    public void Rewound_DropsTracksEarlierInTheSameCommit()
    {
        var bag = BagWith(3, 1, 1);
        var scene = SceneOf(bag, new Position(0, 0));
        var t = Timeline.Rest(scene).Commit([
            new SimEvent.Bumped(1, 0, LocationId.B, bag.Id, new Position(0, 0), Direction.Right, BumpReason.Unnavigable),
            new SimEvent.Rewound(1, 1, 0)
        ], scene, P);
        Assert.Empty(t.Tracks);
    }

    /// <summary>Row 10: crafting, dialogue, chrome, time and panel events add no track (an n = 1 re-render).</summary>
    [Fact]
    public void OtherEvents_NoTrack()
    {
        var bag = BagWith(3, 1, 0);
        var scene = SceneOf(bag);
        var cell = new CellRef(bag.Id, 0);
        SimEvent[] events =
        [
            new SimEvent.IntentApplied(1, 0, new Intent.Tick()),
            new SimEvent.TimeAdvanced(1, 1),
            new SimEvent.CraftStarted(1, 2, bag.Id, "r", cell),
            new SimEvent.CraftProgressed(1, 3, bag.Id, "r", 2, cell),
            new SimEvent.PlantGrew(1, 4, cell, 1, 3),
            new SimEvent.DialogueQueued(1, 5, "b"),
            new SimEvent.PanelOpened(1, 6, LocationId.C, bag.Id),
            new SimEvent.BagEntered(1, 7, LocationId.B, bag.Id, 1),
            new SimEvent.StackRemoved(1, 8, Rock, 1, cell, StackCause.Dsl),
            new SimEvent.ZoneReached(1, 9, bag.Id),
            new SimEvent.Bumped(1, 10, LocationId.B, bag.Id, new Position(0, 0), null, BumpReason.Unnavigable)
        ];
        var t = Timeline.Rest(scene).Commit(events, scene, P);
        Assert.Empty(t.Tracks);
        Assert.Equal(scene.Poses, t.Sample(0));
    }

    /// <summary>Events about bags the scene does not show add no track.</summary>
    [Fact]
    public void UnshownBag_NoTrack()
    {
        var bag = BagWith(3, 1, 0);
        var other = Guid.NewGuid();
        var scene = SceneOf(bag);
        var t = Timeline.Rest(scene).Commit([
            new SimEvent.CursorMoved(1, 0, LocationId.B, other, new Position(0, 0), new Position(0, 1)),
            new SimEvent.StackMoved(1, 1, Rock, 1, new CellRef(other, 0), new CellRef(other, 1), StackCause.Dsl),
            new SimEvent.BagRearranged(1, 2, other, [1, 0])
        ], scene, P);
        Assert.Empty(t.Tracks);
    }

    // ==================== D7 snap, determinism ====================

    /// <summary>D7: committing step 2 at 40 ms into step 1's walk snaps it; step 2's sample at 0 ms shows step 1's end pose.</summary>
    [Fact]
    public void Snap_CommitMidWalk_StartsFromStep1EndPose()
    {
        var bag = BagWith(4, 1);
        var step1 = Timeline.Rest(SceneOf(bag, new Position(0, 1)))
            .Commit([new SimEvent.CursorMoved(1, 0, LocationId.B, bag.Id, new Position(0, 1), new Position(0, 2))],
                SceneOf(bag, new Position(0, 2)), P);
        var at40 = One(step1.Sample(40), Body);
        Assert.False(at40 is { CellCol: 2, SubX: 0 }, "step 1 is mid-walk at 40 ms");

        var step2 = step1.Commit([new SimEvent.CursorMoved(2, 0, LocationId.B, bag.Id, new Position(0, 2), new Position(0, 3))],
            SceneOf(bag, new Position(0, 3)), P);
        var start = One(step2.Sample(0), Body);
        Assert.Equal((2, 0, 0), (start.CellCol, start.SubX, start.SubY));
        Assert.Single(step2.Tracks);

        var idle = step1.Commit([], SceneOf(bag, new Position(0, 2)), P);
        Assert.Empty(idle.Tracks);
        Assert.Equal((2, 0), (One(idle.Sample(0), Body).CellCol, One(idle.Sample(0), Body).SubX));
        Assert.Equal(Facing.E, One(idle.Sample(0), Body).Facing);
    }

    /// <summary>Determinism: the same inputs give an identical PoseText stream across two runs (fresh bag ids each run).</summary>
    [Fact]
    public void SameInputs_IdenticalPoseTextStream()
    {
        static IEnumerable<string> Run()
        {
            var bag = BagWith(4, 2, 0, 5);
            var after = BagWith(4, 2, 1, 5) with { Id = bag.Id };
            var t = Timeline.Rest(SceneOf(bag, new Position(1, 0)));
            var text = new PoseText();
            var commits = new (SimEvent[] Events, Scene Next)[]
            {
                ([new SimEvent.CursorMoved(1, 0, LocationId.B, bag.Id, new Position(1, 0), new Position(1, 1))], SceneOf(bag, new Position(1, 1))),
                ([new SimEvent.StackMoved(2, 0, Rock, 5, new CellRef(bag.Id, 0), new CellRef(bag.Id, 1), StackCause.Dsl)], SceneOf(after, new Position(1, 1))),
                ([new SimEvent.Bumped(3, 0, LocationId.B, bag.Id, new Position(1, 1), Direction.Down, BumpReason.Unnavigable)], SceneOf(after, new Position(1, 1))),
                ([new SimEvent.Acquired(4, 0, Wood, 5, new CellRef(bag.Id, 5), StackCause.Acquire)], SceneOf(after, new Position(1, 1)))
            };
            foreach (var (events, next) in commits)
            {
                t = t.Commit(events, next, RealizationPolicy.Default);
                var span = t.SpanMs;
                foreach (var ms in Quarters(span))
                    foreach (var p in t.Sample(ms))
                        yield return $"{ms} {text.Line(p)}";
            }
        }

        var a = Run().ToList();
        Assert.NotEmpty(a);
        Assert.Equal(a, Run().ToList());
    }

    /// <summary>Sampling is total: before 0 and long after the span both give sensible poses.</summary>
    [Fact]
    public void Sample_ClampsOutOfRangeTimes()
    {
        var bag = BagWith(3, 1);
        var next = SceneOf(bag, new Position(0, 2));
        var t = Timeline.Rest(SceneOf(bag, new Position(0, 1)))
            .Commit([new SimEvent.CursorMoved(1, 0, LocationId.B, bag.Id, new Position(0, 1), new Position(0, 2))], next, P);
        Assert.Equal((1, 0), (One(t.Sample(-5), Body).CellCol, One(t.Sample(-5), Body).SubX));
        Assert.Equal(t.Scene.Poses, t.Sample(int.MaxValue));
    }

    /// <summary>A rest timeline has no tracks and samples its scene.</summary>
    [Fact]
    public void Rest_SamplesTheScene()
    {
        var scene = SceneOf(BagWith(2, 1, 1), new Position(0, 0));
        var t = Timeline.Rest(scene);
        Assert.Empty(t.Tracks);
        Assert.Equal(0, t.SpanMs);
        Assert.Equal(scene.Poses, t.Sample(0));
    }
}
