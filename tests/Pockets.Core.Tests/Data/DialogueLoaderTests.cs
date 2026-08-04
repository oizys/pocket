using Pockets.Core.Data;
using Pockets.Core.Models;

namespace Pockets.Core.Tests.Data;

/// <summary>
/// Dialogue beats are data-driven (markdown, same posture as item cards). These pin the loader:
/// trigger parsing, per-line portrait tags, the optional on-dismiss chrome reveal, and that the
/// repo's seeded demo-opening file (0:00 + 1:30 beats) loads with the exact journey-doc text.
/// </summary>
public class DialogueLoaderTests
{
    [Fact]
    public void LoadFromMarkdown_ParsesTrigger_Reveals_AndPortraitTaggedLines()
    {
        const string md = """
            # Dialogue: opening
            Trigger: GameStart
            Reveals: Grid

            - (groggy) …cold. Was I reaching for something?
            """;

        var book = DialogueLoader.LoadFromMarkdown(md);
        var beat = book.Get("opening");

        Assert.NotNull(beat);
        Assert.Equal(DialogueTriggerKind.GameStart, beat!.Trigger.Kind);
        Assert.Equal(ChromeElement.Grid, beat.Reveals);
        Assert.Single(beat.Lines);
        Assert.Equal("…cold. Was I reaching for something?", beat.Lines[0].Text);
        Assert.Equal("groggy", beat.Lines[0].Portrait);
    }

    [Fact]
    public void LoadFromMarkdown_ParsesCountedTrigger_AndNoReveals()
    {
        const string md = """
            # Dialogue: amnesia
            Trigger: NthUniqueInspect 3

            - (puzzled) I know what these are. Why don't I know where I am?
            """;

        var beat = DialogueLoader.LoadFromMarkdown(md).Get("amnesia")!;

        Assert.Equal(DialogueTriggerKind.NthUniqueInspect, beat.Trigger.Kind);
        Assert.Equal(3, beat.Trigger.Threshold);
        Assert.Null(beat.Reveals);
    }

    [Fact]
    public void LoadFromDirectory_LoadsTheSeededDemoOpeningBeats_WithJourneyText()
    {
        var book = DialogueLoader.LoadFromDirectory(TestPaths.DataDir);

        var opening = book.Get("opening");
        var amnesia = book.Get("amnesia");

        Assert.NotNull(opening);
        Assert.NotNull(amnesia);
        Assert.Equal("…cold. Was I reaching for something?", opening!.Lines[0].Text);
        Assert.Equal(ChromeElement.Grid, opening.Reveals);
        Assert.Equal("I know what these are. Why don't I know where I am?", amnesia!.Lines[0].Text);
        Assert.Equal(new DialogueTrigger(DialogueTriggerKind.NthUniqueInspect, 3), amnesia.Trigger);
    }

    [Fact]
    public void MissingTrigger_Throws()
    {
        const string md = """
            # Dialogue: broken

            - (flat) no trigger here
            """;

        Assert.Throws<FormatException>(() => DialogueLoader.LoadFromMarkdown(md));
    }
}
