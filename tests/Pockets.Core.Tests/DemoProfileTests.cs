using System.Collections.Immutable;
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

    /// <summary>Resolves the bag contained in a root cell, or null if the cell holds no bag.</summary>
    private static Bag? BagAt(GameState state, int cellIndex)
    {
        var stack = state.RootBag.Grid.GetCell(cellIndex).Stack;
        return stack?.ContainedBagId is { } id ? state.Store.GetById(id) : null;
    }

    [Fact]
    public void CreateDemoProfile_SeedsPeekableChest_AtFreeCell9()
    {
        var chest = BagAt(Profile().State, 9); // (1,1) — first free cell after the Belt Pouch at 8
        Assert.NotNull(chest);
        Assert.Equal("Chest", chest!.EnvironmentType);
        Assert.False(chest.EnterOnly);                    // peekable — C looks in, no refusal
        Assert.Contains(chest.Grid.Cells, cell => !cell.IsEmpty); // has a couple of finds inside
    }

    [Fact]
    public void CreateDemoProfile_SeedsEnterOnlyPlaceholder_AtFreeCell10()
    {
        var pocket = BagAt(Profile().State, 10); // (1,2)
        Assert.NotNull(pocket);
        Assert.Equal("Quiet Pocket", pocket!.EnvironmentType);
        Assert.True(pocket.EnterOnly);                    // C is refused; E enters
    }

    [Fact]
    public void NonDemoProfiles_HaveNoEnterOnlyBags()
    {
        // The Slice-4 content lives only in the demo profile; the classic stages are untouched.
        var types = ImmutableArray.Create(new ItemType("Rock", Category.Material, IsStackable: true));
        var stage1 = GameState.CreateStage1(types, new[] { new ItemStack(types[0], 5) });
        Assert.DoesNotContain(stage1.Store.All, bag => bag.EnterOnly);
    }
}
