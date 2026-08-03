using System.Collections.Immutable;
using Pockets.Core;
using Pockets.Core.Data;
using Pockets.Core.Models;
using Pockets.Core.Rendering;

namespace Pockets.Core.Tests.Rendering;

/// <summary>
/// Exercises the invariant pack (stack validity, progressability, conservation census)
/// the journey runner asserts after every step.
/// </summary>
public class InvariantCheckerTests
{
    private static readonly ItemType Rock = new("Plain Rock", Category.Material, IsStackable: true, MaxStackSize: 20);
    private static readonly ItemType Axe = new("Stone Axe", Category.Weapon, IsStackable: false, MaxStackSize: 1);

    /// <summary>Minimal state: 4×2 root grid + 1-slot hand, both in the store.</summary>
    private static GameState MakeState(params (int cell, ItemStack stack)[] placements)
    {
        var cells = Enumerable.Repeat(new Cell(), 8).ToArray();
        foreach (var (idx, stack) in placements)
            cells[idx] = new Cell(stack);
        var rootBag = new Bag(new Grid(4, 2, cells.ToImmutableArray()));
        var handBag = GameState.CreateHandBag();
        var store = BagStore.Empty.Add(rootBag).Add(handBag);
        return new GameState(store, LocationMap.Create(handBag.Id, rootBag.Id),
            ImmutableArray.Create(Rock, Axe));
    }

    [Fact]
    public void Check_CleanState_HasNoViolations()
    {
        var state = MakeState((0, new ItemStack(Rock, 5)));
        Assert.Empty(InvariantChecker.Check(state));
    }

    [Fact]
    public void Check_StackOverMax_IsReported()
    {
        var state = MakeState((0, new ItemStack(Rock, 25))); // max 20
        var v = InvariantChecker.Check(state);
        Assert.Contains(v, x => x.Rule == "stack-validity" && x.Detail.Contains("exceeds max"));
    }

    [Fact]
    public void Check_UniqueItemWithCountAboveOne_IsReported()
    {
        var state = MakeState((0, new ItemStack(Axe, 3))); // unique must be 1
        var v = InvariantChecker.Check(state);
        Assert.Contains(v, x => x.Rule == "stack-validity" && x.Detail.Contains("must be 1"));
    }

    [Fact]
    public void Check_FilterViolation_IsReported()
    {
        var cells = Enumerable.Repeat(new Cell(), 8).ToArray();
        // Cell filtered to Medicine but holding a Material.
        cells[0] = new Cell(new ItemStack(Rock, 1), CategoryFilter: Category.Medicine);
        var rootBag = new Bag(new Grid(4, 2, cells.ToImmutableArray()));
        var handBag = GameState.CreateHandBag();
        var store = BagStore.Empty.Add(rootBag).Add(handBag);
        var state = new GameState(store, LocationMap.Create(handBag.Id, rootBag.Id),
            ImmutableArray.Create(Rock, Axe));

        var v = InvariantChecker.Check(state);
        Assert.Contains(v, x => x.Rule == "stack-validity" && x.Detail.Contains("violates filter"));
    }

    [Fact]
    public void Check_CursorOutOfBounds_IsReported()
    {
        var baseState = MakeState((0, new ItemStack(Rock, 5)));
        var bLoc = baseState.Locations.Get(LocationId.B);
        var corrupt = baseState with
        {
            Locations = baseState.Locations.Set(LocationId.B, bLoc with { Cursor = new Cursor(new Position(9, 9)) })
        };
        var v = InvariantChecker.Check(corrupt);
        Assert.Contains(v, x => x.Rule == "progressability" && x.Detail.Contains("out of bounds"));
    }

    [Fact]
    public void Census_SumsCountsByName()
    {
        var state = MakeState((0, new ItemStack(Rock, 5)), (1, new ItemStack(Rock, 3)), (2, new ItemStack(Axe, 1)));
        var census = InvariantChecker.Census(state);
        Assert.Equal(8, census["Plain Rock"]);
        Assert.Equal(1, census["Stone Axe"]);
    }

    [Fact]
    public void CensusDelta_IsEmpty_WhenConserved()
    {
        var s1 = MakeState((0, new ItemStack(Rock, 5)));
        var s2 = MakeState((3, new ItemStack(Rock, 5))); // same item, different cell
        Assert.Empty(InvariantChecker.CensusDelta(InvariantChecker.Census(s1), InvariantChecker.Census(s2)));
    }

    [Fact]
    public void CensusDelta_ReportsChange_WhenItemsVanish()
    {
        var s1 = MakeState((0, new ItemStack(Rock, 5)));
        var s2 = MakeState((0, new ItemStack(Rock, 2)));
        var delta = InvariantChecker.CensusDelta(InvariantChecker.Census(s1), InvariantChecker.Census(s2));
        Assert.Single(delta);
        Assert.Contains("Plain Rock", delta[0]);
        Assert.Contains("-3", delta[0]);
    }

    [Fact]
    public void RealDemoProfile_StartsInvariantClean()
    {
        var session = GameInitializer.CreateDemoProfile(ContentLoader.LoadFromDirectory(TestPaths.DataDir)).NewSession();
        Assert.Empty(InvariantChecker.Check(session.Current));
    }
}
