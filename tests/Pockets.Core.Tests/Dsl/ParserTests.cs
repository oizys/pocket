using System.Collections.Immutable;
using Pockets.Core.Dsl;
using Pockets.Core.Models;

namespace Pockets.Core.Tests.Dsl;

public class ParserTests
{
    [Fact]
    public void Parse_SingleOpcode()
    {
        var nodes = DslParser.Parse("grab");
        Assert.Single(nodes);
        Assert.IsType<OpNode>(nodes[0]);
        Assert.Equal("grab", ((OpNode)nodes[0]).Name);
    }

    [Fact]
    public void Parse_MultipleOpcodes()
    {
        var nodes = DslParser.Parse("right harvest grab drop");
        Assert.Equal(4, nodes.Length);
        Assert.All(nodes, n => Assert.IsType<OpNode>(n));
    }

    [Fact]
    public void Parse_LocationId_PushesAsImmediate()
    {
        var nodes = DslParser.Parse("B grab");
        Assert.Equal(2, nodes.Length);
        var push = Assert.IsType<OpNode>(nodes[0]);
        Assert.Equal("__push_location", push.Name);
        Assert.Single(push.Immediates);
        Assert.Equal(LocationId.B, push.Immediates[0]);
    }

    [Fact]
    public void Parse_Integer_PushesAsImmediate()
    {
        var nodes = DslParser.Parse("16 split-at");
        Assert.Equal(2, nodes.Length);
        var push = Assert.IsType<OpNode>(nodes[0]);
        Assert.Equal("__push_int", push.Name);
        Assert.Equal(16, push.Immediates[0]);
    }

    [Fact]
    public void Parse_Quotation_Times()
    {
        // "[ right harvest ] 3 times" restructures to: push_int(3), TimesNode([right, harvest])
        var nodes = DslParser.Parse("[ right harvest ] 3 times");
        Assert.Equal(2, nodes.Length);
        var intPush = Assert.IsType<OpNode>(nodes[0]);
        Assert.Equal("__push_int", intPush.Name);
        Assert.Equal(3, intPush.Immediates[0]);
        var times = Assert.IsType<TimesNode>(nodes[1]);
        Assert.Equal(2, times.Body.Length);
    }

    [Fact]
    public void Parse_TryBlock_Postfix()
    {
        var nodes = DslParser.Parse("[ grab drop ] try");
        Assert.Single(nodes);
        var tryNode = Assert.IsType<TryNode>(nodes[0]);
        Assert.Equal(2, tryNode.Body.Length);
    }

    [Fact]
    public void Parse_IfOkBlock_Postfix()
    {
        var nodes = DslParser.Parse("[ drop ] if-ok");
        Assert.Single(nodes);
        var ifOk = Assert.IsType<IfOkNode>(nodes[0]);
        Assert.Single(ifOk.Body);
    }

    [Fact]
    public void Parse_MacroDef_ExpandsInline()
    {
        var nodes = DslParser.Parse(":def harvest-and-grab harvest grab ; harvest-and-grab");
        // :def → DefNode (no-op at runtime), then the expanded macro
        Assert.Equal(3, nodes.Length); // DefNode, harvest, grab
        Assert.IsType<DefNode>(nodes[0]);
        Assert.Equal("harvest", ((OpNode)nodes[1]).Name);
        Assert.Equal("grab", ((OpNode)nodes[2]).Name);
    }

    [Fact]
    public void Parse_LocationId_CaseInsensitive()
    {
        var nodes = DslParser.Parse("b grab");
        var push = Assert.IsType<OpNode>(nodes[0]);
        Assert.Equal(LocationId.B, push.Immediates[0]);
    }

    [Fact]
    public void Parse_EmptyString_ReturnsEmpty()
    {
        var nodes = DslParser.Parse("");
        Assert.Empty(nodes);
    }

    [Fact]
    public void Parse_EachBlock_Postfix()
    {
        var nodes = DslParser.Parse("[ grab ] each");
        Assert.Single(nodes);
        var each = Assert.IsType<EachNode>(nodes[0]);
        Assert.Single(each.Body);
    }

    [Fact]
    public void Parse_Combinator_WithoutQuotation_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => DslParser.Parse("grab try"));
    }

    [Fact]
    public void Parse_NestedQuotations()
    {
        var nodes = DslParser.Parse("[ [ right ] 2 times grab ] try");
        Assert.Single(nodes);
        var tryNode = Assert.IsType<TryNode>(nodes[0]);
        Assert.True(tryNode.Body.Length >= 2); // push_int(2), TimesNode, grab
    }
}
