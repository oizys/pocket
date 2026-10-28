using System.Collections.Immutable;
using Pockets.Core.Models;
using Pockets.Core.Sim;

namespace Pockets.Realization.Tests;

/// <summary>
/// Slice 2.3 rogue choreography and the realtime path: phase classification by event kind and position, the phase
/// layout (player at 0, reactions and ambient from the policy's overlap of the player phase), and P2-D13 (a player
/// commit snaps, a clock commit keeps live tracks rebased by the elapsed ms).
/// </summary>
public class ChoreographyTests
{
    private static readonly ItemType Rock = new("Plain Rock", Category.Material, true);
    private static readonly ItemType Wood = new("Rough Wood", Category.Material, true);

    /// <summary>A policy with durations divisible by 4 (walk and slide 128 ms).</summary>
    private static readonly RealizationPolicy P = RealizationPolicy.Default with { WalkMs = 128, SlideMs = 128 };

    /// <summary>A bag of <paramref name="cols"/>×<paramref name="rows"/> with the given cells filled with Rock.</summary>
    private static Bag BagWith(int cols, int rows, params int[] filled) =>
        new(filled.Aggregate(Grid.Create(cols, rows), (g, idx) => g.SetCell(idx, new Cell(new ItemStack(Rock, 5)))));

    /// <summary>The rest scene: an optional body of B in <paramref name="bag"/>, then one centred stack per non-empty cell of every bag.</summary>
    private static Scene SceneOf(Bag bag, Position? body = null, params Bag[] more)
    {
        var bags = new[] { bag }.Concat(more).ToList();
        var bodies = body is { } b
            ? new[] { new Pose(new EntityKey.Body(LocationId.B), bag.Id, b.Col, b.Row, 0, 0, null, Facing.S) }
            : Array.Empty<Pose>();
        var stacks = bags.SelectMany(bg => bg.Grid.Cells.Select((c, i) => (c, i)).Where(t => !t.c.IsEmpty)
            .Select(t => new Pose(new EntityKey.Stack(new CellRef(bg.Id, t.i)), bg.Id, t.i % bg.Grid.Columns,
                t.i / bg.Grid.Columns, 0, 0, null, null)));
        return new Scene(bodies.Concat(stacks).ToImmutableArray())
        {
            Columns = bags.ToImmutableDictionary(bg => bg.Id, bg => bg.Grid.Columns)
        };
    }

    /// <summary>The pose of <paramref name="key"/> in a sample (the first one when a key repeats).</summary>
    private static Pose One(ImmutableArray<Pose> poses, EntityKey key) => poses.First(p => p.Entity == key);

    private static EntityKey Body => new EntityKey.Body(LocationId.B);
    private static EntityKey Stack(Bag bag, int i) => new EntityKey.Stack(new CellRef(bag.Id, i));

    /// <summary>A world-bag walk of B from (0, <paramref name="from"/>) to (0, <paramref name="to"/>), as a player Move turn.</summary>
    private static SimEvent[] Walk(int step, Bag bag, int from, int to) =>
    [
        new SimEvent.IntentApplied(step, 0, new Intent.Move(LocationId.B, Direction.Right)),
        new SimEvent.CursorMoved(step, 1, LocationId.B, bag.Id, new Position(0, from), new Position(0, to))
    ];

    // ==================== Phase classification ====================

    /// <summary>Envelope and own changes are Player; Bumped/Harvested/rule events after them are Reaction; TimeAdvanced and later are Ambient.</summary>
    [Fact]
    public void Phases_ByKindAndPosition()
    {
        var bag = Guid.NewGuid();
        var cell = new CellRef(bag, 0);
        SimEvent[] events =
        [
            new SimEvent.IntentApplied(1, 0, new Intent.Primary(LocationId.B)),
            new SimEvent.CursorMoved(1, 1, LocationId.B, bag, new Position(0, 0), new Position(0, 1)),
            new SimEvent.StackMoved(1, 2, Rock, 1, cell, new CellRef(bag, 1), StackCause.Grab),
            new SimEvent.Harvested(1, 3, cell, []),
            new SimEvent.Bumped(1, 4, LocationId.B, bag, new Position(0, 1), Direction.Right, BumpReason.Unnavigable),
            new SimEvent.TimeAdvanced(1, 5),
            new SimEvent.PlantGrew(1, 6, cell, 1, 3),
            new SimEvent.Acquired(1, 7, Rock, 1, cell, StackCause.Acquire),
            new SimEvent.Harvested(1, 8, cell, [])
        ];
        Assert.Equal(
            [
                TurnPhase.Player, TurnPhase.Player, TurnPhase.Player, TurnPhase.Reaction, TurnPhase.Reaction,
                TurnPhase.Ambient, TurnPhase.Ambient, TurnPhase.Ambient, TurnPhase.Ambient
            ],
            Choreography.Phases(events));
    }

    /// <summary>A new envelope (or a new step) ends the ambient run; a bare Bumped opening a step is a reaction.</summary>
    [Fact]
    public void Phases_NewEnvelopeOrStep_EndsAmbient()
    {
        var bag = Guid.NewGuid();
        SimEvent[] events =
        [
            new SimEvent.TimeAdvanced(1, 0),
            new SimEvent.PlantGrew(1, 1, new CellRef(bag, 0), 1, 3),
            new SimEvent.IntentApplied(2, 0, new Intent.Sort(LocationId.B)),
            new SimEvent.BagRearranged(2, 1, bag, [1, 0]),
            new SimEvent.TimeAdvanced(2, 2),
            new SimEvent.Bumped(3, 0, LocationId.B, bag, new Position(0, 0), null, BumpReason.FailedPeek),
            new SimEvent.Acquired(3, 1, Rock, 1, new CellRef(bag, 0), StackCause.Acquire)
        ];
        Assert.Equal(
            [
                TurnPhase.Ambient, TurnPhase.Ambient, TurnPhase.Player, TurnPhase.Player, TurnPhase.Ambient,
                TurnPhase.Reaction, TurnPhase.Player
            ],
            Choreography.Phases(events));
    }

    /// <summary>A clock commit holds only clock envelopes (Tick, SetElapsed) and ambient runs; anything else, or nothing, is player input.</summary>
    [Fact]
    public void IsClock_OnlyClockEnvelopesAndAmbient()
    {
        var bag = Guid.NewGuid();
        Assert.True(Choreography.IsClock([new SimEvent.TimeAdvanced(1, 0), new SimEvent.PlantGrew(1, 1, new CellRef(bag, 0), 1, 3)]));
        Assert.True(Choreography.IsClock([new SimEvent.IntentApplied(1, 0, new Intent.SetElapsed(TimeSpan.FromSeconds(5)))]));
        Assert.True(Choreography.IsClock([new SimEvent.IntentApplied(1, 0, new Intent.Tick()), new SimEvent.TimeAdvanced(1, 1)]));
        Assert.True(Choreography.IsClock([
            new SimEvent.IntentApplied(4, 0, new Intent.SetElapsed(TimeSpan.FromSeconds(16))),
            new SimEvent.TimeAdvanced(4, 0),
            new SimEvent.CraftStarted(4, 1, bag, "r", null)
        ]));

        Assert.False(Choreography.IsClock([]));
        Assert.False(Choreography.IsClock(Walk(1, BagWith(3, 1), 0, 1)));
        Assert.False(Choreography.IsClock([new SimEvent.Rewound(2, 0, 1)]));
        Assert.False(Choreography.IsClock([
            new SimEvent.Bumped(1, 0, LocationId.B, bag, new Position(0, 0), null, BumpReason.FailedPeek)
        ]));
        Assert.False(Choreography.IsClock([
            new SimEvent.IntentApplied(1, 0, new Intent.Grab(LocationId.B)),
            new SimEvent.TimeAdvanced(1, 1)
        ]));
    }

    // ==================== Rogue turns ====================

    /// <summary>Rogue grab-then-tick: the grab's fade plays at 0, the tick's ambient pop starts at 50% of it; the commit snaps old tracks.</summary>
    [Fact]
    public void Rogue_GrabThenTick_AmbientFromHalfOfPlayer()
    {
        var bag = BagWith(3, 1, 0, 2);
        var hand = BagWith(1, 1);
        var after = BagWith(3, 1, 2) with { Id = bag.Id };
        var handAfter = BagWith(1, 1, 0) with { Id = hand.Id };
        var walking = Timeline.Rest(SceneOf(BagWith(3, 1) with { Id = bag.Id }, new Position(0, 0)))
            .Commit(Walk(1, bag, 0, 1), SceneOf(BagWith(3, 1) with { Id = bag.Id }, new Position(0, 1)), P);

        var t = walking.Commit([
            new SimEvent.IntentApplied(2, 0, new Intent.Grab(LocationId.B)),
            new SimEvent.StackMoved(2, 1, Rock, 5, new CellRef(bag.Id, 0), new CellRef(hand.Id, 0), StackCause.Grab),
            new SimEvent.TimeAdvanced(2, 2),
            new SimEvent.PlantGrew(2, 3, new CellRef(bag.Id, 2), 1, 3),
            new SimEvent.Acquired(2, 4, Rock, 1, new CellRef(bag.Id, 2), StackCause.Acquire)
        ], SceneOf(after, null, handAfter), P, atMs: 40);

        Assert.DoesNotContain(t.Tracks, k => k.Entity is EntityKey.Body);
        Assert.Equal(
            [(Channel.Fade, 0, 128), (Channel.Height, 64, 160)],
            t.Tracks.Select(k => (k.Channel, k.StartMs, k.DurMs)).ToArray());
        Assert.Equal(new int?[] { 0, 0, 6, null }, new[] { 0, 63, 144, 224 }.Select(ms => One(t.Sample(ms), Stack(bag, 2)).Height).ToArray());
        Assert.Equal(224, t.SpanMs);
    }

    /// <summary>A walk into a tree: the walk is refused, so only the lunge plays, starting at 0.</summary>
    [Fact]
    public void WalkIntoTree_OnlyTheLunge_AtZero()
    {
        var bag = BagWith(3, 1, 1);
        var scene = SceneOf(bag, new Position(0, 0));
        var t = Timeline.Rest(scene).Commit([
            new SimEvent.IntentApplied(1, 0, new Intent.Move(LocationId.B, Direction.Right)),
            new SimEvent.Bumped(1, 1, LocationId.B, bag.Id, new Position(0, 0), Direction.Right, BumpReason.Unnavigable)
        ], scene, P);

        var track = Assert.Single(t.Tracks);
        Assert.Equal((Body, 0, 120, Curve.PingPong), (track.Entity, track.StartMs, track.DurMs, track.Curve));
        Assert.Equal([0, 2, 4, 2, 0], new[] { 0, 30, 60, 90, 120 }.Select(ms => One(t.Sample(ms), Body).SubX).ToArray());
    }

    /// <summary>A harvest after a walk: the walk plays at 0, the pops (tree cell and shown yield) start at 50% of the walk.</summary>
    [Fact]
    public void Harvest_WalkThenPopAtHalf()
    {
        var world = BagWith(3, 1, 1);
        var parent = BagWith(3, 1, 2);
        var before = SceneOf(world, new Position(0, 0), parent);
        var after = SceneOf(world, new Position(0, 1), parent);
        SimEvent[] events =
        [
            new SimEvent.IntentApplied(1, 0, new Intent.Click(LocationId.B, new Position(0, 1), ClickType.Primary)),
            new SimEvent.CursorMoved(1, 1, LocationId.B, world.Id, new Position(0, 0), new Position(0, 1)),
            new SimEvent.Harvested(1, 2, new CellRef(world.Id, 1), [new Placed(Wood, 2, new CellRef(parent.Id, 2))])
        ];
        var t = Timeline.Rest(before).Commit(events, after, P);

        Assert.Equal(
            [(Channel.Motion, 0, 128), (Channel.Height, 64, 160), (Channel.Height, 64, 160)],
            t.Tracks.Select(k => (k.Channel, k.StartMs, k.DurMs)).ToArray());
        Assert.Equal((0, 0), (One(t.Sample(64), Stack(world, 1)).Height ?? 0, One(t.Sample(64), Stack(parent, 2)).Height ?? 0));
        Assert.Equal((6, 6), (One(t.Sample(144), Stack(world, 1)).Height ?? 0, One(t.Sample(144), Stack(parent, 2)).Height ?? 0));
        Assert.Equal((1, 0), (One(t.Sample(144), Body).CellCol, One(t.Sample(144), Body).SubX));
        Assert.Equal(after.Poses.Select(p => p.Entity is EntityKey.Body ? p with { Facing = Facing.E } : p), t.Sample(224));

        // Classification is by kind and position, not intent type: the same turn as SetCursor + Primary lays out identically.
        var split = Timeline.Rest(before).Commit([
            new SimEvent.IntentApplied(1, 0, new Intent.SetCursor(LocationId.B, new Position(0, 1))),
            events[1],
            new SimEvent.IntentApplied(1, 2, new Intent.Primary(LocationId.B)),
            events[2] with { Seq = 3 }
        ], after, P);
        Assert.Equal(t.Tracks, split.Tracks);
    }

    /// <summary>The reaction offset is the policy's overlap percent of the player phase.</summary>
    [Theory]
    [InlineData(0, 0)]
    [InlineData(50, 64)]
    [InlineData(100, 128)]
    public void ReactionStart_IsPolicyPercentOfPlayerPhase(int percent, int start)
    {
        var world = BagWith(3, 1, 1);
        var scene = SceneOf(world, new Position(0, 1));
        var t = Timeline.Rest(SceneOf(world, new Position(0, 0))).Commit([
            .. Walk(1, world, 0, 1),
            new SimEvent.Harvested(1, 2, new CellRef(world.Id, 1), [])
        ], scene, P with { ReactionOverlapPercent = percent });
        Assert.Equal(start, t.Tracks.Single(k => k.Channel == Channel.Height).StartMs);
    }

    // ==================== Realtime (P2-D13) ====================

    /// <summary>A clock tick landing 40 ms into a walk keeps the walk, rebased: its 0 ms sample is the walk at 40 ms.</summary>
    [Fact]
    public void Realtime_TickMidWalk_DoesNotSnap()
    {
        var bag = BagWith(4, 1);
        var scene = SceneOf(bag, new Position(0, 2));
        var walking = Timeline.Rest(SceneOf(bag, new Position(0, 1))).Commit(Walk(1, bag, 1, 2), scene, P);

        var t = walking.Commit([
            new SimEvent.IntentApplied(2, 0, new Intent.SetElapsed(TimeSpan.FromSeconds(1))),
            new SimEvent.TimeAdvanced(2, 0),
            new SimEvent.PlantGrew(2, 1, new CellRef(bag.Id, 3), 1, 3)
        ], scene, P, atMs: 40);

        Assert.Equal(88, t.SpanMs);
        Assert.Equal(walking.Sample(40), t.Sample(0));
        Assert.Equal(walking.Sample(100), t.Sample(60));
        Assert.Equal(walking.Sample(128), t.Sample(88));
    }

    /// <summary>A player key landing 40 ms into a walk snaps it (D7), whatever the elapsed time.</summary>
    [Fact]
    public void Realtime_KeyMidWalk_Snaps()
    {
        var bag = BagWith(4, 1);
        var walking = Timeline.Rest(SceneOf(bag, new Position(0, 1))).Commit(Walk(1, bag, 1, 2), SceneOf(bag, new Position(0, 2)), P);

        var t = walking.Commit(Walk(2, bag, 2, 3), SceneOf(bag, new Position(0, 3)), P, atMs: 40);

        var track = Assert.Single(t.Tracks);
        Assert.Equal(0, track.StartMs);
        Assert.Equal((2, 0), (One(t.Sample(0), Body).CellCol, One(t.Sample(0), Body).SubX));
    }

    /// <summary>A tick adds its ambient tracks at 0 next to the kept walk; tracks finished by the tick are dropped.</summary>
    [Fact]
    public void Realtime_TickAddsAmbient_AndDropsFinishedTracks()
    {
        var bag = BagWith(4, 1, 3);
        var scene = SceneOf(bag, new Position(0, 2));
        var walking = Timeline.Rest(SceneOf(bag, new Position(0, 1))).Commit(Walk(1, bag, 1, 2), scene, P);
        SimEvent[] tick =
        [
            new SimEvent.TimeAdvanced(2, 0),
            new SimEvent.Acquired(2, 1, Rock, 1, new CellRef(bag.Id, 3), StackCause.Acquire)
        ];

        var mid = walking.Commit(tick, scene, P, atMs: 40);
        Assert.Equal(
            [(Channel.Motion, -40, 128), (Channel.Height, 0, 160)],
            mid.Tracks.Select(k => (k.Channel, k.StartMs, k.DurMs)).ToArray());
        Assert.Equal(walking.Sample(40).First(p => p.Entity == Body), One(mid.Sample(0), Body));
        Assert.Equal(6, One(mid.Sample(80), Stack(bag, 3)).Height);

        var late = walking.Commit(tick, scene, P, atMs: 200);
        Assert.Equal([Channel.Height], late.Tracks.Select(k => k.Channel).ToArray());
    }

    /// <summary>The default atMs is 0: a clock commit with no elapsed time keeps the tracks exactly where they were.</summary>
    [Fact]
    public void Realtime_TickWithoutElapsed_KeepsTracksInPlace()
    {
        var bag = BagWith(4, 1);
        var scene = SceneOf(bag, new Position(0, 2));
        var walking = Timeline.Rest(SceneOf(bag, new Position(0, 1))).Commit(Walk(1, bag, 1, 2), scene, P);
        var t = walking.Commit([new SimEvent.TimeAdvanced(2, 0)], scene, P);
        Assert.Equal(walking.Tracks, t.Tracks);
    }
}
