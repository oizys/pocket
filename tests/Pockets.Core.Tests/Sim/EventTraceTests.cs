using System.Collections.Immutable;
using Pockets.Core.Data;
using Pockets.Core.Models;
using Pockets.Core.Sim;

namespace Pockets.Core.Tests.Sim;

/// <summary>
/// Two-layer Phase 1, Slice 1.5 acceptance 3: one event trace per mechanic, in the spike A trace style —
/// an input board plus an intent, and the step's events as canonical lines (<see cref="EventText"/>; bags are
/// named <c>bag#1, bag#2…</c> in order of first appearance, P1-D8). Every trace is also run through the
/// coverage check (<see cref="EventCoverage"/>, P1-D1).
/// <para>
/// Boards: a 4-wide root grid written as space-separated tokens, rows split by <c>/</c>. <c>.</c> is an empty
/// cell, <c>R5</c> five Plain Rock, <c>W3</c> three Rough Wood, <c>T</c> a tree (unnavigable), <c>b</c> a plain
/// pouch bag, <c>e</c> an enter-only bag. The hand is one slot. Sessions are realtime unless the trace says
/// otherwise, so a tool step does not tick.
/// </para>
/// </summary>
public class EventTraceTests
{
    private static readonly ItemType Rock = new("Plain Rock", Category.Material, IsStackable: true, MaxStackSize: 20);
    private static readonly ItemType Wood = new("Rough Wood", Category.Material, IsStackable: true, MaxStackSize: 20);
    private static readonly ItemType Berry = new("Wild Berry", Category.Medicine, IsStackable: true, MaxStackSize: 20);
    private static readonly ItemType Bush = new("Berry Bush", Category.Structure, IsStackable: false);
    private static readonly ItemType Pouch = new("Small Pouch", Category.Bag, IsStackable: false);
    private static readonly ItemType StoneAxe = new("Stone Axe", Category.Tool, IsStackable: false);
    private static readonly ItemType WorkbenchType = new("Workbench", Category.Structure, IsStackable: false);
    private static readonly ItemType Card = new("Axe Recipe", Category.Material, IsStackable: false);

    private static readonly Recipe AxeRecipe = new(
        "workbench_axe", "Stone Axe",
        new[] { new RecipeInput(Rock, 5), new RecipeInput(Wood, 3) },
        _ => RecipeOutput.FromStacks(new[] { new ItemStack(StoneAxe, 1) }),
        Duration: 3);

    private static readonly ImmutableArray<ItemType> Types =
        ImmutableArray.Create(Rock, Wood, Berry, Bush, Pouch, StoneAxe, WorkbenchType, Card);

    /// <summary>
    /// A root bag parsed from <paramref name="board"/> (see the class summary), a one-slot hand holding
    /// <paramref name="hand"/>, the B cursor at <paramref name="cursor"/>, and the full UI ledger.
    /// </summary>
    private static GameState Board(string board, ItemStack? hand = null, Position cursor = default)
    {
        var tokens = board.Split('/').SelectMany(r => r.Split(' ', StringSplitOptions.RemoveEmptyEntries)).ToList();
        var grid = Grid.Create(4, (tokens.Count + 3) / 4);
        var store = BagStore.Empty;
        for (var i = 0; i < tokens.Count; i++)
        {
            Cell cell;
            switch (tokens[i][0])
            {
                case '.': continue;
                case 'T': cell = new Cell(Frame: new TreeFrame()); break;
                case 'b' or 'e':
                    var inner = new Bag(Grid.Create(2, 1), "Pouch") { EnterOnly = tokens[i][0] == 'e' };
                    store = store.Add(inner);
                    cell = new Cell(new ItemStack(Pouch, 1, ContainedBagId: inner.Id));
                    break;
                default:
                    var type = tokens[i][0] == 'R' ? Rock : Wood;
                    cell = new Cell(new ItemStack(type, int.Parse(tokens[i][1..])));
                    break;
            }
            grid = grid.SetCell(i, cell);
        }

        var root = new Bag(grid, "Root");
        var handBag = GameState.CreateHandBag();
        if (hand is not null)
            handBag = handBag with { Grid = handBag.Grid.SetCell(0, new Cell(hand)) };
        store = store.Add(root).Add(handBag);
        return new GameState(store, LocationMap.Create(handBag.Id, root.Id, new Cursor(cursor)), Types);
    }

    /// <summary>A SimState over <paramref name="game"/> with the axe recipe loaded.</summary>
    private static SimState Sim(GameState game, TickMode mode = TickMode.Realtime, DialogueBook? beats = null) =>
        new(game, 0, TimeSpan.Zero, null, null,
            new SimContent(ImmutableArray.Create(AxeRecipe), null, beats ?? DialogueBook.Empty, mode), SimRng.Create(7));

    /// <summary>
    /// Runs <paramref name="intents"/> one step each (as the shell does) and returns every event line, bags
    /// named across the whole run. Asserts the coverage check on every step.
    /// </summary>
    private static string Trace(ref SimState s, params Intent[] intents)
    {
        var text = new EventText();
        var lines = new List<string>();
        foreach (var intent in intents)
        {
            var r = Simulation.Step(s, new[] { intent });
            Assert.Empty(EventCoverage.Uncovered(s, r.State, r.Events));
            lines.AddRange(r.Events.Select(text.Line));
            s = r.State;
        }
        return string.Join("\n", lines);
    }

    /// <summary><see cref="Trace(ref SimState, Intent[])"/> from a fresh state.</summary>
    private static string Trace(SimState s, params Intent[] intents) => Trace(ref s, intents);

    private static string Lines(params string[] lines) => string.Join("\n", lines);

    // ==================== Cursor ====================

    [Fact]
    public void Cursor_Move_Bump_FailedPeek()
    {
        // R5 .  T  e      cursor (0,0)
        var s = Sim(Board("R5 . T e"));
        Assert.Equal(Lines(
                "1 0 IntentApplied intent=Move(B, Right)",
                "1 1 CursorMoved loc=B bag=bag#1 from=(0,0) to=(0,1)",
                // the tree refuses the move: the cue, and the bump count rising, but no cursor change
                "1 0 IntentApplied intent=Move(B, Right)",
                "1 1 Bumped loc=B bag=bag#1 at=(0,1) dir=Right reason=Unnavigable"),
            Trace(ref s, new Intent.Move(LocationId.B, Direction.Right), new Intent.Move(LocationId.B, Direction.Right)));

        // An enter-only bag refuses a look-in: only the cue (no beat loaded, so nothing else changes).
        var peek = Sim(Board("R5 . T e", cursor: new Position(0, 3)));
        Assert.Equal("1 0 Bumped loc=B bag=bag#1 at=(0,3) dir=- reason=FailedPeek",
            Trace(peek, new Intent.Peek(LocationId.B)));
    }

    // ==================== Stacks ====================

    [Fact]
    public void Grab_ThenDrop_MovesTheStack()
    {
        var s = Sim(Board("R5 . . ."));
        Assert.Equal(Lines(
                "1 0 IntentApplied intent=Grab(B)",
                "1 1 StackMoved item=\"Plain Rock\" count=5 from=bag#1[0] to=bag#2[0] cause=Grab",
                "2 0 IntentApplied intent=SetCursor(B, (0,2))",
                "2 1 CursorMoved loc=B bag=bag#1 from=(0,0) to=(0,2)",
                "2 0 IntentApplied intent=Drop(B)",
                "2 1 StackMoved item=\"Plain Rock\" count=5 from=bag#2[0] to=bag#1[2] cause=Drop"),
            Trace(ref s, new Intent.Grab(LocationId.B), new Intent.SetCursor(LocationId.B, new Position(0, 2)),
                new Intent.Drop(LocationId.B)));
    }

    [Fact]
    public void Drop_OntoTheSameType_Merges_AndPrimary_OntoAnother_Swaps()
    {
        Assert.Equal(Lines(
                "1 0 IntentApplied intent=ToolPrimary(B)",
                "1 1 StackMerged item=\"Plain Rock\" count=3 from=bag#1[0] to=bag#2[0] cause=Drop"),
            Trace(Sim(Board("R5 . . .", hand: new ItemStack(Rock, 3))), new Intent.ToolPrimary(LocationId.B)));

        Assert.Equal(Lines(
                "1 0 IntentApplied intent=ToolPrimary(B)",
                // the hand is first in canonical bag order, so its side of the exchange is listed first
                "1 1 StackMoved item=\"Rough Wood\" count=3 from=bag#1[0] to=bag#2[0] cause=Swap",
                "1 2 StackMoved item=\"Plain Rock\" count=5 from=bag#2[0] to=bag#1[0] cause=Swap"),
            Trace(Sim(Board("R5 . . .", hand: new ItemStack(Wood, 3))), new Intent.ToolPrimary(LocationId.B)));
    }

    [Fact]
    public void QuickSplit_And_ModalSplit_SplitIntoTheHand()
    {
        Assert.Equal(Lines(
                "1 0 IntentApplied intent=QuickSplit(B)",
                "1 1 StackSplit item=\"Plain Rock\" from=bag#1[0] to=bag#2[0] left=3 moved=2 cause=Split"),
            Trace(Sim(Board("R5 . . .")), new Intent.QuickSplit(LocationId.B)));

        Assert.Equal(Lines(
                "1 0 IntentApplied intent=ModalSplit(B, 1)",
                "1 1 StackSplit item=\"Plain Rock\" from=bag#1[0] to=bag#2[0] left=1 moved=4 cause=Split"),
            Trace(Sim(Board("R5 . . .")), new Intent.ModalSplit(LocationId.B, 1)));
    }

    [Fact]
    public void PickupToToolbar_FliesABarePickupToTheToolbar()
    {
        var game = Board("R5 . . .") with { ToolbarPickup = true };
        var toolbar = new Bag(Grid.Create(3, 1), "Toolbar");
        game = game with
        {
            Store = game.Store.Add(toolbar),
            Locations = game.Locations.Set(LocationId.T, Location.AtOrigin(toolbar.Id))
        };
        Assert.Equal(Lines(
                "1 0 IntentApplied intent=Primary(B)",
                "1 1 StackMoved item=\"Plain Rock\" count=5 from=bag#1[0] to=bag#2[0] cause=Pickup"),
            Trace(Sim(game), new Intent.Primary(LocationId.B)));
    }

    [Fact]
    public void Sort_RearrangesTheBag_WithItsPermutation()
    {
        // W1 R2 .  R3   →   R5 W1 .  .   (rock from old cell 1, merged with cell 3; wood from old cell 0)
        Assert.Equal(Lines(
                "1 0 IntentApplied intent=Sort(B)",
                "1 1 BagRearranged bag=bag#1 permutation=[1, 0, -1, -1]"),
            Trace(Sim(Board("W1 R2 . R3")), new Intent.Sort(LocationId.B)));
    }

    // ==================== Navigation ====================

    [Fact]
    public void Enter_Leave_LookIn()
    {
        var s = Sim(Board("b . . ."));
        Assert.Equal(Lines(
                "1 0 IntentApplied intent=EnterBag(B)",
                "1 1 BagEntered loc=B bag=bag#1 depth=1",
                "2 0 IntentApplied intent=LeaveBag(B)",
                "2 1 BagLeft loc=B bag=bag#2 depth=0",
                "3 0 IntentApplied intent=Peek(B)",
                "3 1 PanelOpened loc=C bag=bag#1",
                "4 0 IntentApplied intent=Leave(C)",
                "4 1 PanelClosed loc=C bag=bag#1"),
            Trace(ref s, new Intent.EnterBag(LocationId.B), new Intent.LeaveBag(LocationId.B),
                new Intent.Peek(LocationId.B), new Intent.Leave(LocationId.C)));
    }

    // ==================== Facilities ====================

    /// <summary>A root with a Workbench (axe recipe, 3 steps) at cell 1 whose inputs are loaded.</summary>
    private static GameState Workshop()
    {
        var workbench = FacilityBuilder.CreateWorkbench(AxeRecipe);
        workbench = workbench with
        {
            Grid = workbench.Grid
                .SetCell(0, workbench.Grid.GetCell(0) with { Stack = new ItemStack(Rock, 5) })
                .SetCell(1, workbench.Grid.GetCell(1) with { Stack = new ItemStack(Wood, 3) })
        };
        var game = Board("R5 . . .");
        var root = game.RootBag;
        root = root with { Grid = root.Grid.SetCell(1, new Cell(new ItemStack(WorkbenchType, 1, ContainedBagId: workbench.Id))) };
        return game with { Store = game.Store.Set(root.Id, root).Add(workbench) };
    }

    [Fact]
    public void Facility_Starts_CountsDown_Completes()
    {
        // The facility appears first (bag#1); the root holding its owner stack second (bag#2).
        Assert.Equal(Lines(
                "1 0 TimeAdvanced",
                "1 1 CraftStarted facility=bag#1 recipeId=\"workbench_axe\" owner=bag#2[1]",
                "2 0 TimeAdvanced",
                "2 1 CraftProgressed facility=bag#1 recipeId=\"workbench_axe\" remaining=1 owner=bag#2[1]",
                "3 0 TimeAdvanced",
                "3 1 CraftCompleted facilityBagId=bag#1 recipeId=\"workbench_axe\" outputs=[1×\"Stone Axe\"@bag#1[2]] consumed=[bag#1[0], bag#1[1]] owner=bag#2[1]"),
            Trace(Sim(Workshop()), new Intent.Tick(), new Intent.Tick(), new Intent.Tick()));
    }

    [Fact]
    public void Facility_Aborts_WhenItsInputsAreTaken()
    {
        var s = Sim(Workshop());
        s = Simulation.Step(s, new[] { new Intent.Tick() }).State; // started
        var bench = s.Game.Store.Facilities.Single();
        s = s with
        {
            Game = s.Game with
            {
                Store = s.Game.Store.Set(bench.Id, bench with { Grid = bench.Grid.SetCell(0, bench.Grid.GetCell(0) with { Stack = null }) })
            }
        };
        Assert.Equal(Lines(
                "2 0 TimeAdvanced",
                "2 1 CraftAborted facility=bag#1 recipeId=\"workbench_axe\" owner=bag#2[1]"),
            Trace(s, new Intent.Tick()));
    }

    [Fact]
    public void RecipeMenu_Confirm_SelectsTheRecipe()
    {
        var game = Workshop();
        var bench = game.Store.Facilities.Single();
        var s = Sim(game) with
        {
            RecipeMenu = new RecipeMenuState(bench.Id, bench.EnvironmentType,
                ImmutableArray.Create(AxeRecipe.Id), ImmutableArray.Create(AxeRecipe.Name), 0)
        };
        Assert.Equal(Lines(
                "1 0 IntentApplied intent=ConfirmRecipeMenu",
                "1 1 RecipeSelected facility=bag#1 recipeId=\"workbench_axe\" owner=bag#2[1] slotsRebuilt=false"),
            Trace(s, new Intent.ConfirmRecipeMenu()));
    }

    // ==================== Plants ====================

    /// <summary>A root with a Berry Bush on a planter at cell 0, <paramref name="progress"/> of 2 steps grown.</summary>
    private static GameState Garden(int progress)
    {
        var game = Board(". . . .");
        var bush = new ItemStack(Bush, 1)
            .WithProperty("Duration", new IntValue(2))
            .WithProperty("Progress", new IntValue(progress))
            .WithProperty("Produce", new StringValue(Berry.Name))
            .WithProperty("Yield", new IntValue(4));
        var root = game.RootBag with { Grid = game.RootBag.Grid.SetCell(0, new Cell(bush, Frame: new PlanterFrame())) };
        return game with { Store = game.Store.Set(root.Id, root) };
    }

    [Fact]
    public void Plant_Grows_ThenIsHarvested()
    {
        Assert.Equal(Lines(
                "1 0 TimeAdvanced",
                "1 1 PlantGrew cell=bag#1[0] stage=1 of=2"),
            Trace(Sim(Garden(0)), new Intent.Tick()));

        // Grown: the grab harvests its produce into the hand; the bush stays, its growth reset.
        Assert.Equal(Lines(
                "1 0 IntentApplied intent=Grab(B)",
                "1 1 Harvested cell=bag#1[0] yields=[4×\"Wild Berry\"@bag#2[0]]"),
            Trace(Sim(Garden(2)), new Intent.Grab(LocationId.B)));
    }

    // ==================== Acquire ====================

    [Fact]
    public void AcquireRandom_AcquiresOneDrawnItem()
    {
        // With one item type the draw is forced: one Plain Rock merges into the cell-0 stack.
        var game = Board("R5 . . .") with { ItemTypes = ImmutableArray.Create(Rock) };
        Assert.Equal(Lines(
                "1 0 IntentApplied intent=AcquireRandom(B)",
                "1 1 Acquired item=\"Plain Rock\" count=1 cell=bag#1[0] source=Acquire"),
            Trace(Sim(game), new Intent.AcquireRandom(LocationId.B)));
    }

    // ==================== Chrome, dialogue, recipes ====================

    [Fact]
    public void FirstPickup_OfARecipeCard_FiresChrome_AndLearnsTheRecipe()
    {
        var game = Board(". . . .") with { Ui = UiLedger.Empty };
        var card = new ItemStack(Card, 1).WithProperty(GameState.RecipeItemProperty, new StringValue("demo-axe"));
        game = game with { Store = game.Store.Set(game.RootBagId, game.RootBag with { Grid = game.RootBag.Grid.SetCell(0, new Cell(card)) }) };
        Assert.Equal(Lines(
                "1 0 IntentApplied intent=Grab(B)",
                "1 1 StackMoved item=\"Axe Recipe\" count=1 from=bag#1[0] to=bag#2[0] cause=Grab",
                "1 2 UiTriggerFired trigger=FirstPickup",
                "1 3 RecipeLearned recipeId=\"demo-axe\" card=bag#2[0]"),
            Trace(Sim(game), new Intent.Grab(LocationId.B)));
    }

    [Fact]
    public void Dialogue_QueuedByABump_ThenAdvancedAndDismissed()
    {
        var beat = new DialogueBeat("axe-absence", new DialogueTrigger(DialogueTriggerKind.NthTreeBump, 1),
            ImmutableArray.Create(new DialogueLine("Solid.", "me"), new DialogueLine("No path.", "me")));
        var beats = new DialogueBook(ImmutableDictionary<string, DialogueBeat>.Empty.Add(beat.Id, beat));
        var s = Sim(Board(". T . ."), beats: beats);
        Assert.Equal(Lines(
                "1 0 IntentApplied intent=Move(B, Right)",
                "1 1 Bumped loc=B bag=bag#1 at=(0,0) dir=Right reason=Unnavigable",
                "1 2 DialogueQueued beatId=\"axe-absence\"",
                "1 0 IntentApplied intent=AdvanceDialogue",
                "1 1 DialogueAdvanced beatId=\"axe-absence\" dismissed=false",
                "1 0 IntentApplied intent=AdvanceDialogue",
                "1 1 DialogueAdvanced beatId=\"axe-absence\" dismissed=true"),
            Trace(ref s, new Intent.Move(LocationId.B, Direction.Right), new Intent.AdvanceDialogue(), new Intent.AdvanceDialogue()));
    }

    // ==================== Rejections, rogue mode ====================

    [Fact]
    public void Rejection_IsTheOnlyEvent()
    {
        Assert.Equal("1 0 IntentRejected intent=CycleRecipe(B) reason=\"not in a facility\"",
            Trace(Sim(Board("R5 . . .")), new Intent.CycleRecipe(LocationId.B)));
    }

    [Fact]
    public void Rogue_ToolThenTick_InOneStep()
    {
        // The grab's own events, then the tick phase it triggers (the loaded workbench starts).
        Assert.Equal(Lines(
                "1 0 IntentApplied intent=Grab(B)",
                "1 1 StackMoved item=\"Plain Rock\" count=5 from=bag#1[0] to=bag#2[0] cause=Grab",
                "1 2 TimeAdvanced",
                "1 3 CraftStarted facility=bag#3 recipeId=\"workbench_axe\" owner=bag#1[1]"),
            Trace(Sim(Workshop(), TickMode.Rogue), new Intent.Grab(LocationId.B)));
    }

    // ==================== Belts (reserved, P1-D3) ====================

    [Fact]
    public void BeltAdvanced_IsOneLinePerChain()
    {
        Assert.Equal("4 2 BeltAdvanced chainId=7 dir=Right count=3 headCell=12",
            new EventText().Line(new SimEvent.BeltAdvanced(4, 2, 7, Direction.Right, 3, 12)));
    }

    // ==================== The coverage check bites ====================

    [Fact]
    public void Coverage_ReportsAChangeNoEventNames()
    {
        var s = Sim(Board("R5 . . ."));
        var r = Simulation.Step(s, new[] { new Intent.Grab(LocationId.B) });
        Assert.Empty(EventCoverage.Uncovered(s, r.State, r.Events));

        var withoutMove = r.Events.Where(e => e is not SimEvent.StackMoved).ToList();
        Assert.Equal(2, EventCoverage.Uncovered(s, r.State, withoutMove).Count); // the root cell and the hand slot

        var moved = Simulation.Step(s, new[] { new Intent.Move(LocationId.B, Direction.Right) });
        Assert.Equal(new[] { "location B" },
            EventCoverage.Uncovered(s, moved.State, moved.Events.Where(e => e is not SimEvent.CursorMoved).ToList()));
    }
}
