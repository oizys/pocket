using System.Globalization;
using Pockets.Core.Models;
using Pockets.Core.Sim;

namespace Pockets.Realization.Tests;

/// <summary>The canonical pose line.</summary>
public class PoseTextTests
{
    private static readonly Guid BagA = Guid.NewGuid();
    private static readonly Guid BagB = Guid.NewGuid();

    /// <summary>A body pose line.</summary>
    [Fact]
    public void Line_Body()
    {
        var p = new Pose(new EntityKey.Body(LocationId.B), BagA, 2, 1, 3, -4, null, Facing.S);
        Assert.Equal("entity=Body(B) bag=bag#1 col=2 row=1 subX=3 subY=-4 height=- facing=S", new PoseText().Line(p));
    }

    /// <summary>A stack pose line.</summary>
    [Fact]
    public void Line_Stack()
    {
        var p = new Pose(new EntityKey.Stack(new CellRef(BagA, 4)), BagA, 4, 0, 0, 0, 16, null);
        Assert.Equal("entity=Stack(bag#1[4]) bag=bag#1 col=4 row=0 subX=0 subY=0 height=16 facing=-", new PoseText().Line(p));
    }

    /// <summary>One BagNames shared with an EventText keeps a bag's name stable across both.</summary>
    [Fact]
    public void SharedNames_WithEventText()
    {
        var names = new BagNames();
        Assert.Equal("bag#1", names.Name(BagA));
        var text = new PoseText(names);
        var p = new Pose(new EntityKey.Body(LocationId.H), BagB, 0, 0, 0, 0, null, null);
        Assert.Contains("bag=bag#2", text.Line(p));
        Assert.Equal("bag#1", new EventText(names).Names.Name(BagA));
    }

    /// <summary>Static Lines starts a fresh naming at bag#1.</summary>
    [Fact]
    public void Lines_StartAtBag1()
    {
        var p = new Pose(new EntityKey.Body(LocationId.H), BagB, 0, 0, 0, 0, null, null);
        var lines = PoseText.Lines(new[] { p, p });
        Assert.Equal(2, lines.Length);
        Assert.All(lines, l => Assert.Contains("bag=bag#1", l));
    }

    /// <summary>Output ignores the current culture.</summary>
    [Theory]
    [InlineData("ar-SA")]
    [InlineData("fr-FR")]
    [InlineData("tr-TR")]
    public void Line_IsCultureInvariant(string culture)
    {
        var old = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(culture);
            var p = new Pose(new EntityKey.Body(LocationId.B), BagA, -2, -1, -8, 8, -3, Facing.NW);
            Assert.Equal("entity=Body(B) bag=bag#1 col=-2 row=-1 subX=-8 subY=8 height=-3 facing=NW", new PoseText().Line(p));
        }
        finally { CultureInfo.CurrentCulture = old; }
    }
}
