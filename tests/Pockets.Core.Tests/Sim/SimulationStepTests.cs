using System.Collections.Immutable;
using Pockets.Core.Data;
using Pockets.Core.Models;
using Pockets.Core.Sim;

namespace Pockets.Core.Tests.Sim;

/// <summary>
/// Slice 1.2: <see cref="Simulation.Step"/> semantics, namely the no-op / rejected / applied split, the
/// explicit time flag (P1-D2), coarse events in (Step, Seq) emission order, and craft completion.
/// </summary>
public class SimulationStepTests
{
    private static readonly ItemType Rock = new("Plain Rock", Category.Material, IsStackable: true, MaxStackSize: 20);
    private static readonly ItemType Wood = new("Rough Wood", Category.Material, IsStackable: true, MaxStackSize: 20);
    private static readonly ItemType StoneAxe = new("Stone Axe", Category.Tool, IsStackable: false);
    private static readonly ItemType WorkbenchType = new("Workbench", Category.Structure, IsStackable: false);

    private static readonly Recipe AxeRecipe = new(
        "workbench_axe", "Stone Axe",
        new[] { new RecipeInput(Rock, 5), new RecipeInput(Wood, 3) },
        () => RecipeOutput.FromStacks(new[] { new ItemStack(StoneAxe, 1) }),
        Duration: 3);

    /// <summary>A root bag holding a Rock stack at cell 0 and a Workbench (inputs optionally pre-loaded) at cell 1.</summary>
    private static GameState CreateState(bool loadWorkbench = false)
    {
        var workbench = FacilityBuilder.CreateWorkbench(AxeRecipe);
        if (loadWorkbench)
            workbench = workbench with
            {
                Grid = workbench.Grid
                    .SetCell(0, workbench.Grid.GetCell(0) with { Stack = new ItemStack(Rock, 5) })
                    .SetCell(1, workbench.Grid.GetCell(1) with { Stack = new ItemStack(Wood, 3) })
            };

        var rootGrid = Grid.Create(8, 4)
            .SetCell(0, new Cell(new ItemStack(Rock, 5)))
            .SetCell(1, new Cell(new ItemStack(WorkbenchType, 1, ContainedBagId: workbench.Id)));
        var root = new Bag(rootGrid);
        var hand = GameState.CreateHandBag();
        var store = BagStore.Empty.Add(root).Add(hand).Add(workbench);
        return new GameState(store, LocationMap.Create(hand.Id, root.Id),
            ImmutableArray.Create(Rock, Wood, StoneAxe, WorkbenchType));
    }

    /// <summary>Wraps a game state as a SimState at the given tick count with the axe recipe loaded.</summary>
    private static SimState Sim(GameState game, int tickCount = 0) =>
        new(game, tickCount, TimeSpan.Zero, null, null,
            new SimContent(ImmutableArray.Create(AxeRecipe), null, DialogueBook.Empty));

    [Fact]
    public void Tool_Failed_EmitsOnlyRejected_StateUnchanged()
    {
        var s = Sim(CreateState(), tickCount: 4);
        var intent = new Intent.Tool(g => ToolResult.Fail(g, "nope"), AdvancesTime: true);

        var r = Simulation.Step(s, new[] { intent });

        Assert.Same(s, r.State);
        Assert.Equal(new SimEvent[] { new SimEvent.IntentRejected(5, 0, intent, "nope") }, r.Events.ToArray());
    }

    [Fact]
    public void Tool_NoOpSuccess_NoEvents_NoTick_EvenWhenTimeAdvances()
    {
        var s = Sim(CreateState(loadWorkbench: true));
        var r = Simulation.Step(s, new[] { new Intent.Tool(g => ToolResult.Ok(g), AdvancesTime: true) });

        Assert.Same(s, r.State);
        Assert.Empty(r.Events);
    }

    [Fact]
    public void Tool_Realtime_AppliesWithoutTimeAdvance()
    {
        var s = Sim(CreateState(loadWorkbench: true), tickCount: 2);
        var intent = new Intent.Tool(g => g.ToolGrab(), AdvancesTime: false);

        var r = Simulation.Step(s, new[] { intent });

        Assert.Equal(3, r.State.TickCount);
        Assert.Equal(new SimEvent[] { new SimEvent.IntentApplied(3, 0, intent) }, r.Events.ToArray());
        // The loaded workbench did not start crafting: no time passed.
        Assert.All(r.State.Game.Store.Facilities, f => Assert.Null(f.FacilityState!.RecipeId));
    }

    [Fact]
    public void Tool_Rogue_AppliesThenAdvancesTime_InSeqOrder()
    {
        var s = Sim(CreateState(loadWorkbench: true));
        var intent = new Intent.Tool(g => g.ToolGrab(), AdvancesTime: true);

        var r = Simulation.Step(s, new[] { intent });

        Assert.IsType<SimEvent.IntentApplied>(r.Events[0]);
        Assert.IsType<SimEvent.TimeAdvanced>(r.Events[1]);
        Assert.Equal(Enumerable.Range(0, r.Events.Length), r.Events.Select(e => e.Seq));
        Assert.All(r.Events, e => Assert.Equal(1, e.Step));
        Assert.Contains(r.State.Game.Store.Facilities, f => f.FacilityState!.RecipeId == AxeRecipe.Id);
    }

    [Fact]
    public void Tool_GrabIntoEmptyLedger_FiresFirstPickupOnce()
    {
        var game = CreateState() with { Ui = UiLedger.Empty };
        var r = Simulation.Step(Sim(game), new[] { new Intent.Tool(g => g.ToolGrab(), AdvancesTime: false) });

        Assert.Single(r.Events.OfType<SimEvent.UiTriggerFired>(), e => e.Trigger == UiTrigger.FirstPickup);
        Assert.True(r.State.Game.Ui.Has(ChromeElement.Toolbar));
    }

    [Fact]
    public void Tick_NothingToDo_IsNoOp()
    {
        var s = Sim(CreateState());
        var r = Simulation.Step(s, new[] { new Intent.Tick() });

        Assert.Same(s, r.State);
        Assert.Empty(r.Events);
    }

    [Fact]
    public void Tick_LoadedWorkbench_EventuallyEmitsCraftCompleted()
    {
        var s = Sim(CreateState(loadWorkbench: true));
        var facilityId = s.Game.Store.Facilities.Single().Id;

        var completions = new List<SimEvent.CraftCompleted>();
        for (var i = 0; i < 10 && completions.Count == 0; i++)
        {
            var r = Simulation.Step(s, new[] { new Intent.Tick() });
            Assert.IsType<SimEvent.TimeAdvanced>(r.Events[0]);
            completions.AddRange(r.Events.OfType<SimEvent.CraftCompleted>());
            s = r.State;
        }

        var done = Assert.Single(completions);
        Assert.Equal(facilityId, done.FacilityBagId);
        Assert.Equal(AxeRecipe.Id, done.RecipeId);
        Assert.Equal(s.TickCount, done.Step);
    }

    [Fact]
    public void Session_RoguePath_LogsCompletionAfterActionLine_LikeBefore()
    {
        var session = GameSession.New(CreateState(loadWorkbench: true), ImmutableArray.Create(AxeRecipe), TickMode.Rogue);
        for (var i = 0; i < 10; i++)
            session = session.ExecuteSort();

        var idx = session.ActionLog.FindIndex(l => l.StartsWith("✦ "));
        Assert.True(idx > 0);
        Assert.Equal("✦ Workbench crafted: Stone Axe", session.ActionLog[idx]);
        Assert.Equal("Sort: reorganized bag", session.ActionLog[idx - 1]);
    }
}
