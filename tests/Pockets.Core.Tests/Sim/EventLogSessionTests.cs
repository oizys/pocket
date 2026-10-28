using System.Collections.Immutable;
using System.Text.Json.Nodes;
using Pockets.Core.Data;
using Pockets.Core.Models;
using Pockets.Core.Rendering;
using Pockets.Core.Sim;

namespace Pockets.Core.Tests.Sim;

/// <summary>
/// Slice 1.8 acceptance 1, session level: the shell's <see cref="GameSession.EventLog"/> holds every step's
/// events; undo truncates it to the snapshot's mark and appends one <see cref="SimEvent.Rewound"/>; retention
/// follows the oldest retained snapshot (the undo stack in rogue, the checkpoint ring in realtime); the cap
/// leaves a marker. Also pins what 1.8 deliberately keeps: the intent log is not truncated (undo replays),
/// and the known swapped-frame undo snapshot (Slice 1.3).
/// </summary>
public class EventLogSessionTests
{
    private static readonly ItemType Rock = new("Rock", Category.Material, IsStackable: true, MaxStackSize: 20);
    private static readonly ItemType FacilityType = new("Workbench", Category.Structure, IsStackable: false);
    private static readonly ImmutableArray<ItemType> Types = ImmutableArray.Create(Rock, FacilityType);

    /// <summary>A 4×2 root: rocks at cells 1–3, a Workbench (holding 6 rocks) at cell 0; cursor at (0,0).</summary>
    private static GameState MakeState(out Guid facilityId)
    {
        var facility = new Bag(Grid.Create(3, 1).SetCell(0, new Cell(new ItemStack(Rock, 6))), "Workbench",
            FacilityState: new FacilityState());
        facilityId = facility.Id;
        var grid = Grid.Create(4, 2)
            .SetCell(0, new Cell(new ItemStack(FacilityType, 1, ContainedBagId: facility.Id)))
            .SetCell(1, new Cell(new ItemStack(Rock, 5)))
            .SetCell(2, new Cell(new ItemStack(Rock, 7)))
            .SetCell(3, new Cell(new ItemStack(Rock, 9)));
        var root = new Bag(grid);
        var hand = GameState.CreateHandBag();
        return new GameState(BagStore.Empty.Add(root).Add(hand).Add(facility), LocationMap.Create(hand.Id, root.Id), Types);
    }

    private static readonly ItemType Wood = new("Wood", Category.Material, IsStackable: true, MaxStackSize: 20);
    private static readonly ItemType Axe = new("Axe", Category.Tool, IsStackable: false);

    /// <summary>A craft long enough that every tick of a test progresses it (so every tick is a step).</summary>
    private static readonly Recipe LongAxe = new(
        "long_axe", "Axe", new[] { new RecipeInput(Rock, 5), new RecipeInput(Wood, 3) },
        _ => RecipeOutput.FromStacks(new[] { new ItemStack(Axe, 1) }), Duration: 100_000);

    /// <summary>
    /// A root with rocks at cell 0 (under the cursor) and a loaded Workbench crafting <see cref="LongAxe"/> at
    /// cell 1: each tick progresses the craft, so realtime ticks advance the step count.
    /// </summary>
    private static GameSession CraftingSession(TickMode mode, EventLogPolicy policy)
    {
        var workbench = FacilityBuilder.CreateWorkbench(LongAxe);
        workbench = workbench with
        {
            Grid = workbench.Grid
                .SetCell(0, workbench.Grid.GetCell(0) with { Stack = new ItemStack(Rock, 7) })
                .SetCell(1, workbench.Grid.GetCell(1) with { Stack = new ItemStack(Wood, 3) })
        };
        var owner = new ItemStack(FacilityType, 1, ContainedBagId: workbench.Id);
        var root = new Bag(Grid.Create(4, 2)
            .SetCell(0, new Cell(new ItemStack(Rock, 5)))
            .SetCell(1, new Cell(owner)));
        var hand = GameState.CreateHandBag();
        var state = new GameState(BagStore.Empty.Add(root).Add(hand).Add(workbench), LocationMap.Create(hand.Id, root.Id),
            ImmutableArray.Create(Rock, Wood, Axe, FacilityType));
        var map = ImmutableDictionary<string, ImmutableArray<string>>.Empty.Add(workbench.EnvironmentType, ImmutableArray.Create(LongAxe.Id));
        return GameSession.New(state, ImmutableArray.Create(LongAxe), map, mode) with { LogPolicy = policy };
    }

    private static GameSession NewSession(TickMode mode = TickMode.Rogue, EventLogPolicy? policy = null, int maxUndo = 1000) =>
        GameSession.New(MakeState(out _), ImmutableArray<Recipe>.Empty, mode, maxUndo) with
        {
            LogPolicy = policy ?? EventLogPolicy.Default
        };

    /// <summary>Applies <paramref name="intents"/> in order, also returning every event they emitted.</summary>
    private static (GameSession Session, List<SimEvent> Events) Run(GameSession session, params Intent[] intents)
    {
        var all = new List<SimEvent>();
        foreach (var intent in intents)
        {
            (session, _, var events) = session.ApplyTraced(intent);
            all.AddRange(events);
        }
        return (session, all);
    }

    private static readonly Intent Right = new Intent.Move(LocationId.B, Direction.Right);
    private static readonly Intent Grab = new Intent.Grab(LocationId.B);
    private static readonly Intent Drop = new Intent.Drop(LocationId.B);
    private static readonly Intent Undo = new Intent.Undo();

    // ==================== Append ====================

    [Fact]
    public void Steps_AppendTheirEvents_InEmissionOrder()
    {
        var (session, emitted) = Run(NewSession(), Right, Grab, Right, Drop, new Intent.Tick());

        Assert.NotEmpty(emitted);
        Assert.Equal(emitted, session.EventLog.Events);
    }

    [Fact]
    public void NoOpIntent_LeavesTheLogInstance()
    {
        var session = NewSession();
        Assert.Same(session.EventLog, session.Apply(Drop).EventLog); // empty hand: nothing to drop
    }

    // ==================== Undo ====================

    [Fact]
    public void Undo_TruncatesToTheSnapshotMark_AndAppendsOneRewound()
    {
        var (before, kept) = Run(NewSession(), Right);
        var (afterGrab, _) = Run(before, Grab, Right); // the move after the grab pushes no snapshot

        var (undone, events) = Run(afterGrab, Undo);

        var rewound = Assert.IsType<SimEvent.Rewound>(Assert.Single(events));
        Assert.Equal(new SimEvent.Rewound(afterGrab.TickCount + 1, 0, before.TickCount), rewound);
        Assert.Equal(kept.Append(rewound), undone.EventLog.Events);
    }

    [Fact]
    public void Undo_OfACompositeClick_KeepsTheCursorMoveBeforeTheSnapshot()
    {
        var click = new Intent.Click(LocationId.B, new Position(0, 2), ClickType.Primary);
        var (clicked, _) = Run(NewSession(), click);
        Assert.True(clicked.Current.HasItemsInHand);

        var (undone, _) = Run(clicked, Undo);

        var log = undone.EventLog.Events.ToList();
        Assert.Contains(log, e => e is SimEvent.CursorMoved);
        Assert.DoesNotContain(log, e => e is SimEvent.StackMoved);
        Assert.IsType<SimEvent.Rewound>(log[^1]);
    }

    [Fact]
    public void Undo_Twice_RewindsTheLogTwice_ThenRedoContinuesFromThere()
    {
        var (start, _) = Run(NewSession(), Right);
        var startLog = start.EventLog.Events.ToList();
        var (done, _) = Run(start, Grab, Right, Drop);
        var doneLog = done.EventLog.Events.ToList();

        var (undone, _) = Run(done, Undo, Undo);
        var log = undone.EventLog.Events.ToList();
        Assert.Equal(startLog, log.Take(startLog.Count));
        // The second undo truncates to the grab's mark, before the first Rewound: one Rewound remains.
        Assert.IsType<SimEvent.Rewound>(Assert.Single(log.Skip(startLog.Count)));

        var (redone, redoEvents) = Run(undone, Grab);
        Assert.Equal(log.Concat(redoEvents), redone.EventLog.Events);
        Assert.Equal(doneLog, done.EventLog.Events); // the abandoned timeline's session still sees its own log
    }

    [Fact]
    public void Undo_KeepsTheIntentLog_SoTheSessionReplays()
    {
        var start = NewSession();
        var (live, _) = Run(start, Right, Grab, Right, Undo, Right, Drop, new Intent.Tick(), Undo);

        Assert.Contains(live.IntentLog, e => e.Intent is Intent.Undo);

        var replayed = live.IntentLog.Aggregate(start, (s, e) => s.Apply(e.Intent));
        Assert.Equal(live.ActionLog, replayed.ActionLog);
        Assert.Equal(live.TickCount, replayed.TickCount);
        Assert.Equal(live.UndoDepth, replayed.UndoDepth);
        Assert.Equal(live.IntentLog, replayed.IntentLog);
        Assert.Equal(EventText.Lines(live.EventLog.Events), EventText.Lines(replayed.EventLog.Events));
    }

    // ==================== Retention ====================

    [Fact]
    public void Rogue_KeepsEverything_UntilTheUndoStackTrims()
    {
        var policy = new EventLogPolicy(ChunkSize: 2);
        var (session, emitted) = Run(NewSession(policy: policy), Right, Grab, Right, Drop, Right, Grab, Drop);

        Assert.Equal(0, session.RetentionHorizon);
        Assert.Equal(emitted, session.EventLog.Events);
    }

    [Fact]
    public void Rogue_UndoStackTrim_MovesTheHorizon_AndCompactsBeforeIt()
    {
        var policy = new EventLogPolicy(ChunkSize: 2);
        var intents = new[] { Right }.Concat(Enumerable.Repeat(new[] { Grab, Drop }, 4).SelectMany(x => x)).ToArray();
        var (session, emitted) = Run(NewSession(policy: policy, maxUndo: 2), intents);

        Assert.Equal(2, session.UndoDepth);
        Assert.Equal(session.UndoMarks.Last().EventPos, session.RetentionHorizon);
        Assert.True(session.EventLog.Start > 0);
        Assert.True(session.EventLog.Start <= session.RetentionHorizon);
        Assert.Equal(emitted.Skip((int)session.EventLog.Start), session.EventLog.Events);
    }

    [Fact]
    public void Rogue_TakesNoCheckpoints()
    {
        var (session, _) = Run(CraftingSession(TickMode.Rogue, EventLogPolicy.Default),
            Enumerable.Repeat<Intent>(new Intent.Tick(), 20).ToArray());
        Assert.Equal(20, session.TickCount);
        Assert.Empty(session.Checkpoints);
    }

    [Fact]
    public void Realtime_CheckpointsEveryNSteps_RingBounded_HorizonAtTheOldest()
    {
        var policy = new EventLogPolicy(ChunkSize: 2, CheckpointEvery: 2, CheckpointRing: 3);
        var (session, emitted) = Run(CraftingSession(TickMode.Realtime, policy),
            Enumerable.Repeat<Intent>(new Intent.Tick(), 20).ToArray());

        Assert.Equal(20, session.TickCount);
        Assert.Equal(new[] { 16, 18, 20 }, session.Checkpoints.Select(c => c.Mark.Step));
        Assert.Equal(session.Checkpoints[0].Mark.EventPos, session.RetentionHorizon);
        Assert.True(session.EventLog.Start > 0 && session.EventLog.Start <= session.RetentionHorizon);
        Assert.Equal(emitted.Skip((int)session.EventLog.Start), session.EventLog.Events);
    }

    [Fact]
    public void Realtime_Checkpoint_HoldsTheStateAfterItsStep()
    {
        var policy = new EventLogPolicy(CheckpointEvery: 2);
        var (session, _) = Run(CraftingSession(TickMode.Realtime, policy), new Intent.Tick(), new Intent.Tick());

        var checkpoint = Assert.Single(session.Checkpoints);
        Assert.Same(session.Current, checkpoint.State);
        Assert.Equal(new LogMark(2, session.EventLog.End), checkpoint.Mark);
    }

    [Fact]
    public void Realtime_Undo_DropsCheckpointsOfTheAbandonedTimeline()
    {
        var policy = new EventLogPolicy(CheckpointEvery: 2);
        var tick = new Intent.Tick();
        var (session, _) = Run(CraftingSession(TickMode.Realtime, policy), tick, tick, Grab, tick);
        Assert.Equal(4, session.TickCount);
        Assert.Equal(new[] { 2, 4 }, session.Checkpoints.Select(c => c.Mark.Step));

        var (undone, _) = Run(session, Undo); // back before step 4: its checkpoint was on the abandoned timeline

        Assert.Equal(new[] { 2 }, undone.Checkpoints.Select(c => c.Mark.Step));
        Assert.True(undone.RetentionHorizon <= undone.EventLog.End);
    }

    [Fact]
    public void Cap_HoldsTheSessionLogUnderTheLimit_AndLeavesAMarker()
    {
        var perEvent = EventLog.EstimateBytes(new SimEvent.TimeAdvanced(0, 0));
        var policy = new EventLogPolicy(MaxBytes: perEvent * 8, ChunkSize: 2);
        var (session, _) = Run(CraftingSession(TickMode.Realtime, policy),
            Enumerable.Repeat<Intent>(new Intent.Tick(), 30).ToArray());

        Assert.True(session.EventLog.Bytes <= policy.MaxBytes);
        Assert.NotNull(session.EventLog.Truncated);
    }

    // ==================== Both frontends see Rewound ====================

    [Fact]
    public void Controller_Undo_ReportsTheRewound()
    {
        var controller = new GameController(NewSession());
        controller.HandleKey(GameKey.Right);
        controller.HandleKey(GameKey.Primary);

        var result = controller.HandleKey(GameKey.Undo);

        Assert.IsType<SimEvent.Rewound>(Assert.Single(result.Events));
    }

    [Fact]
    public void DebugCommandHandler_Undo_ResponseCarriesTheRewoundLine()
    {
        var controller = new GameController(NewSession());
        DebugCommandHandler.Handle(controller, """{"action":"key","key":"Right"}""", out _);
        DebugCommandHandler.Handle(controller, """{"action":"key","key":"Primary"}""", out _);

        var resp = JsonNode.Parse(DebugCommandHandler.Handle(controller, """{"action":"key","key":"Undo"}""", out _))!;

        var line = Assert.Single(resp["events"]!.AsArray())!.GetValue<string>();
        Assert.Matches(@"^\d+ 0 Rewound toStep=\d+$", line);
    }

    // ==================== Kept as is ====================

    /// <summary>
    /// Pins the known Slice 1.3 latent bug, which 1.8 must keep exactly (no ruling yet): a tool run at a panel
    /// other than B pushes the <i>swapped</i> frame, so undoing it restores a state whose B shows that panel's bag.
    /// </summary>
    [Fact]
    public void KnownBug_ToolAtPanelC_UndoRestoresTheSwappedFrame()
    {
        var state = MakeState(out var facilityId).OpenAsContainer(facilityId).State;
        var session = GameSession.New(state, ImmutableArray<Recipe>.Empty, TickMode.Rogue);
        var rootId = session.Current.Locations.Get(LocationId.B).BagId;

        var split = session.Apply(new Intent.QuickSplit(LocationId.C));
        Assert.True(split.CanUndo);
        var undone = split.Apply(Undo);

        Assert.NotEqual(rootId, undone.Current.Locations.Get(LocationId.B).BagId);
        Assert.Equal(facilityId, undone.Current.Locations.Get(LocationId.B).BagId);
    }
}
