using System.Collections.Immutable;
using Terminal.Gui;
using Pockets.Core.Models;
using Pockets.App.Views;
using Pockets.App.Rendering;

namespace Pockets.App.Tests.Views;

/// <summary>
/// Tests that HandCellView renders correctly in empty and holding states.
/// </summary>
public class HandCellViewRenderTests : IDisposable
{
    private TuiTestHarness? _harness;

    private static readonly ItemType Rock = new("Rock", Category.Material, IsStackable: true, MaxStackSize: 20);
    private static readonly ImmutableArray<ItemType> AllTypes = ImmutableArray.Create(Rock);

    private static GameState EmptyHandState()
    {
        var grid = Grid.Create(2, 1);
        return new GameState(new Bag(grid), new Cursor(new Position(0, 0)), AllTypes, GameState.CreateHandBag());
    }

    private static GameState HoldingState()
    {
        var grid = Grid.Create(2, 1);
        var handBag = new Bag(Grid.Create(1, 1));
        var (filledHand, _) = handBag.AcquireItems(new[] { new ItemStack(Rock, 5) });
        return new GameState(new Bag(grid), new Cursor(new Position(0, 0)), AllTypes, filledHand);
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
    public void EmptyHand_RendersBoxBorders()
    {
        SetupHandView(EmptyHandState());

        Assert.Equal('\u250c', _harness!.GetChar(0, 0)); // ┌
        Assert.Equal('\u2518', _harness.GetChar(9, 2));   // ┘
    }

    [Fact]
    public void EmptyHand_ContentIsBlank()
    {
        SetupHandView(EmptyHandState());

        var content = _harness!.GetText(1, 1, CellRenderer.ContentWidth);
        Assert.Equal(new string(' ', CellRenderer.ContentWidth), content);
    }

    [Fact]
    public void HoldingItem_ShowsItemText()
    {
        SetupHandView(HoldingState());

        var content = _harness!.GetText(1, 1, CellRenderer.ContentWidth);
        Assert.Contains("ROCK", content);
        Assert.Contains("5", content);
    }

    [Fact]
    public void HoldingItem_HasDifferentAttributeThanEmpty()
    {
        var view = SetupHandView(EmptyHandState());
        var emptyAttr = _harness!.GetAttribute(1, 1);

        view.UpdateState(HoldingState());
        _harness.Render();

        var holdingAttr = _harness.GetAttribute(1, 1);
        Assert.NotEqual(emptyAttr, holdingAttr);
    }
}
