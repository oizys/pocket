using Pockets.Core;
using Pockets.Core.Data;
using Pockets.Core.Models;
using Pockets.Core.Rendering;

namespace Pockets.Core.Tests;

/// <summary>
/// The shared demo profile is the parity baseline: both frontends load it, so it must be
/// deterministic (fixed seed) and pin a single tick mode.
/// </summary>
public class DemoProfileTests
{
    private static DemoProfile Profile(int? seed = null) =>
        GameInitializer.CreateDemoProfile(ContentLoader.LoadFromDirectory(TestPaths.DataDir), seed);

    [Fact]
    public void CreateDemoProfile_IsDeterministic_AcrossCalls()
    {
        var a = Profile();
        var b = Profile();

        // Same seed → identical view-model and identical item census.
        Assert.Equal(
            ViewModelSerializer.SerializeToString(a.NewSession()),
            ViewModelSerializer.SerializeToString(b.NewSession()));
        Assert.Equal(
            InvariantChecker.Census(a.State),
            InvariantChecker.Census(b.State));
    }

    [Fact]
    public void CreateDemoProfile_PinsRogueTickMode()
    {
        Assert.Equal(TickMode.Rogue, Profile().TickMode);
        Assert.Equal(TickMode.Rogue, Profile().NewSession().TickMode);
    }

    [Fact]
    public void CreateDemoProfile_StartsOnRoot_WithEmptyHand()
    {
        var state = Profile().State;
        Assert.False(state.IsNested);
        Assert.False(state.HasItemsInHand);
        Assert.Equal(8, state.ActiveBag.Grid.Columns);
        Assert.Equal(4, state.ActiveBag.Grid.Rows);
    }
}
