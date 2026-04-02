using System.Collections.Immutable;
using Pockets.Core.Data;
using Pockets.Core.Models;

namespace Pockets.Core.Tests.Data;

public class PipelineExecutorTests
{
    private static readonly ItemType Rock = new("Plain Rock", Category.Material, IsStackable: true);
    private static readonly ItemType Wood = new("Rough Wood", Category.Material, IsStackable: true);
    private static readonly ItemType Axe = new("Stone Axe", Category.Tool, IsStackable: false);

    private static readonly ImmutableDictionary<string, ItemType> Items =
        ImmutableDictionary<string, ItemType>.Empty
            .Add("Plain Rock", Rock)
            .Add("Rough Wood", Wood)
            .Add("Stone Axe", Axe);

    [Fact]
    public void Execute_StaticOnly_ProducesStacks()
    {
        var steps = PipelineParser.Parse("1 Stone Axe");
        var result = PipelineExecutor.Execute(steps, Items,
            ImmutableDictionary<string, object>.Empty,
            ImmutableDictionary<string, GeneratorFunc>.Empty);

        var stacks = Assert.IsType<StacksValue>(result);
        Assert.Single(stacks.Stacks);
        Assert.Equal(Axe, stacks.Stacks[0].ItemType);
        Assert.Equal(1, stacks.Stacks[0].Count);
    }

    [Fact]
    public void Execute_TemplateRef_ProducesTemplateValue()
    {
        var template = new GridTemplate("belt-pouch", 3, 2, "Pouch", "Brown");
        var templates = ImmutableDictionary<string, object>.Empty.Add("belt-pouch", template);
        var steps = PipelineParser.Parse("@belt-pouch");

        var result = PipelineExecutor.Execute(steps, Items, templates,
            ImmutableDictionary<string, GeneratorFunc>.Empty);

        var tv = Assert.IsType<TemplateValue>(result);
        Assert.Equal("belt-pouch", tv.Id);
        Assert.Same(template, tv.Template);
    }

    [Fact]
    public void Execute_TemplateToGenerator_Pipeline()
    {
        var template = new GridTemplate("small-bag", 3, 2, "Test", "Blue");
        var templates = ImmutableDictionary<string, object>.Empty.Add("small-bag", template);

        GeneratorFunc bagGen = (input, args) =>
        {
            var gt = (GridTemplate)(input is TemplateValue tv ? tv.Template : args[0]);
            var bag = new Bag(Grid.Create(gt.Columns, gt.Rows), gt.EnvironmentType, gt.ColorScheme);
            return new BagValue(bag);
        };
        var generators = ImmutableDictionary<string, GeneratorFunc>.Empty.Add("bag", bagGen);

        var steps = PipelineParser.Parse("@small-bag -> !bag");
        var result = PipelineExecutor.Execute(steps, Items, templates, generators);

        var bv = Assert.IsType<BagValue>(result);
        Assert.Equal(3, bv.Bag.Grid.Columns);
        Assert.Equal(2, bv.Bag.Grid.Rows);
        Assert.Equal("Test", bv.Bag.EnvironmentType);
    }

    [Fact]
    public void Execute_GeneratorWithInlineTemplateArgs()
    {
        var gridT = new GridTemplate("forest-6x4", 6, 4, "Forest", "Green");
        var lootT = new LootTableTemplate("forest-loot",
            ImmutableArray.Create(new LootTableEntry("Plain Rock", 1.0)), 0.5);
        var templates = ImmutableDictionary<string, object>.Empty
            .Add("forest-6x4", gridT)
            .Add("forest-loot", lootT);

        GeneratorFunc wildGen = (input, args) =>
        {
            // args should contain the resolved templates
            Assert.Equal(2, args.Count);
            Assert.IsType<GridTemplate>(args[0]);
            Assert.IsType<LootTableTemplate>(args[1]);
            var gt = (GridTemplate)args[0];
            return new BagValue(new Bag(Grid.Create(gt.Columns, gt.Rows), gt.EnvironmentType, gt.ColorScheme));
        };
        var generators = ImmutableDictionary<string, GeneratorFunc>.Empty.Add("wilderness", wildGen);

        var steps = PipelineParser.Parse("!wilderness(@forest-6x4, @forest-loot)");
        var result = PipelineExecutor.Execute(steps, Items, templates, generators);

        var bv = Assert.IsType<BagValue>(result);
        Assert.Equal(6, bv.Bag.Grid.Columns);
    }

    [Fact]
    public void Execute_StaticThenGenerator_PipelineChain()
    {
        var template = new GridTemplate("pouch", 2, 2, "Pouch", "Brown");
        var templates = ImmutableDictionary<string, object>.Empty.Add("pouch", template);

        GeneratorFunc attachBag = (input, args) =>
        {
            var stacks = ((StacksValue)input!).Stacks;
            var gt = (GridTemplate)args[0];
            var bag = new Bag(Grid.Create(gt.Columns, gt.Rows), gt.EnvironmentType, gt.ColorScheme);
            var withBag = stacks.Select(s => s with { ContainedBagId = bag.Id }).ToList();
            return new StacksValue(withBag, new[] { bag });
        };
        var generators = ImmutableDictionary<string, GeneratorFunc>.Empty.Add("attach-bag", attachBag);

        var steps = PipelineParser.Parse("1 Stone Axe -> !attach-bag(@pouch)");
        var result = PipelineExecutor.Execute(steps, Items, templates, generators);

        var sv = Assert.IsType<StacksValue>(result);
        Assert.Single(sv.Stacks);
        Assert.Equal(Axe, sv.Stacks[0].ItemType);
        Assert.NotNull(sv.Stacks[0].ContainedBagId);
        Assert.NotNull(sv.NewBags);
        var attachedBag = sv.NewBags!.First(b => b.Id == sv.Stacks[0].ContainedBagId);
        Assert.Equal(2, attachedBag.Grid.Columns);
    }

    [Fact]
    public void Execute_ThreeStepPipeline()
    {
        var template = new GridTemplate("t", 2, 2);
        var templates = ImmutableDictionary<string, object>.Empty.Add("t", template);

        GeneratorFunc gen1 = (input, args) =>
        {
            var gt = (GridTemplate)((TemplateValue)input!).Template;
            return new BagValue(new Bag(Grid.Create(gt.Columns, gt.Rows)));
        };
        GeneratorFunc gen2 = (input, args) => input!; // passthrough
        var generators = ImmutableDictionary<string, GeneratorFunc>.Empty
            .Add("bag", gen1)
            .Add("shuffle", gen2);

        var steps = PipelineParser.Parse("@t -> !bag -> !shuffle");
        var result = PipelineExecutor.Execute(steps, Items, templates, generators);

        Assert.IsType<BagValue>(result);
    }
}
