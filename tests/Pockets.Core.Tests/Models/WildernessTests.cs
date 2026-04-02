using System.Collections.Immutable;
using Pockets.Core.Data;
using Pockets.Core.Models;

namespace Pockets.Core.Tests.Models;

public class WildernessTests
{
    private static readonly ItemType Rock = new("Plain Rock", Category.Material, IsStackable: true, MaxStackSize: 20);
    private static readonly ItemType Wood = new("Rough Wood", Category.Material, IsStackable: true, MaxStackSize: 20);
    private static readonly ItemType Herb = new("Wild Herb", Category.Medicine, IsStackable: true, MaxStackSize: 10);
    private static readonly ItemType ForestBagType = new("Forest Bag", Category.Bag, IsStackable: false);

    private static readonly ImmutableArray<ItemType> AllTypes =
        ImmutableArray.Create(Rock, Wood, Herb, ForestBagType);

    private static WildernessTemplate CreateForestTemplate(double fillRatio = 0.6) =>
        new("Forest", "Forest", "Green", 6, 4, fillRatio,
            ImmutableArray.Create<(ItemType, double)>((Rock, 2.0), (Wood, 1.0)));

    /// <summary>
    /// Creates a game state with a wilderness bag at root cell 0.
    /// Root bag is 4×3, wilderness bag is defined by the template.
    /// Optionally fills root bag with extra items (skipping cell 0).
    /// </summary>
    private static GameState CreateWithWildernessBag(
        WildernessTemplate? template = null,
        IEnumerable<ItemStack>? rootContents = null)
    {
        template ??= CreateForestTemplate();
        var wildernessBag = WildernessGenerator.Generate(template, new Random(42));

        var rootGrid = Grid.Create(4, 3);
        var bagCell = new Cell(new ItemStack(ForestBagType, 1, ContainedBagId: wildernessBag.Id));
        rootGrid = rootGrid.SetCell(0, bagCell);

        if (rootContents != null)
        {
            var (filledGrid, _) = rootGrid.AcquireItems(rootContents,
                ImmutableHashSet.Create(0));
            rootGrid = filledGrid;
        }

        var rootBag = new Bag(rootGrid);
        var handBag = GameState.CreateHandBag();
        var store = BagStore.Empty.Add(rootBag).Add(handBag).Add(wildernessBag);
        return new GameState(store, LocationMap.Create(handBag.Id, rootBag.Id), AllTypes);
    }

    // ==================== WildernessGenerator ====================

    [Fact]
    public void WildernessGenerator_ProducesCorrectDimensions()
    {
        var template = CreateForestTemplate();
        var bag = WildernessGenerator.Generate(template, new Random(42));

        Assert.Equal(6, bag.Grid.Columns);
        Assert.Equal(4, bag.Grid.Rows);
        Assert.Equal("Forest", bag.EnvironmentType);
        Assert.Equal("Green", bag.ColorScheme);
    }

    [Fact]
    public void WildernessGenerator_ProducesFilledBag()
    {
        var template = CreateForestTemplate(fillRatio: 0.6);
        var bag = WildernessGenerator.Generate(template, new Random(42));

        var totalCells = 6 * 4;
        var filledCells = bag.Grid.Cells.Count(c => !c.IsEmpty);

        // With 60% fill ratio, expect roughly 14-15 cells filled out of 24
        // but due to randomness, just check it's > 0 and < total
        Assert.True(filledCells > 0, "Expected some filled cells");
        Assert.True(filledCells < totalCells, "Expected some empty cells");
    }

    [Fact]
    public void WildernessGenerator_AllItemsHaveCount1()
    {
        var template = CreateForestTemplate();
        var bag = WildernessGenerator.Generate(template, new Random(42));

        var allCounts = bag.Grid.Cells
            .Where(c => !c.IsEmpty)
            .Select(c => c.Stack!.Count)
            .ToList();

        Assert.All(allCounts, count => Assert.Equal(1, count));
    }

    [Fact]
    public void WildernessGenerator_OnlyUsesLootTableItems()
    {
        var template = CreateForestTemplate();
        var bag = WildernessGenerator.Generate(template, new Random(42));

        var validTypes = new HashSet<ItemType> { Rock, Wood };
        var usedTypes = bag.Grid.Cells
            .Where(c => !c.IsEmpty)
            .Select(c => c.Stack!.ItemType)
            .ToHashSet();

        Assert.All(usedTypes, t => Assert.Contains(t, validTypes));
    }

    [Fact]
    public void WildernessGenerator_FullFill_FillsAllCells()
    {
        var template = CreateForestTemplate(fillRatio: 1.0);
        var bag = WildernessGenerator.Generate(template, new Random(42));

        var filledCells = bag.Grid.Cells.Count(c => !c.IsEmpty);
        Assert.Equal(6 * 4, filledCells);
    }

    [Fact]
    public void WildernessGenerator_ZeroFill_ProducesEmptyBag()
    {
        var template = CreateForestTemplate(fillRatio: 0.0);
        var bag = WildernessGenerator.Generate(template, new Random(42));

        var filledCells = bag.Grid.Cells.Count(c => !c.IsEmpty);
        Assert.Equal(0, filledCells);
    }

    // ==================== ToolHarvest ====================

    [Fact]
    public void Harvest_AtRoot_Fails()
    {
        var state = CreateWithWildernessBag();
        var result = state.ToolHarvest();

        Assert.False(result.Success);
        Assert.Equal("Not in a bag", result.Error);
    }

    [Fact]
    public void Harvest_EmptyCell_NoOp()
    {
        var template = CreateForestTemplate(fillRatio: 0.0); // empty wilderness
        var state = CreateWithWildernessBag(template);
        var entered = state.EnterBag().State;

        var result = entered.ToolHarvest();

        Assert.True(result.Success);
        Assert.Equal(entered, result.State); // no change
    }

    [Fact]
    public void Harvest_InsideBag_RemovesAndAcquiresIntoParent()
    {
        var template = CreateForestTemplate(fillRatio: 1.0);
        var state = CreateWithWildernessBag(template);
        var entered = state.EnterBag().State;

        // Cursor is at (0,0) which should have an item
        var itemToHarvest = entered.CurrentCell.Stack!;
        Assert.NotNull(itemToHarvest);

        var result = entered.ToolHarvest();

        Assert.True(result.Success);

        // Cursor cell in active bag should now be empty
        Assert.True(result.State.CurrentCell.IsEmpty);

        // Item should be in the root bag (parent)
        var rootNonEmpty = result.State.RootBag.Grid.Cells
            .Where(c => !c.IsEmpty && c.Stack!.ItemType != ForestBagType)
            .ToList();
        Assert.Single(rootNonEmpty);
        Assert.Equal(itemToHarvest.ItemType, rootNonEmpty[0].Stack!.ItemType);
        Assert.Equal(1, rootNonEmpty[0].Stack!.Count);
    }

    [Fact]
    public void Harvest_ParentFull_Fails()
    {
        // Fill root bag completely except cell 0 (which has the wilderness bag)
        var filler = Enumerable.Range(0, 11)
            .Select(_ => new ItemStack(Herb, Herb.MaxStackSize))
            .ToList();

        var state = CreateWithWildernessBag(
            template: CreateForestTemplate(fillRatio: 1.0),
            rootContents: filler);

        var entered = state.EnterBag().State;
        var result = entered.ToolHarvest();

        Assert.False(result.Success);
        Assert.Equal("Parent bag is full", result.Error);
    }

    [Fact]
    public void Harvest_DoesNotOverwriteInnerBagCell()
    {
        var template = CreateForestTemplate(fillRatio: 1.0);
        var state = CreateWithWildernessBag(template);
        var entered = state.EnterBag().State;

        var result = entered.ToolHarvest();
        Assert.True(result.Success);

        // Cell 0 in root should still be the wilderness bag
        var rootCell0 = result.State.RootBag.Grid.GetCell(0);
        Assert.Equal(ForestBagType, rootCell0.Stack!.ItemType);
        Assert.NotNull(rootCell0.Stack.ContainedBagId);
    }

    [Fact]
    public void Harvest_MultipleItems_AccumulatesInParent()
    {
        var template = CreateForestTemplate(fillRatio: 1.0);
        var state = CreateWithWildernessBag(template);
        var entered = state.EnterBag().State;

        // Harvest first item
        var result1 = entered.ToolHarvest();
        Assert.True(result1.Success);

        // Move to next cell and harvest again
        var moved = result1.State.MoveCursor(Direction.Right);
        var result2 = moved.ToolHarvest();
        Assert.True(result2.Success);

        // Parent bag should have 2 harvested items (may be merged if same type)
        var rootNonBag = result2.State.RootBag.Grid.Cells
            .Where(c => !c.IsEmpty && c.Stack!.ItemType != ForestBagType)
            .Sum(c => c.Stack!.Count);
        Assert.Equal(2, rootNonBag);
    }

    // ==================== GameSession integration ====================

    [Fact]
    public void Harvest_ViaSession_Undoable()
    {
        var template = CreateForestTemplate(fillRatio: 1.0);
        var state = CreateWithWildernessBag(template);
        var session = GameSession.New(state);

        // Enter bag
        session = session.ExecuteEnterBag();
        Assert.True(session.Current.IsNested);

        // Harvest
        var itemBefore = session.Current.CurrentCell.Stack;
        session = session.ExecuteHarvest();

        // Cell should be empty after harvest
        Assert.True(session.Current.CurrentCell.IsEmpty);
        Assert.Equal(2, session.UndoDepth); // enter + harvest

        // Undo harvest
        session = session.Undo()!;
        Assert.False(session.Current.CurrentCell.IsEmpty);
        Assert.Equal(itemBefore!.ItemType, session.Current.CurrentCell.Stack!.ItemType);
    }

    [Fact]
    public void Harvest_ViaSession_LogsAction()
    {
        var template = CreateForestTemplate(fillRatio: 1.0);
        var state = CreateWithWildernessBag(template);
        var session = GameSession.New(state);

        session = session.ExecuteEnterBag();
        var itemName = session.Current.CurrentCell.Stack!.ItemType.Name;
        session = session.ExecuteHarvest();

        Assert.Contains(session.ActionLog, log => log.Contains("Harvest") && log.Contains(itemName));
    }

    [Fact]
    public void Harvest_ViaSession_AtRoot_LogsFailure()
    {
        var state = CreateWithWildernessBag();
        var session = GameSession.New(state);

        session = session.ExecuteHarvest();

        Assert.Contains(session.ActionLog, log => log.Contains("FAILED") && log.Contains("Not in a bag"));
    }
}
