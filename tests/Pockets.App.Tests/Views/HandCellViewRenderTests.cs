using System.Collections.Immutable;
using Pockets.Core.Models;
using Pockets.App.Views;
using Pockets.App.Rendering;

namespace Pockets.App.Tests.Views;

/// <summary>
/// Tests that HandCellView renders the new 3×2 glyph cell. Hand is a single
/// 3×2 cell — row 1 shows glyph + count, row 2 is the (always empty) frame row.
/// </summary>
public class HandCellViewRenderTests : IDisposable
{
    private TuiTestHarness? _harness;

    private static readonly ItemType Rock = new("Rock", Category.Material, IsStackable: true, MaxStackSize: 20);
    private static readonly ImmutableArray<ItemType> AllTypes = ImmutableArray.Create(Rock);

    private static GameState EmptyHandState()
    {
        var grid = Grid.Create(2, 1);
        var rootBag = new Bag(grid);
        var handBag = GameState.CreateHandBag();
        var store = BagStore.Empty.Add(rootBag).Add(handBag);
        return new GameState(store, LocationMap.Create(handBag.Id, rootBag.Id), AllTypes);
    }

    private static GameState HoldingState()
    {
        var grid = Grid.Create(2, 1);
        var rootBag = new Bag(grid);
        var handBag = new Bag(Grid.Create(1, 1));
        var (filledHand, _) = handBag.AcquireItems(new[] { new ItemStack(Rock, 5) });
        var store = BagStore.Empty.Add(rootBag).Add(filledHand);
        return new GameState(store, LocationMap.Create(filledHand.Id, rootBag.Id), AllTypes);
    }

    private HandCellView SetupHandView(GameState state)
    {
        _harness = TuiTestHarness.Create();
        var view = new HandCellView(state) { X = 0, Y = 0 };
        _harness.AddView(view);
        _harness.Render();
        return view;
    }

    public void Dispose()
    {
        _harness?.Dispose();
    }

    [Fact]
    public void EmptyHand_RendersThreeSpaces_TwoRows()
    {
        SetupHandView(EmptyHandState());
        Assert.Equal("   ", _harness!.GetText(0, 0, 3));
        Assert.Equal("   ", _harness.GetText(0, 1, 3));
    }

    [Fact]
    public void HoldingItem_RendersGlyphAndCount()
    {
        SetupHandView(HoldingState());
        Assert.Equal("R 5", _harness!.GetText(0, 0, 3));
        Assert.Equal("   ", _harness.GetText(0, 1, 3));
    }

    [Fact]
    public void HoldingItem_HasDifferentAttributeThanEmpty()
    {
        var view = SetupHandView(EmptyHandState());
        var emptyAttr = _harness!.GetAttribute(0, 0);

        view.UpdateState(HoldingState());
        _harness.Render();

        var holdingAttr = _harness.GetAttribute(0, 0);
        Assert.NotEqual(emptyAttr, holdingAttr);
    }
}
