using System.Collections.Immutable;
using Pockets.Core.Data;
using Pockets.Core.Models;

namespace Pockets.Core.Tests.Data;

public class GeneratorBuiltinsTests
{
    private static readonly ItemType Rock = new("Plain Rock", Category.Material, IsStackable: true);
    private static readonly ItemType Wood = new("Rough Wood", Category.Material, IsStackable: true);
    private static readonly ItemType Pouch = new("Belt Pouch", Category.Bag, IsStackable: false);

    // ==================== bag generator ====================

    [Fact]
    public void Bag_FromTemplateInput_CreatesBag()
    {
        var template = new GridTemplate("pouch-3x2", 3, 2, "Pouch", "Brown");
        var input = new TemplateValue("pouch-3x2", template);

        var result = GeneratorBuiltins.Bag(input, Array.Empty<object>());

        var bv = Assert.IsType<BagValue>(result);
        Assert.Equal(3, bv.Bag.Grid.Columns);
        Assert.Equal(2, bv.Bag.Grid.Rows);
        Assert.Equal("Pouch", bv.Bag.EnvironmentType);
        Assert.Equal("Brown", bv.Bag.ColorScheme);
    }

    [Fact]
    public void Bag_FromTemplateArg_CreatesBag()
    {
        var template = new GridTemplate("chest-4x4", 4, 4, "Chest", "Red");

        var result = GeneratorBuiltins.Bag(null, new object[] { template });

        var bv = Assert.IsType<BagValue>(result);
        Assert.Equal(4, bv.Bag.Grid.Columns);
        Assert.Equal(4, bv.Bag.Grid.Rows);
    }

    // ==================== wilderness generator ====================

    [Fact]
    public void Wilderness_CreatesPopulatedBag()
    {
        var gridT = new GridTemplate("forest", 4, 3, "Forest", "Green");
        var lootT = new LootTableTemplate("loot",
            ImmutableArray.Create(new LootTableEntry("Plain Rock", 1.0)), 1.0);
        var items = ImmutableDictionary<string, ItemType>.Empty.Add("Plain Rock", Rock);

        var result = GeneratorBuiltins.Wilderness(null, new object[] { gridT, lootT, items });

        var bv = Assert.IsType<BagValue>(result);
        Assert.Equal(4, bv.Bag.Grid.Columns);
        Assert.Equal(3, bv.Bag.Grid.Rows);
        Assert.Equal("Forest", bv.Bag.EnvironmentType);
        // With fill ratio 1.0, all cells should be filled
        var filledCount = Enumerable.Range(0, 12)
            .Count(i => !bv.Bag.Grid.GetCell(i).IsEmpty);
        Assert.Equal(12, filledCount);
    }

    [Fact]
    public void Wilderness_RespectsTemplate()
    {
        var gridT = new GridTemplate("cave", 2, 2, "Cave", "Gray");
        var lootT = new LootTableTemplate("loot",
            ImmutableArray.Create(new LootTableEntry("Plain Rock", 1.0)), 0.0);
        var items = ImmutableDictionary<string, ItemType>.Empty.Add("Plain Rock", Rock);

        var result = GeneratorBuiltins.Wilderness(null, new object[] { gridT, lootT, items });

        var bv = Assert.IsType<BagValue>(result);
        Assert.Equal("Cave", bv.Bag.EnvironmentType);
        // With fill ratio 0.0, no cells should be filled
        var filledCount = Enumerable.Range(0, 4)
            .Count(i => !bv.Bag.Grid.GetCell(i).IsEmpty);
        Assert.Equal(0, filledCount);
    }

    // ==================== attach-bag generator ====================

    [Fact]
    public void AttachBag_SetsContainedBagOnStacks()
    {
        var template = new GridTemplate("pouch", 3, 2, "Pouch", "Brown");
        var stacks = new StacksValue(new[] { new ItemStack(Pouch, 1) });

        var result = GeneratorBuiltins.AttachBag(stacks, new object[] { template });

        var sv = Assert.IsType<StacksValue>(result);
        Assert.Single(sv.Stacks);
        Assert.NotNull(sv.Stacks[0].ContainedBagId);
        Assert.NotNull(sv.NewBags);
        var attachedBag = sv.NewBags!.First(b => b.Id == sv.Stacks[0].ContainedBagId);
        Assert.Equal(3, attachedBag.Grid.Columns);
        Assert.Equal("Pouch", attachedBag.EnvironmentType);
    }

    // ==================== shuffle generator ====================

    [Fact]
    public void Shuffle_BagValue_PreservesBagProperties()
    {
        var grid = Grid.Create(4, 2);
        grid = grid.SetCell(0, new Cell(new ItemStack(Rock, 3)));
        grid = grid.SetCell(1, new Cell(new ItemStack(Wood, 2)));
        var bag = new Bag(grid, "Test", "Blue");
        var input = new BagValue(bag);

        var result = GeneratorBuiltins.Shuffle(input, Array.Empty<object>());

        var bv = Assert.IsType<BagValue>(result);
        Assert.Equal("Test", bv.Bag.EnvironmentType);
        Assert.Equal(4, bv.Bag.Grid.Columns);
        // Same number of non-empty cells
        var origFilled = Enumerable.Range(0, 8).Count(i => !bag.Grid.GetCell(i).IsEmpty);
        var newFilled = Enumerable.Range(0, 8).Count(i => !bv.Bag.Grid.GetCell(i).IsEmpty);
        Assert.Equal(origFilled, newFilled);
    }

    // ==================== GetAll provides all built-ins ====================

    [Fact]
    public void GetAll_ReturnsExpectedGenerators()
    {
        var items = ImmutableDictionary<string, ItemType>.Empty;
        var generators = GeneratorBuiltins.GetAll(items);

        Assert.True(generators.ContainsKey("bag"));
        Assert.True(generators.ContainsKey("wilderness"));
        Assert.True(generators.ContainsKey("attach-bag"));
        Assert.True(generators.ContainsKey("shuffle"));
    }
}
