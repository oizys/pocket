using System.Collections.Immutable;
using Terminal.Gui;
using Pockets.Core.Models;
using Pockets.App.Views;
using Pockets.App.Rendering;

namespace Pockets.App.Tests.Views;

/// <summary>
/// End-to-end tests: init GameView in FakeDriver, send keys, read buffer, verify screen state.
/// These test the full rendering pipeline from GameState through GameView to pixel buffer.
/// FakeDriver buffer is 80×25.
/// </summary>
public class GamePlaythroughTests : IDisposable
{
    private TuiTestHarness? _harness;

    private static readonly ItemType Rock = new("Rock", Category.Material, IsStackable: true, MaxStackSize: 20);
    private static readonly ItemType Herb = new("Herb", Category.Medicine, IsStackable: true, MaxStackSize: 20);
    private static readonly ImmutableArray<ItemType> AllTypes = ImmutableArray.Create(Rock, Herb);

    /// <summary>
    /// Creates a GameState with a 4×2 grid, Rock×5 at (0,0), Herb×3 at (0,1), cursor at (0,0).
    /// </summary>
    private static GameState MakeTestState()
    {
        var cells = new Cell[8]; // 4 cols × 2 rows
        cells[0] = new Cell(new ItemStack(Rock, 5));
        cells[1] = new Cell(new ItemStack(Herb, 3));
        for (int i = 2; i < 8; i++)
            cells[i] = new Cell();

        var grid = new Grid(4, 2, cells.ToImmutableArray());
        var rootBag = new Bag(grid);
        var handBag = GameState.CreateHandBag();
        var store = BagStore.Empty.Add(rootBag).Add(handBag);
        return new GameState(store, LocationMap.Create(handBag.Id, rootBag.Id), AllTypes);
    }

    private GameView SetupGame(GameState state)
    {
        _harness = TuiTestHarness.Create();
        var gameView = new GameView(state, enableTickTimer: false)
        {
            X = 0, Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill()
        };
        _harness.AddView(gameView);
        _harness.Render();
        return gameView;
    }

    private void SendKey(GameView view, Key key)
    {
        view.ProcessKey(new KeyEvent(key, new KeyModifiers()));
        _harness!.Render();
    }

    public void Dispose()
    {
        _harness?.Dispose();
    }

    [Fact]
    public void InitialRender_ShowsItemsInGrid()
    {
        var state = MakeTestState();
        SetupGame(state);

        var dump = _harness!.DumpBuffer();
        Assert.Contains("ROCK", dump);
        Assert.Contains("HERB", dump);
    }

    [Fact]
    public void InitialRender_ShowsWindowTitle()
    {
        var state = MakeTestState();
        SetupGame(state);

        var dump = _harness!.DumpBuffer();
        Assert.Contains("Pockets", dump);
    }

    [Fact]
    public void ArrowKey_MovesCursor_ChangesRender()
    {
        var state = MakeTestState();
        var gameView = SetupGame(state);

        var rockPos = _harness!.FindText("ROCK");
        Assert.NotNull(rockPos);
        var initialAttr = _harness.GetAttribute(rockPos.Value.X, rockPos.Value.Y);

        // Move cursor right — Rock should no longer be under cursor
        SendKey(gameView, Key.CursorRight);

        var newAttr = _harness.GetAttribute(rockPos.Value.X, rockPos.Value.Y);
        Assert.NotEqual(initialAttr, newAttr);
    }

    [Fact]
    public void GrabItem_RemovesFromGrid()
    {
        var state = MakeTestState();
        var gameView = SetupGame(state);

        var rockPos = _harness!.FindText("ROCK");
        Assert.NotNull(rockPos);

        // Primary action = grab
        SendKey(gameView, (Key)'1');

        // Rock should be gone from grid at that position
        var charAtOldPos = _harness.GetText(rockPos.Value.X, rockPos.Value.Y, 4);
        Assert.NotEqual("ROCK", charAtOldPos);
    }

    [Fact]
    public void GrabMoveAndDrop_RelocatesItem()
    {
        var state = MakeTestState();
        var gameView = SetupGame(state);

        // Grab Rock at (0,0)
        SendKey(gameView, (Key)'1');

        // Move right twice to cell (0,2) which is empty
        SendKey(gameView, Key.CursorRight);
        SendKey(gameView, Key.CursorRight);

        // Drop Rock
        SendKey(gameView, (Key)'1');

        // Rock should appear somewhere in the buffer still
        var dump = _harness!.DumpBuffer();
        Assert.Contains("ROCK", dump);
    }

    [Fact]
    public void Sort_KeepsAllItems()
    {
        var state = MakeTestState();
        var gameView = SetupGame(state);

        SendKey(gameView, (Key)'4');

        var dump = _harness!.DumpBuffer();
        Assert.Contains("ROCK", dump);
        Assert.Contains("HERB", dump);
    }

    [Fact]
    public void Undo_RestoresGridAfterGrab()
    {
        var state = MakeTestState();
        var gameView = SetupGame(state);

        var rockPos = _harness!.FindText("ROCK");
        Assert.NotNull(rockPos);

        // Grab Rock
        SendKey(gameView, (Key)'1');

        var charAfterGrab = _harness.GetText(rockPos.Value.X, rockPos.Value.Y, 4);
        Assert.NotEqual("ROCK", charAfterGrab);

        // Undo
        SendKey(gameView, Key.Z | Key.CtrlMask);

        var charAfterUndo = _harness.GetText(rockPos.Value.X, rockPos.Value.Y, 4);
        Assert.Equal("ROCK", charAfterUndo);
    }

    [Fact]
    public void WindowAndPanelTitles_AreRendered()
    {
        var state = MakeTestState();
        SetupGame(state);

        var dump = _harness!.DumpBuffer();
        // Window title
        Assert.Contains("Pockets", dump);
        // Inventory panel frame title
        Assert.Contains("Inventory", dump);
    }

    [Fact]
    public void EnteringNestedBagOfDifferentHeight_RepositionsDescriptionView()
    {
        // Regression test for Fix 1 (RootBag → ActiveBag in GridPanel layout).
        // Root 4×2 with a bag-item in cell 0; nested 1×4 (different row count).
        // Before the fix, the description view's Y was computed from RootBag rows
        // and never updated, so it overlapped/gapped against the active grid.
        // After the fix, the description repositions for ActiveBag rows on each
        // UpdateState call.
        var bagItem = new ItemType("Pouch", Category.Material, IsStackable: false, MaxStackSize: 1);
        var innerCells = Enumerable.Repeat(new Cell(), 4).ToImmutableArray();
        var innerGrid = new Grid(1, 4, innerCells); // 1 col × 4 rows
        var innerBag = new Bag(innerGrid);

        var rootCells = new Cell[8];
        rootCells[0] = new Cell(new ItemStack(bagItem, 1, ContainedBagId: innerBag.Id));
        for (int i = 1; i < 8; i++)
            rootCells[i] = new Cell();
        var rootGrid = new Grid(4, 2, rootCells.ToImmutableArray()); // 4 cols × 2 rows
        var rootBag = new Bag(rootGrid);

        var handBag = GameState.CreateHandBag();
        var store = BagStore.Empty.Add(rootBag).Add(handBag).Add(innerBag);
        var types = ImmutableArray.Create(bagItem);
        var state = new GameState(store, LocationMap.Create(handBag.Id, rootBag.Id), types);

        var gameView = SetupGame(state);

        var descView = FindFirstSubview<ItemDescriptionView>(gameView);
        Assert.NotNull(descView);

        // Root active: 2 rows → description Y = 1 + 2*CellHeight
        Assert.Equal(1 + 2 * CellRenderer.CellHeight, descView!.Frame.Y);

        // Enter nested bag (E key)
        SendKey(gameView, (Key)'e');

        // Nested active: 4 rows → description Y = 1 + 4*CellHeight
        Assert.Equal(1 + 4 * CellRenderer.CellHeight, descView.Frame.Y);
    }

    private static T? FindFirstSubview<T>(View root) where T : View
    {
        foreach (var sub in root.Subviews)
        {
            if (sub is T match)
                return match;
            var deeper = FindFirstSubview<T>(sub);
            if (deeper is not null)
                return deeper;
        }
        return null;
    }
}
