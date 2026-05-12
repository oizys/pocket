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
        // 3x2 glyph cells: Rock x5 -> "R 5", Herb x3 -> "H 3"
        Assert.Contains("R 5", dump);
        Assert.Contains("H 3", dump);
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

        var rockPos = _harness!.FindText("R 5");
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

        var rockPos = _harness!.FindText("R 5");
        Assert.NotNull(rockPos);

        // Primary action = grab
        SendKey(gameView, (Key)'1');

        // Rock glyph + count should be gone from the original grid cell.
        var charAtOldPos = _harness.GetText(rockPos.Value.X, rockPos.Value.Y, 3);
        Assert.NotEqual("R 5", charAtOldPos);
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
        Assert.Contains("R 5", dump);
    }

    [Fact]
    public void Sort_KeepsAllItems()
    {
        var state = MakeTestState();
        var gameView = SetupGame(state);

        SendKey(gameView, (Key)'4');

        var dump = _harness!.DumpBuffer();
        Assert.Contains("R 5", dump);
        Assert.Contains("H 3", dump);
    }

    [Fact]
    public void Undo_RestoresGridAfterGrab()
    {
        var state = MakeTestState();
        var gameView = SetupGame(state);

        var rockPos = _harness!.FindText("R 5");
        Assert.NotNull(rockPos);

        // Grab Rock
        SendKey(gameView, (Key)'1');

        var charAfterGrab = _harness.GetText(rockPos.Value.X, rockPos.Value.Y, 3);
        Assert.NotEqual("R 5", charAfterGrab);

        // Undo
        SendKey(gameView, Key.Z | Key.CtrlMask);

        var charAfterUndo = _harness.GetText(rockPos.Value.X, rockPos.Value.Y, 3);
        Assert.Equal("R 5", charAfterUndo);
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
    public void InlineSplit_HashEntersMode_ArrowAdjusts_EnterCommits()
    {
        // Regression test for Stage 2: the inline split mode replaces
        // ShowModalSplitDialog. Pressing # while cursor sits on a stack
        // of 5 enters split mode (GrabCount=2 by default for 5/2). The
        // command strip shows the editor. ← drops GrabCount, Enter commits
        // the split into hand.
        var state = MakeTestState();  // Rock x5 at (0,0)
        var gameView = SetupGame(state);

        // Enter split mode via # (Shift-3)
        SendKey(gameView, (Key)'#');

        var dumpInMode = _harness!.DumpBuffer();
        Assert.Contains("Split:", dumpInMode);
        Assert.Contains("grab 2", dumpInMode);  // 5 / 2 = 2

        // ← drops GrabCount to 1, leftCount becomes 4
        SendKey(gameView, Key.CursorLeft);

        var dumpAfterAdjust = _harness.DumpBuffer();
        Assert.Contains("grab 1", dumpAfterAdjust);
        Assert.Contains("leave 4", dumpAfterAdjust);

        // Enter commits the split
        SendKey(gameView, Key.Enter);

        // The inline editor (which uses "←/→ adjust") must be gone. Note the
        // action log will show a "Split: 5 Rock → 4/1" entry, so we look for
        // an editor-specific token, not bare "Split:".
        var dumpAfterCommit = _harness.DumpBuffer();
        Assert.DoesNotContain("←/→ adjust", dumpAfterCommit);

        // 4 Rocks stay at (0,0), 1 Rock now in hand
        var controllerField = gameView.GetType()
            .GetField("_controller",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var ctrl = (GameController)controllerField!.GetValue(gameView)!;
        Assert.Equal(4, ctrl.Session.Current.RootBag.Grid.GetCell(0).Stack!.Count);
        Assert.Equal(1, ctrl.Session.Current.HandItems[0].Count);
    }

    [Fact]
    public void DescriptionPane_FollowsFocusAcrossPanels()
    {
        // Stage 3 invariant: the focused-description pane reflects the
        // *focused* panel's cursor cell, not always B. Open a facility bag
        // as C, switch focus to C, and the description should show that
        // bag's first cell (a recipe slot label) — not B's cursor.
        // Concretely: with focus=B we expect a B-cell description; with
        // focus=C we expect a C-cell description that differs.
        var state = MakeTestState();  // Root with Rock×5 at (0,0)
        var gameView = SetupGame(state);

        var descView = FindFirstSubview<ItemDescriptionView>(gameView)!;
        var label = FindFirstSubview<Label>(descView)!;
        Assert.NotNull(label);

        var bText = label.Text.ToString() ?? "";
        Assert.Contains("Rock", bText); // B has Rock at cursor

        // Add an empty C panel pointing at the hand bag (any open bag works
        // for this test — we just need C's cursor cell to differ from B's).
        var ctrl = (GameController)gameView.GetType()
            .GetField("_controller",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(gameView)!;

        // Create a small empty bag for C, register it in the store, and set
        // a C Location pointing at its empty (0,0) cell.
        var cBag = new Bag(Grid.Create(2, 2));
        var stateWithC = ctrl.Session.Current with
        {
            Store = ctrl.Session.Current.Store.Add(cBag),
            Locations = ctrl.Session.Current.Locations.Set(LocationId.C, Location.AtOrigin(cBag.Id))
        };
        ctrl.SetSession(ctrl.Session with { Current = stateWithC });
        ctrl.SetFocus(LocationId.C);

        // Trigger UpdateUI by sending a no-op key (cursor right wraps in 2x2).
        SendKey(gameView, Key.CursorRight);

        var cText = label.Text.ToString() ?? "";
        // C's cursor cell is empty — description must NOT contain Rock
        Assert.DoesNotContain("Rock", cText);
        Assert.NotEqual(bText, cText);
    }

    [Fact]
    public void DescriptionPaneY_IsStableAcrossActiveBagSizeChanges()
    {
        // Stage 3 invariant: the focused-description pane is a standalone view
        // at GameView level positioned with Pos.AnchorEnd. Its Y must NOT
        // change when the active bag's grid size changes (e.g. entering a
        // nested bag of different rows). Stage 1's Fix 1 test used to assert
        // the *opposite* — that the Y moved with ActiveBag.Rows — because the
        // description was a child of GridPanel positioned right below the
        // grid cells. Stage 3 fixes that coupling permanently.
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
        var rootY = descView!.Frame.Y;

        // Enter the nested bag — active grid changes from 4x2 to 1x4
        SendKey(gameView, (Key)'e');

        // Pane Y must be unchanged. Old (stage 1) Fix 1 behavior would have
        // changed it; stage 3 decouples the pane from the active grid size.
        Assert.Equal(rootY, descView.Frame.Y);
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
