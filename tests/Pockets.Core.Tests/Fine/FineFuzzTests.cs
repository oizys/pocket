using System.Collections.Immutable;
using System.Text.Json.Nodes;
using Pockets.Core.Fine;
using Pockets.Core.Models;
using Pockets.Core.Rendering;
using Pockets.Core.Sim;

namespace Pockets.Core.Tests.Fine;

/// <summary>
/// Slice 2.8, "fuzz L2a ⇒ identical L1" (D5): the seeded noise generator behind the runner's <c>--fuzz-fine</c>, the
/// journey fuzz itself (the same check the parity gate runs), the mutation that proves the check can fail, and the
/// property test over random L1 intent streams that makes the wall behavioural as well as textual.
/// </summary>
public class FineFuzzTests
{
    private static readonly LocationId[] Locs = Enum.GetValues<LocationId>();

    // ==================== The generator ====================

    [Fact]
    public void Generate_IsDeterministicPerSeedAndSalt_AndVariesWithEither()
    {
        var game = JourneyCommands.NewDemoSession().Current;
        Assert.Equal(FineFuzz.Generate(game, 7, 3), FineFuzz.Generate(game, 7, 3));
        Assert.NotEqual(FineFuzz.Generate(game, 7, 3), FineFuzz.Generate(game, 7, 4));
        Assert.NotEqual(FineFuzz.Generate(game, 7, 3), FineFuzz.Generate(game, 8, 3));
    }

    [Fact]
    public void Generate_PutsOffsetsOnOccupiedCellsOfActiveBags_AndBodiesAtRestingPositions_WithinBounds()
    {
        var game = JourneyCommands.NewDemoSession().Current;
        var active = Locs.Where(l => game.Locations.TryGet(l) is not null).Select(l => game.ActiveBagAt(l)).ToList();
        var fuzzes = Enumerable.Range(0, 20).Select(salt => FineFuzz.Generate(game, 1, salt)).ToList();

        Assert.All(fuzzes.SelectMany(f => f.Offsets), kv =>
        {
            var bag = active.First(b => b.Id == kv.Key.Bag);
            Assert.False(bag.Grid.Cells[kv.Key.Index].IsEmpty);
            Assert.InRange(kv.Value.Dx, -FineState.MaxOffset, FineState.MaxOffset);
            Assert.InRange(kv.Value.Dy, -FineState.MaxOffset, FineState.MaxOffset);
        });
        Assert.All(fuzzes.SelectMany(f => f.Bodies), kv =>
        {
            Assert.Equal(game.ActiveBagAt(kv.Key.Loc).Id, kv.Key.Bag);
            Assert.InRange(kv.Value.Px, -7, 7);
            Assert.InRange(kv.Value.Py, -7, 7);
            Assert.Equal((0, 0, (Direction8?)null), (kv.Value.Vx, kv.Value.Vy, kv.Value.Held));
        });
        // Not vacuous: the demo start has stacks and locations, so the noise lands somewhere.
        Assert.Contains(fuzzes, f => f.Offsets.Count > 0);
        Assert.Contains(fuzzes, f => f.Bodies.Count > 0);
        Assert.Contains(fuzzes.SelectMany(f => f.Offsets), kv => Math.Abs(kv.Value.Dx) == FineState.MaxOffset);
    }

    [Fact]
    public void WithFuzzedFine_OffsetsOnly_KeepsTheBodies()
    {
        var session = JourneyCommands.NewDemoSession();
        var bodies = session.WithFuzzedFine(5, 0).Fine.Bodies;
        Assert.NotEmpty(bodies);
        var withBodies = session.WithFuzzedFine(5, 0);
        var offsetsOnly = withBodies.WithFuzzedFine(9, 1, bodies: false);
        Assert.Equal(withBodies.Fine.Bodies, offsetsOnly.Fine.Bodies);
        Assert.Equal(FineFuzz.Generate(session.Current, 9, 1).Offsets, offsetsOnly.Fine.Offsets);
    }

    [Fact]
    public void ControllerFuzz_ChangesOnlyTheFineState_NotTheLogsOrTheViewModel()
    {
        var controller = new GameController(JourneyCommands.NewDemoSession());
        var before = controller.Session;
        var vm = ViewModelSerializer.Serialize(before).ToJsonString();
        controller.FuzzFine(3, 0);
        Assert.False(controller.Session.Fine.IsEmpty);
        Assert.Same(before.Current, controller.Session.Current);
        Assert.Equal(before.IntentLog, controller.Session.IntentLog);
        Assert.Equal(before.ActionLog, controller.Session.ActionLog);
        Assert.Equal(vm, ViewModelSerializer.Serialize(controller.Session).ToJsonString());
    }

    // ==================== The journey fuzz (what --fuzz-fine checks) ====================

    [Theory]
    [InlineData("smoke.journey.json", 1)]
    [InlineData("smoke.journey.json", 2)]
    [InlineData("target-demo.journey.json", 1)]
    public void Fuzz_AfterEveryStep_LeavesEveryViewModelAndEventLineIdentical(string journeyFile, int seed)
    {
        var plain = JourneyCommands.Run(new GameController(JourneyCommands.NewDemoSession()), journeyFile);
        var fuzzedSteps = 0;
        var fuzzed = JourneyCommands.Run(new GameController(JourneyCommands.NewDemoSession()), journeyFile, (c, next) =>
        {
            c.FuzzFine(seed, next);
            if (!c.Session.Fine.IsEmpty) fuzzedSteps++;
        });

        Assert.True(fuzzedSteps > plain.Count / 2, $"the fuzz was mostly empty ({fuzzedSteps} of {plain.Count})");
        Assert.Null(JourneyCommands.FirstDifference(plain, fuzzed));
    }

    /// <summary>
    /// The required mutation: a test-only hook that lets L1 read an offset (any offset in the fine state snaps the B
    /// cursor home before the step) must fail the fuzz check. Unfuzzed, the fine state stays empty and the hook is inert,
    /// so the plain run is unchanged; fuzzed, the hook fires and the VMs diverge.
    /// </summary>
    [Fact]
    public void Mutation_L1ReadsAnOffset_FailsTheFuzz()
    {
        static SimState Leak(SimState sim, FineState fine)
        {
            if (fine.Offsets.IsEmpty) return sim;
            var b = sim.Game.Locations.Get(LocationId.B);
            return sim with { Game = sim.Game with { Locations = sim.Game.Locations.Set(LocationId.B, b with { Cursor = new Cursor(new Position(0, 0)) }) } };
        }
        GameController Leaky() => new(JourneyCommands.NewDemoSession() with { FineLeakForTests = Leak });

        var baseline = JourneyCommands.Run(new GameController(JourneyCommands.NewDemoSession()), "smoke.journey.json");
        var plain = JourneyCommands.Run(Leaky(), "smoke.journey.json");
        var fuzzed = JourneyCommands.Run(Leaky(), "smoke.journey.json", (c, next) => c.FuzzFine(1, next));

        Assert.Null(JourneyCommands.FirstDifference(baseline, plain));
        Assert.NotNull(JourneyCommands.FirstDifference(plain, fuzzed));
    }

    // ==================== The property test ====================

    /// <summary>The L1 keys the random streams draw from (no steering; the TUI-style D2-A input).</summary>
    private static readonly GameKey[] Keys =
    [
        GameKey.Up, GameKey.Down, GameKey.Left, GameKey.Right, GameKey.Up, GameKey.Down, GameKey.Left, GameKey.Right,
        GameKey.Primary, GameKey.Secondary, GameKey.QuickSplit, GameKey.Sort, GameKey.AcquireRandom, GameKey.Peek,
        GameKey.LeaveBag, GameKey.Undo, GameKey.FocusNext, GameKey.BeginSplit, GameKey.Confirm, GameKey.Cancel
    ];

    /// <summary>One random command: mostly keys, sometimes a clock advance or the free-movement flag (never a steer).</summary>
    private static string RandomCommand(Random rng) => rng.Next(20) switch
    {
        0 => new JsonObject { ["action"] = "advanceTime", ["ms"] = (long)(125 * rng.Next(1, 12)) }.ToJsonString(),
        1 => new JsonObject { ["action"] = "setMovement", ["loc"] = "B", ["model"] = rng.Next(2) == 0 ? "Free" : "GridLocked" }.ToJsonString(),
        _ => new JsonObject { ["action"] = "key", ["key"] = Keys[rng.Next(Keys.Length)].ToString() }.ToJsonString()
    };

    /// <summary>The handler's response, or <c>{"error": "&lt;exception type&gt;"}</c> when the command throws.</summary>
    private static JsonNode Handle(GameController controller, string command)
    {
        try { return JsonNode.Parse(DebugCommandHandler.Handle(controller, command))!; }
        catch (Exception ex) { return new JsonObject { ["error"] = ex.GetType().Name }; }
    }

    /// <summary>
    /// Random fine states × random L1 intent streams (no <c>Steer</c>) give identical L1: per command, the same VM
    /// and the same event lines whether or not the fine state is overwritten with noise before it.
    /// </summary>
    [Theory]
    [InlineData(11)] [InlineData(12)] [InlineData(13)] [InlineData(14)] [InlineData(15)]
    [InlineData(16)] [InlineData(17)] [InlineData(18)] [InlineData(19)] [InlineData(20)]
    public void RandomFineStates_TimesRandomL1Streams_GiveIdenticalL1(int seed)
    {
        var rng = new Random(seed);
        var commands = Enumerable.Range(0, 120).Select(_ => RandomCommand(rng)).ToList();
        var plain = new GameController(JourneyCommands.NewDemoSession());
        var fuzzed = new GameController(JourneyCommands.NewDemoSession());
        var text = (Plain: new List<string>(), Fuzzed: new List<string>());

        for (var i = 0; i < commands.Count; i++)
        {
            fuzzed.FuzzFine(seed, i);
            var a = Handle(plain, commands[i]);
            var b = Handle(fuzzed, commands[i]);
            // A random stream can reach a pre-existing L1 throw (a tool routed to a C panel that has since closed: a
            // KeyNotFoundException in Simulation.SwapIn). The session is left as it was; it must happen identically in both.
            Assert.True(a["error"]?.ToJsonString() == b["error"]?.ToJsonString(), $"seed {seed} command {i} {commands[i]}: {a["error"]} vs {b["error"]}");
            if (a["error"] is not null) continue;
            Assert.True(a["events"]!.ToJsonString() == b["events"]!.ToJsonString(),
                $"seed {seed} command {i} {commands[i]}: events differ\n  {a["events"]}\n  {b["events"]}");
            Assert.True(ViewModelSerializer.Serialize(plain.Session).ToJsonString() == ViewModelSerializer.Serialize(fuzzed.Session).ToJsonString(),
                $"seed {seed} command {i} {commands[i]}: view-model differs");
        }
        Assert.Equal(plain.Session.TickCount, fuzzed.Session.TickCount);
    }
}
