using System.Collections.Immutable;
using Pockets.Core.Data;
using Pockets.Core.Models;
using Pockets.Core.Sim;

namespace Pockets.Core.Tests.Sim;

/// <summary>
/// Slice 1.4 acceptance 4: a <see cref="FacilityCraftRule"/> pass on the Workbench allocates no more than the
/// pre-1.4 facility tick. "Today" is kept below as <see cref="Legacy"/>, a verbatim copy of the old
/// <c>Simulation.TickFacilities</c> + <c>FacilityLogic.Tick</c>/<c>CompleteCraft</c> (per-change
/// <c>SetCell</c>), so both are measured in the same process with a Stopwatch-style harness as in spike B
/// (warm up, then <see cref="GC.GetAllocatedBytesForCurrentThread"/> over many iterations).
/// The rule must also produce exactly the legacy result.
/// </summary>
public class FacilityCraftRuleAllocationTests(Xunit.Abstractions.ITestOutputHelper output)
{
    private const int Iterations = 500;

    private static readonly ItemType Rock = new("Plain Rock", Category.Material, IsStackable: true, MaxStackSize: 20);
    private static readonly ItemType Wood = new("Rough Wood", Category.Material, IsStackable: true, MaxStackSize: 20);
    private static readonly ItemType StoneAxe = new("Stone Axe", Category.Tool, IsStackable: false);
    private static readonly ItemType WorkbenchType = new("Workbench", Category.Structure, IsStackable: false);

    private static readonly Recipe AxeRecipe = new(
        "workbench_axe", "Stone Axe",
        new[] { new RecipeInput(Rock, 5), new RecipeInput(Wood, 3) },
        _ => RecipeOutput.FromStacks(new[] { new ItemStack(StoneAxe, 1) }),
        Duration: 3);

    private static readonly SimContent Content = new(
        ImmutableArray.Create(AxeRecipe),
        ImmutableDictionary<string, ImmutableArray<string>>.Empty.Add("Workbench", ImmutableArray.Create("workbench_axe")),
        DialogueBook.Empty, TickMode.Rogue);

    /// <summary>A root bag owning a loaded Workbench at cell 1, crafting the axe with the given owner progress.</summary>
    private static GameState Workbench(int progress)
    {
        var workbench = FacilityBuilder.CreateWorkbench(AxeRecipe);
        workbench = workbench with
        {
            Grid = workbench.Grid
                .SetCell(0, workbench.Grid.GetCell(0) with { Stack = new ItemStack(Rock, 7) })
                .SetCell(1, workbench.Grid.GetCell(1) with { Stack = new ItemStack(Wood, 3) }),
            FacilityState = workbench.FacilityState! with { RecipeId = progress > 0 ? AxeRecipe.Id : null }
        };
        var owner = new ItemStack(WorkbenchType, 1, ContainedBagId: workbench.Id)
            .WithProperty("Progress", new IntValue(progress));
        var root = new Bag(Grid.Create(8, 4)
            .SetCell(0, new Cell(new ItemStack(Rock, 5)))
            .SetCell(1, new Cell(owner)));
        var hand = GameState.CreateHandBag();
        var store = BagStore.Empty.Add(root).Add(hand).Add(workbench);
        return new GameState(store, LocationMap.Create(hand.Id, root.Id),
            ImmutableArray.Create(Rock, Wood, StoneAxe, WorkbenchType));
    }

    /// <summary>The new path: one FacilityCraftRule pass.</summary>
    private static GameState RunRule(GameState state) =>
        FacilityCraftRule.Instance.Apply(state, new RuleContext(1, state.KnownRecipes, Content,
            SimRng.Create(0).AsRandom(SimStream.Craft), _ => { }));

    /// <summary>The pre-1.4 path.</summary>
    private static GameState RunLegacy(GameState state) =>
        Legacy.TickFacilities(state, state.KnownRecipes, Content, SimRng.Create(0).AsRandom(SimStream.Craft), (_, _) => { });

    /// <summary>Bytes allocated per call of <paramref name="run"/> on <paramref name="state"/>, after warm-up.</summary>
    private static double BytesPerCall(Func<GameState, GameState> run, GameState state)
    {
        for (var i = 0; i < 50; i++)
            run(state);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < Iterations; i++)
            run(state);
        return (GC.GetAllocatedBytesForCurrentThread() - before) / (double)Iterations;
    }

    [Theory]
    [InlineData(0)] // start crafting
    [InlineData(1)] // countdown
    [InlineData(2)] // completion: consume two inputs, place the output
    public void RulePass_MatchesLegacy_AndAllocatesNoMore(int progress)
    {
        var state = Workbench(progress);

        var expected = RunLegacy(state);
        var actual = RunRule(state);
        Assert.Equal(Fingerprint(expected), Fingerprint(actual));

        var legacy = BytesPerCall(RunLegacy, state);
        var rule = BytesPerCall(RunRule, state);
        output.WriteLine($"progress {progress}: rule {rule:F0} B/pass, legacy {legacy:F0} B/pass");
        Assert.True(rule <= legacy, $"FacilityCraftRule allocates {rule:F0} B/pass, legacy {legacy:F0} B/pass");
    }

    /// <summary>Every bag's cells and facility state, plus every owner stack's properties, as text.</summary>
    private static string Fingerprint(GameState s) =>
        string.Join("\n", s.Store.All
            .OrderBy(b => b.EnvironmentType, StringComparer.Ordinal).ThenBy(b => b.Grid.Cells.Length)
            .Select(b => $"{b.EnvironmentType} {b.FacilityState} | " + string.Join(";", b.Grid.Cells.Select(c =>
                c.Stack is null ? "_" : $"{c.Stack.ItemType.Name}x{c.Stack.Count}{{{string.Join(",",
                    (c.Stack.Properties ?? ImmutableDictionary<string, PropertyValue>.Empty)
                        .OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => $"{p.Key}={p.Value}"))}}}"))));

    /// <summary>Verbatim copy of the pre-1.4 facility tick (Simulation.TickFacilities + FacilityLogic.Tick/CompleteCraft).</summary>
    private static class Legacy
    {
        public static GameState TickFacilities(
            GameState state, ImmutableHashSet<string> knownRecipes, SimContent content, Random random,
            Action<Guid, string> completed)
        {
            if (content.Recipes.IsDefaultOrEmpty)
                return state;

            var facilities = Simulation.OrderedFacilities(state).ToList();
            if (facilities.Count == 0)
                return state;

            foreach (var facility in facilities)
            {
                var facilityRecipes = content.RecipesForFacility(facility.EnvironmentType, knownRecipes);
                if (facilityRecipes.Count == 0)
                    continue;

                int currentProgress = state.Store.GetOwnerStack(facility.Id)?.GetInt("Progress") ?? 0;

                var wasCrafting = facility.FacilityState?.RecipeId;
                var (ticked, newProgress, newBags) = Tick(facility, currentProgress, facilityRecipes, random);
                if (ticked == facility && newProgress == currentProgress)
                    continue;

                if (wasCrafting is not null && ticked.FacilityState?.RecipeId is null)
                    completed(facility.Id, wasCrafting);

                if (newBags.Count > 0)
                    state = state with { Store = state.Store.AddRange(newBags) };

                var progressValue = newProgress;
                state = state.ReplaceBagById(facility.Id, ticked,
                    stack => stack.WithProperty("Progress", new IntValue(progressValue)));
            }

            return state;
        }

        private static (Bag Facility, int Progress, IReadOnlyList<Bag> NewBags) Tick(
            Bag facility, int currentProgress, IReadOnlyList<Recipe> recipes, Random rng)
        {
            if (facility.FacilityState is null || !facility.FacilityState.IsActive)
                return (facility, currentProgress, Array.Empty<Bag>());

            var state = facility.FacilityState;
            var inputStacks = FacilityLogic.GetInputStacks(facility);

            if (state.RecipeId is not null)
            {
                var activeRecipe = recipes.FirstOrDefault(r => r.Id == state.RecipeId);
                if (activeRecipe is null || !FacilityLogic.RecipeMatches(activeRecipe, inputStacks))
                    return (facility with { FacilityState = state with { RecipeId = null } }, 0, Array.Empty<Bag>());

                var newProgress = currentProgress + 1;
                if (newProgress >= activeRecipe.Duration)
                {
                    var (completed, newBags) = CompleteCraft(facility, activeRecipe, rng);
                    return (completed, 0, newBags);
                }

                return (facility, newProgress, Array.Empty<Bag>());
            }

            Recipe? match;
            if (state.ActiveRecipeId is not null)
            {
                var activeRecipe = recipes.FirstOrDefault(r => r.Id == state.ActiveRecipeId);
                match = activeRecipe is not null && FacilityLogic.RecipeMatches(activeRecipe, inputStacks)
                    ? activeRecipe : null;
            }
            else if (state.RequiresSelectedRecipe)
                match = null;
            else
                match = FacilityLogic.FindMatchingRecipe(facility, recipes);

            if (match is null)
                return (facility, currentProgress, Array.Empty<Bag>());

            return (facility with { FacilityState = state with { RecipeId = match.Id } }, 1, Array.Empty<Bag>());
        }

        private static (Bag Facility, IReadOnlyList<Bag> NewBags) CompleteCraft(Bag facility, Recipe recipe, Random rng)
        {
            var grid = facility.Grid;
            var newBags = new List<Bag>();

            var remaining = recipe.Inputs.ToDictionary(i => i.ItemType, i => i.Count);
            for (int i = 0; i < grid.Cells.Length; i++)
            {
                var cell = grid.GetCell(i);
                if (cell.Frame is not InputSlotFrame || cell.IsEmpty)
                    continue;

                var stack = cell.Stack!;
                if (!remaining.TryGetValue(stack.ItemType, out var needed) || needed <= 0)
                    continue;

                var consume = Math.Min(stack.Count, needed);
                remaining[stack.ItemType] = needed - consume;

                grid = consume >= stack.Count
                    ? grid.SetCell(i, cell with { Stack = null })
                    : grid.SetCell(i, cell with { Stack = stack with { Count = stack.Count - consume } });
            }

            var recipeOutput = recipe.OutputFactory(rng);
            var outputs = recipeOutput.Stacks;
            newBags.AddRange(recipeOutput.NewBags);

            var outputIndex = 0;
            for (int i = 0; i < grid.Cells.Length && outputIndex < outputs.Count; i++)
            {
                var cell = grid.GetCell(i);
                if (cell.Frame is not OutputSlotFrame || !cell.IsEmpty)
                    continue;

                grid = grid.SetCell(i, cell with { Stack = outputs[outputIndex] });
                outputIndex++;
            }

            return (facility with
            {
                Grid = grid,
                FacilityState = facility.FacilityState! with { RecipeId = null }
            }, newBags);
        }
    }
}
