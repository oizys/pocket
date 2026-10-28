using Pockets.Core.Sim;

namespace Pockets.Core.Tests.Sim;

/// <summary>Shared bag naming between text formatters.</summary>
public class BagNamesTests
{
    /// <summary>Two EventTexts on one BagNames name bags consistently.</summary>
    [Fact]
    public void SharedNames_AreConsistent()
    {
        var names = new BagNames();
        var a = new EventText(names);
        var b = new EventText(names);
        var g1 = Guid.NewGuid();
        var g2 = Guid.NewGuid();
        Assert.Equal("bag#1", a.Names.Name(g1));
        Assert.Equal("bag#2", b.Names.Name(g2));
        Assert.Equal("bag#1", b.Names.Name(g1));
        Assert.Same(names, a.Names);
    }

    /// <summary>A default-constructed EventText starts at bag#1, independent of others.</summary>
    [Fact]
    public void Default_StartsAtBag1()
    {
        var g = Guid.NewGuid();
        Assert.Equal("bag#1", new EventText().Names.Name(g));
        Assert.Equal("bag#1", new EventText().Names.Name(Guid.NewGuid()));
    }
}
