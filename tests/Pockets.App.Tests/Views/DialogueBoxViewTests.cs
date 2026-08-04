using System.Collections.Immutable;
using Terminal.Gui;
using Pockets.Core.Models;
using Pockets.App.Views;

namespace Pockets.App.Tests.Views;

/// <summary>
/// TUI render tests for the dialogue box (Slice 2): the real GameView under FakeDriver draws the
/// active beat's line, hides the grid at frame 0 (dialogue-only), and drops the box after Primary
/// dismisses the beat (revealing the grid). Drives the actual ProcessKey input path.
/// (Serialized against other TUI tests by TuiTestHarness's static lock, as elsewhere.)
/// </summary>
public class DialogueBoxViewTests : IDisposable
{
    private TuiTestHarness? _harness;

    private static readonly ItemType Rock = new("Plain Rock", Category.Material, IsStackable: true);
    private static readonly ItemType Herb = new("Dry Herb", Category.Medicine, IsStackable: true);

    /// <summary>An 8×4 demo-like state at frame 0: dialogue-box-only ledger + the opening beat active.</summary>
    private static (GameState State, DialogueBook Book) FrameZero()
    {
        var book = DialogueLoader_Inline();
        var state = GameState.CreateStage1(
            ImmutableArray.Create(Rock, Herb),
            new[] { new ItemStack(Rock, 3), new ItemStack(Herb, 2) })
            with
            {
                Ui = UiLedger.DemoFrameZero,
                Dialogue = DialogueState.Empty.Enqueue("opening")
            };
        return (state, book);
    }

    private static DialogueBook DialogueLoader_Inline() =>
        Pockets.Core.Data.DialogueLoader.LoadFromMarkdown("""
            # Dialogue: opening
            Trigger: GameStart
            Reveals: Grid

            - (groggy) A test line about reaching.
            """);

    private GameView Setup(GameState state, DialogueBook book)
    {
        _harness = TuiTestHarness.Create();
        var view = new GameView(state, enableTickTimer: false, dialogue: book)
        {
            X = 0, Y = 0, Width = Dim.Fill(), Height = Dim.Fill()
        };
        _harness.AddView(view);
        _harness.Render();
        return view;
    }

    [Fact]
    public void FrameZero_RendersDialogueLine_AndHidesGrid()
    {
        var (state, book) = FrameZero();
        Setup(state, book);

        Assert.NotNull(_harness!.FindText("A test line about reaching."));
        // Grid is ledger-off at frame 0 → the inventory grid (its "Inventory" frame) is not drawn.
        Assert.Null(_harness.FindText("Inventory"));
    }

    [Fact]
    public void PrimaryDismissesBeat_DropsBox_AndRevealsGrid()
    {
        var (state, book) = FrameZero();
        var view = Setup(state, book);

        view.ProcessKey(new KeyEvent((Key)'1', new KeyModifiers())); // Primary → dismiss opening
        _harness!.Render();

        Assert.Null(_harness.FindText("A test line about reaching.")); // box dropped
        Assert.True(view.Controller.Session.Current.Ui.Has(ChromeElement.Grid)); // grid materialized
    }

    public void Dispose() => _harness?.Dispose();
}
