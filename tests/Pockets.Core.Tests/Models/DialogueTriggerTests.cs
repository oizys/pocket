using Pockets.Core;
using Pockets.Core.Data;
using Pockets.Core.Models;

namespace Pockets.Core.Tests.Models;

/// <summary>
/// End-to-end dialogue triggers over the real demo profile (seeded layout + the /data opening
/// beats): frame-0 dialogue-only, dismiss → grid materializes, first cursor-rest → description
/// pane, 3rd-unique-inspect → amnesia beat fires exactly once (rapid scans cannot double-fire),
/// and dialogue input routing (Primary advances, other keys don't leak to the cursor).
/// </summary>
public class DialogueTriggerTests
{
    private static GameController NewController()
    {
        var registry = ContentLoader.LoadFromDirectory(TestPaths.DataDir);
        var book = DialogueLoader.LoadFromDirectory(TestPaths.DataDir);
        var profile = GameInitializer.CreateDemoProfile(registry, seed: null, dialogue: book);
        return new GameController(profile.NewSession());
    }

    [Fact]
    public void FrameZero_IsDialogueBoxOnly_WithOpeningBeatActive()
    {
        var c = NewController();
        var state = c.Session.Current;

        Assert.True(state.Ui.Has(ChromeElement.DialogueBox));
        Assert.False(state.Ui.Has(ChromeElement.Grid));
        Assert.False(state.Ui.Has(ChromeElement.DescriptionPane));
        Assert.Equal("opening", state.Dialogue.ActiveBeatId);
        Assert.Equal(0, state.Dialogue.LineIndex);
    }

    [Fact]
    public void DismissingOpening_MaterializesGrid()
    {
        var c = NewController();
        Assert.False(c.Session.Current.Ui.Has(ChromeElement.Grid));

        c.HandleKey(GameKey.Primary); // single-line opening → dismiss

        Assert.False(c.Session.Current.Dialogue.IsActive);
        Assert.True(c.Session.Current.Ui.Has(ChromeElement.Grid));
    }

    [Fact]
    public void KeysDuringOpening_DoNotLeakToCursor()
    {
        var c = NewController();
        var cursorBefore = c.Session.Current.Cursor.Position;

        c.HandleKey(GameKey.Right);
        c.HandleKey(GameKey.Down);

        Assert.True(c.Session.Current.Dialogue.IsActive); // still showing — not dismissed
        Assert.Equal(cursorBefore, c.Session.Current.Cursor.Position); // no cursor leak
        Assert.False(c.Session.Current.Ui.Has(ChromeElement.Grid)); // world still not materialized
    }

    [Fact]
    public void FirstCursorRestOnItem_MaterializesDescriptionPane()
    {
        var c = NewController();
        c.HandleKey(GameKey.Primary); // dismiss opening → grid on, cursor on Workshop (0,0)
        Assert.False(c.Session.Current.Ui.Has(ChromeElement.DescriptionPane));

        c.HandleKey(GameKey.Right); // rest on the item at (0,1)

        Assert.True(c.Session.Current.Ui.Has(ChromeElement.DescriptionPane));
    }

    [Fact]
    public void ThirdUniqueInspect_FiresAmnesiaBeat_Once()
    {
        var c = NewController();
        c.HandleKey(GameKey.Primary); // dismiss opening (cursor at 0,0 = unique #0, initial rest not counted)

        c.HandleKey(GameKey.Right);   // (0,1) unique #1
        Assert.False(c.Session.Current.Dialogue.IsActive);
        c.HandleKey(GameKey.Right);   // (0,2) unique #2
        Assert.False(c.Session.Current.Dialogue.IsActive);
        c.HandleKey(GameKey.Right);   // (0,3) unique #3 → amnesia fires

        Assert.Equal("amnesia", c.Session.Current.Dialogue.ActiveBeatId);
        Assert.Equal(3, c.Session.Current.Dialogue.UniqueInspectCount);
    }

    [Fact]
    public void RapidScan_CannotDoubleFire_TheAmnesiaBeat()
    {
        var c = NewController();
        c.HandleKey(GameKey.Primary);                 // dismiss opening
        c.HandleKey(GameKey.Right);                    // #1
        c.HandleKey(GameKey.Right);                    // #2
        c.HandleKey(GameKey.Right);                    // #3 → amnesia
        c.HandleKey(GameKey.Primary);                  // dismiss amnesia

        // Scan back and forth over already-seen items, then onto a brand-new unique item.
        c.HandleKey(GameKey.Left);                     // (0,2) already seen
        c.HandleKey(GameKey.Left);                     // (0,1) already seen
        c.HandleKey(GameKey.Right);                    // (0,2) again
        c.HandleKey(GameKey.Right);                    // (0,3) again
        c.HandleKey(GameKey.Right);                    // (0,4) unique #4 — beyond threshold, already fired

        Assert.False(c.Session.Current.Dialogue.IsActive); // amnesia never re-fires
        Assert.True(c.Session.Current.Dialogue.HasFired("amnesia"));
    }

    [Fact]
    public void DialogueProgression_SurvivesUndo()
    {
        var c = NewController();
        c.HandleKey(GameKey.Primary); // dismiss opening → grid on
        c.HandleKey(GameKey.Right);   // #1 (also materializes description pane)
        c.HandleKey(GameKey.Right);   // #2
        // Grab an item to create an undoable frame.
        c.HandleKey(GameKey.Primary);
        var firedBefore = c.Session.Current.Dialogue.InspectedItems.Count;

        c.HandleKey(GameKey.Undo);

        // The grab is undone but the inspection progress is carried forward (never rewinds).
        Assert.Equal(firedBefore, c.Session.Current.Dialogue.InspectedItems.Count);
    }
}
