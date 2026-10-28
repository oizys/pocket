using System.Collections.Immutable;
using System.Text.RegularExpressions;
using Pockets.Core.Data;
using Pockets.Core.Models;
using Pockets.Core.Sim;

namespace Pockets.Core.Tests.Sim;

/// <summary>
/// Slice 1.1 acceptance: every in-step random draw comes from the <see cref="SimRng"/> in state.
/// (1) No source file builds an unseeded <see cref="Random"/>. (2) Two sessions with the same world seed,
/// fed the same AcquireRandom ×50 and seedling craft, end in the same world. (3) Step's AcquireRandom draw
/// is the pinned hash, and the advanced RNG is kept.
/// </summary>
public class SimRngDeterminismTests
{
    private static readonly ItemType Seed = new("Forest Seed", Category.Material, IsStackable: true);
    private static readonly ItemType Soil = new("Rich Soil", Category.Material, IsStackable: true);
    private static readonly ItemType Rock = new("Plain Rock", Category.Material, IsStackable: true);
    private static readonly ItemType Wood = new("Rough Wood", Category.Material, IsStackable: true);
    private static readonly ItemType Grass = new("Dry Grass", Category.Material, IsStackable: true);
    private static readonly ItemType PotType = new("Seedling Pot", Category.Structure, IsStackable: false);
    private static readonly ItemType ForestBag = new("Forest Bag", Category.Bag, IsStackable: false);

    private static readonly ImmutableArray<ItemType> Types =
        ImmutableArray.Create(Seed, Soil, Rock, Wood, Grass, PotType, ForestBag);

    // ── (1) architecture: no unseeded Random in src/ ─────────────────────────

    /// <summary>
    /// A text scan of <c>src/**/*.cs</c>: no <c>new Random()</c>, no target-typed <c>Random x = new()</c>,
    /// and no <c>Random.Shared</c>. Seeded <c>new Random(seed)</c> stays legal for init-time generation (P1-D7).
    /// </summary>
    [Fact]
    public void Src_HasNoUnseededRandom()
    {
        var banned = new Regex(@"new\s+Random\s*\(\s*\)|Random\??\s+\w+\s*=\s*new\s*\(\s*\)|Random\.Shared");
        var src = Path.Combine(TestPaths.RepoRoot, "src");
        var hits = Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Split(Path.DirectorySeparatorChar).Any(part => part is "obj" or "bin"))
            .SelectMany(f => File.ReadLines(f).Select((line, i) => (File: f, Line: i + 1, Text: line)))
            .Where(l => banned.IsMatch(l.Text))
            .Select(l => $"{Path.GetRelativePath(src, l.File)}:{l.Line}: {l.Text.Trim()}")
            .ToList();

        Assert.True(hits.Count == 0, "Unseeded Random in src/:\n" + string.Join("\n", hits));
    }

    // ── (2) same seed, same intents ⇒ same world ─────────────────────────────

    [Fact]
    public void SameSeed_AcquireRandomX50_AndSeedlingCraft_ProduceIdenticalWorlds()
    {
        var a = Play(worldSeed: 42);
        var b = Play(worldSeed: 42);

        Assert.Equal(Fingerprint(a.Current), Fingerprint(b.Current));
        Assert.Equal(a.ActionLog, b.ActionLog);
        Assert.Equal(a.WorldRng, b.WorldRng);

        // The seedling actually crafted (so the Craft stream was exercised), and its forest has loot.
        Assert.Contains(a.ActionLog, l => l.Contains("crafted: Forest Bag"));
        Assert.Matches(@"Forest\[[^\]]*x1", Fingerprint(a.Current));
    }

    [Fact]
    public void DifferentSeed_ProducesADifferentWorld()
    {
        Assert.NotEqual(Fingerprint(Play(worldSeed: 42).Current), Fingerprint(Play(worldSeed: 43).Current));
    }

    [Fact]
    public void Dsl_AcquireRandom_IsDeterministicPerSeed()
    {
        var a = NewSession(7).Execute("acquire-random");
        var b = NewSession(7).Execute("acquire-random");

        Assert.Equal(Fingerprint(a.Current), Fingerprint(b.Current));
        Assert.NotEqual(Fingerprint(NewSession(7).Current), Fingerprint(a.Current));
    }

    [Fact]
    public void Dsl_AcquireRandom_WithoutAnRng_FailsInsteadOfDrawing()
    {
        var result = Pockets.Core.Dsl.DslInterpreter.RunProgram(NewSession(7).Current, "acquire-random");
        Assert.False(result.IsOk);
    }

    // ── (3) Step draws from the in-state RNG ─────────────────────────────────

    [Fact]
    public void Step_AcquireRandom_DrawsThePinnedHash_AndKeepsTheAdvancedRng()
    {
        var s = NewSession(99).ToSimState() with { TickCount = 4 };
        s = s with { Content = s.Content with { TickMode = TickMode.Realtime } }; // no time advance
        var expected = Types[(int)(SimRng.Hash(99, 5, SimStream.Acquire, 0) % (ulong)Types.Length)];

        var r = Simulation.Step(s, new[] { new Intent.AcquireRandom(LocationId.B) });

        Assert.IsType<SimEvent.IntentApplied>(r.Events[0]);
        Assert.Contains(r.State.Game.RootBag.Grid.Cells, c => c.Stack?.ItemType == expected && c.Stack.Count == 1);
        Assert.Equal(5, r.State.Rng.Step);
        Assert.Equal(1, r.State.Rng.DrawCount(SimStream.Acquire));
        Assert.Equal(0, r.State.Rng.DrawCount(SimStream.Craft));
    }

    [Fact]
    public void Session_AcquireRandom_LogNamesTheTypeStepDrew()
    {
        var session = NewSession(99);
        var expected = Types[(int)(SimRng.Hash(99, 1, SimStream.Acquire, 0) % (ulong)Types.Length)];

        var next = session.ExecuteAcquireRandom();

        Assert.Equal($"Acquire: +1 {expected.Name}", next.ActionLog[^1]);
        Assert.Equal(1, next.TickCount);
    }

    [Fact]
    public void Undo_KeepsTheRngMovingForward()
    {
        var once = NewSession(5).ExecuteAcquireRandom();
        var redone = once.Undo()!.ExecuteAcquireRandom();

        // TickCount is carried across undo, so the redo draws at step 2, not step 1 again.
        Assert.Equal(2, redone.WorldRng.Step);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// A rogue-mode session: a Seedling Pot loaded with its 5 seeds + 3 soil (Duration 8) in an 8×4 root.
    /// Built fresh each call, so two sessions share no instances.
    /// </summary>
    private static GameSession NewSession(ulong worldSeed)
    {
        var recipes = RecipeRegistry.BuildRecipes(Types);
        var seedling = recipes.Single(r => r.Id == "seedling_forest");
        var pot = FacilityBuilder.CreateSeedlingPot(seedling);
        pot = pot with
        {
            Grid = pot.Grid
                .SetCell(0, pot.Grid.GetCell(0) with { Stack = new ItemStack(Seed, 5) })
                .SetCell(1, pot.Grid.GetCell(1) with { Stack = new ItemStack(Soil, 3) })
        };

        var root = new Bag(Grid.Create(8, 4)
            .SetCell(0, new Cell(new ItemStack(PotType, 1, ContainedBagId: pot.Id))));
        var hand = GameState.CreateHandBag();
        var state = new GameState(BagStore.Empty.Add(root).Add(hand).Add(pot),
            LocationMap.Create(hand.Id, root.Id), Types);

        return GameSession.New(state, recipes, TickMode.Rogue).WithWorldSeed(worldSeed);
    }

    /// <summary>AcquireRandom ×50 in rogue mode: each action ticks the pot, which completes at tick 8.</summary>
    private static GameSession Play(ulong worldSeed) =>
        Enumerable.Range(0, 50).Aggregate(NewSession(worldSeed), (s, _) => s.ExecuteAcquireRandom());

    /// <summary>
    /// The world's content with bag ids erased (ids are process-fresh GUIDs): the root grid, recursing
    /// into every contained bag, plus the hand.
    /// </summary>
    private static string Fingerprint(GameState g) =>
        Describe(g, g.RootBag) + " hand:" + Describe(g, g.HandBag);

    /// <summary>One bag as <c>Env[cell,cell,…]</c>, each cell <c>Name xCount</c> plus its nested bag.</summary>
    private static string Describe(GameState g, Bag bag) =>
        $"{bag.EnvironmentType}[" + string.Join(",", bag.Grid.Cells.Select(c =>
            c.Stack is not { } stack
                ? "."
                : $"{stack.ItemType.Name}x{stack.Count}"
                  + (stack.ContainedBagId is { } id && g.Store.GetById(id) is { } inner ? Describe(g, inner) : ""))) + "]";
}
