using Pockets.Core.Data;
using Pockets.Core.Models;

namespace Pockets.Core.Tests.Data;

public class PipelineParserTests
{
    [Fact]
    public void ParseStaticOutput()
    {
        var steps = PipelineParser.Parse("1 Stone Axe");

        Assert.Single(steps);
        var step = Assert.IsType<StaticItemStep>(steps[0]);
        Assert.Equal("Stone Axe", step.ItemName);
        Assert.Equal(1, step.Count);
    }

    [Fact]
    public void ParseStaticOutputWithLargerCount()
    {
        var steps = PipelineParser.Parse("5 Plain Rock");

        Assert.Single(steps);
        var step = Assert.IsType<StaticItemStep>(steps[0]);
        Assert.Equal("Plain Rock", step.ItemName);
        Assert.Equal(5, step.Count);
    }

    [Fact]
    public void ParseTemplateRef()
    {
        var steps = PipelineParser.Parse("@forest-6x4");

        Assert.Single(steps);
        var step = Assert.IsType<TemplateRefStep>(steps[0]);
        Assert.Equal("forest-6x4", step.TemplateId);
    }

    [Fact]
    public void ParseGeneratorOnly()
    {
        var steps = PipelineParser.Parse("!shuffle");

        Assert.Single(steps);
        var step = Assert.IsType<GeneratorStep>(steps[0]);
        Assert.Equal("shuffle", step.GeneratorId);
        Assert.Empty(step.TemplateArgs);
    }

    [Fact]
    public void ParseGeneratorWithTemplateArgs()
    {
        var steps = PipelineParser.Parse("!wilderness(@forest-6x4, @forest-materials)");

        Assert.Single(steps);
        var step = Assert.IsType<GeneratorStep>(steps[0]);
        Assert.Equal("wilderness", step.GeneratorId);
        Assert.Equal(2, step.TemplateArgs.Count);
        Assert.Equal("forest-6x4", step.TemplateArgs[0]);
        Assert.Equal("forest-materials", step.TemplateArgs[1]);
    }

    [Fact]
    public void ParseSimplePipeline()
    {
        var steps = PipelineParser.Parse("@forest -> !wilderness");

        Assert.Equal(2, steps.Count);
        var tmpl = Assert.IsType<TemplateRefStep>(steps[0]);
        Assert.Equal("forest", tmpl.TemplateId);
        var gen = Assert.IsType<GeneratorStep>(steps[1]);
        Assert.Equal("wilderness", gen.GeneratorId);
    }

    [Fact]
    public void ParseThreeStepPipeline()
    {
        var steps = PipelineParser.Parse("@forest -> !wilderness -> !shuffle");

        Assert.Equal(3, steps.Count);
        Assert.IsType<TemplateRefStep>(steps[0]);
        Assert.IsType<GeneratorStep>(steps[1]);
        Assert.IsType<GeneratorStep>(steps[2]);
    }

    [Fact]
    public void ParseStaticThenGenerator()
    {
        var steps = PipelineParser.Parse("1 Belt Pouch -> !attach-bag(@belt-pouch)");

        Assert.Equal(2, steps.Count);
        var stat = Assert.IsType<StaticItemStep>(steps[0]);
        Assert.Equal("Belt Pouch", stat.ItemName);
        Assert.Equal(1, stat.Count);
        var gen = Assert.IsType<GeneratorStep>(steps[1]);
        Assert.Equal("attach-bag", gen.GeneratorId);
        Assert.Single(gen.TemplateArgs);
        Assert.Equal("belt-pouch", gen.TemplateArgs[0]);
    }

    [Fact]
    public void ParseGeneratorWithSingleTemplateArg()
    {
        var steps = PipelineParser.Parse("!bag(@belt-pouch-2x3)");

        Assert.Single(steps);
        var gen = Assert.IsType<GeneratorStep>(steps[0]);
        Assert.Equal("bag", gen.GeneratorId);
        Assert.Single(gen.TemplateArgs);
        Assert.Equal("belt-pouch-2x3", gen.TemplateArgs[0]);
    }

    [Fact]
    public void WhitespaceAroundArrowIsTrimmed()
    {
        var steps = PipelineParser.Parse("@forest   ->   !wilderness");

        Assert.Equal(2, steps.Count);
        Assert.IsType<TemplateRefStep>(steps[0]);
        Assert.IsType<GeneratorStep>(steps[1]);
    }

    [Fact]
    public void WhitespaceInsideGeneratorArgsIsTrimmed()
    {
        var steps = PipelineParser.Parse("!wilderness( @forest-6x4 , @forest-materials )");

        var gen = Assert.IsType<GeneratorStep>(steps[0]);
        Assert.Equal("forest-6x4", gen.TemplateArgs[0]);
        Assert.Equal("forest-materials", gen.TemplateArgs[1]);
    }
}
