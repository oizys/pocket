using System.Text.Json.Nodes;
using Pockets.Core.Fine;
using Pockets.Core.Models;
using Pockets.Core.Sim;

namespace Pockets.Realization.Tests;

/// <summary>
/// Slice 2.6 in Realization: <see cref="FineView.Of"/> backs <see cref="IFineView"/> with the session's
/// <see cref="FineState"/>, and the pose stream threads one view for the TUI driver, the debug handler and the replay,
/// so a stack's rest pose carries its L2a offset and a body's carries its sub-cell position.
/// </summary>
public class FineViewTests
{
    private static JsonNode Send(GameController c, string json) => JsonNode.Parse(PoseDebugCommandHandler.Handle(c, json))!;

    private static List<string> Poses(JsonNode resp) => resp["poses"]!.AsArray().Select(n => n!.GetValue<string>()).ToList();

    /// <summary>Every occupied cell index of B's active bag.</summary>
    private static List<int> Occupied(GameController c) =>
        c.Session.Current.ActiveBagAt(LocationId.B).Grid.Cells.Select((cell, i) => (cell, i))
            .Where(t => !t.cell.IsEmpty).Select(t => t.i).ToList();

    [Fact]
    public void Of_ReadsOffsetsAndBodies()
    {
        var bag = Guid.NewGuid();
        var fine = FineState.Empty
            .WithOffset(new CellRef(bag, 2), 3, -1)
            .WithBody(LocationId.B, bag, new FineBody(5, -7, 4, 0, Direction8.E));
        var view = FineView.Of(fine);

        Assert.Equal((3, -1), view.StackOffset(new CellRef(bag, 2)));
        Assert.Equal((0, 0), view.StackOffset(new CellRef(bag, 3)));
        Assert.Equal((5, -7), view.BodyOffset(LocationId.B, bag));
        Assert.Equal((0, 0), view.BodyOffset(LocationId.H, bag));
    }

    [Fact]
    public void Of_Empty_IsTheEmptyView()
    {
        Assert.Same(FineView.Empty, FineView.Of(FineState.Empty));
    }

    [Fact]
    public void Scene_StackRestPose_CarriesItsOffset()
    {
        var c = PoseStreamTests.DemoController();
        var index = Occupied(c)[0];
        Send(c, $$"""{"action":"setOffset","loc":"B","index":{{index}},"dx":4,"dy":-3}""");

        var scene = Scene.FromState(c.Session.Current, [LocationId.B], FineView.Of(c.Session.Fine));
        var at = new CellRef(c.Session.Current.ActiveBagAt(LocationId.B).Id, index);
        var pose = scene.Find(new EntityKey.Stack(at))!.Value;
        Assert.Equal((4, -3), (pose.SubX, pose.SubY));
    }

    [Fact]
    public void Handler_FineQuery_IsNotACommit()
    {
        var c = PoseStreamTests.DemoController();
        Send(c, """{"action":"key","key":"Primary"}""");
        var right = Poses(Send(c, """{"action":"key","key":"Right"}"""));
        Assert.NotEmpty(right);

        var fine = Send(c, """{"action":"fine"}""");
        Assert.Empty(Poses(fine));
        Assert.Empty(fine["fine"]!.AsArray());
        Assert.Equal(right, Poses(Send(c, """{"action":"poses"}""")));
    }

    /// <summary>
    /// Offsets on every stack in B, then a sort: the slides start from the offset rest poses (and the sort clears the
    /// offsets, so they end centred). Without offsets the same journey has no such line.
    /// </summary>
    [Fact]
    public void Handler_SortAfterOffsets_SlidesFromTheOffsetPoses()
    {
        var plain = SortRun(withOffsets: false).Lines;
        var offset = SortRun(withOffsets: true).Lines;

        Assert.DoesNotContain(plain, l => l.Contains("subX=5 subY=-4", StringComparison.Ordinal));
        Assert.Contains(offset, l => l.StartsWith("u=0/4 ", StringComparison.Ordinal) && l.Contains("subX=5 subY=-4", StringComparison.Ordinal));
        Assert.DoesNotContain(offset, l => l.StartsWith("u=4/4 ", StringComparison.Ordinal) && l.Contains("subX=5 subY=-4", StringComparison.Ordinal));
    }

    /// <summary>The pose replay rebuilds the fine state from the intent log, so the offset commits replay identically.</summary>
    [Fact]
    public void PoseReplay_RebuildsTheOffsets()
    {
        var (_, commits, c) = SortRun(withOffsets: true);
        Assert.Contains(commits, k => k.Lines.Any(l => l.Contains("subX=5 subY=-4", StringComparison.Ordinal)));
        Assert.Empty(PoseReplayCheck.Run(PoseStreamTests.DemoController().Session, c.Session.IntentLog, commits));
    }

    /// <summary>The TUI-style direct stream, given the session's fine view, matches the handler's lines.</summary>
    [Fact]
    public void DirectStream_WithTheSessionView_MatchesTheHandler()
    {
        var (lines, _, _) = SortRun(withOffsets: true);

        var c = PoseStreamTests.DemoController();
        var text = new EventText();
        var stream = new PoseStream(c.Session.Current, text.Names, null, FineView.Of(c.Session.Fine));
        var direct = new List<string>();
        void Commit(ControllerResult r)
        {
            _ = r.Events.Select(text.Line).ToList();
            direct.AddRange(stream.Commit(r.Events, c.Session.Current, 0, FineView.Of(c.Session.Fine)));
        }
        Commit(c.HandleKey(GameKey.Primary));
        foreach (var i in Occupied(c))
            Commit(c.SetFineOffset(LocationId.B, i, 5, -4));
        Commit(c.HandleKey(GameKey.Sort));

        Assert.Equal(lines, direct);
    }

    /// <summary>Dismiss the dialogue, optionally offset every B stack by (5, −4), then sort B, over the debug protocol.</summary>
    private static (List<string> Lines, List<PoseCommit> Commits, GameController Controller) SortRun(bool withOffsets)
    {
        var c = PoseStreamTests.DemoController();
        var lines = new List<string>();
        var commits = new List<PoseCommit>();
        void Do(string json)
        {
            var poses = Poses(Send(c, json));
            lines.AddRange(poses);
            commits.Add(new PoseCommit(commits.Count, c.Session.IntentLog.Count, 0, poses));
        }
        Do("""{"action":"key","key":"Primary"}""");
        if (withOffsets)
            foreach (var i in Occupied(c))
                Do($$"""{"action":"setOffset","loc":"B","index":{{i}},"dx":5,"dy":-4}""");
        Do("""{"action":"key","key":"Sort"}""");
        return (lines, commits, c);
    }
}
