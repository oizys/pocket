using System.Text.Json.Nodes;
using Pockets.Core.Models;
using Pockets.Core.Rendering;

namespace Pockets.Realization.Tests;

/// <summary>
/// Slice 2.4 (principle 4): the debug protocol with poses. <see cref="PoseDebugCommandHandler"/> is the
/// <see cref="DebugCommandHandler"/> dispatch plus one <see cref="PoseStream"/> per controller: every success response
/// gains <c>"poses"</c>, and a <c>poses</c> query returns the last commit's lines.
/// </summary>
public class PoseDebugCommandHandlerTests
{
    private static JsonNode Send(GameController c, string json) => JsonNode.Parse(PoseDebugCommandHandler.Handle(c, json))!;

    private static List<string> Poses(JsonNode resp) => resp["poses"]!.AsArray().Select(n => n!.GetValue<string>()).ToList();

    /// <summary>A mutating response carries the VM, the events and the commit's pose lines.</summary>
    [Fact]
    public void Key_Response_CarriesEventsAndPoses()
    {
        var c = PoseStreamTests.DemoController();
        Send(c, """{"action":"key","key":"Primary"}""");
        var resp = Send(c, """{"action":"key","key":"Right"}""");

        Assert.True(resp["handled"]!.GetValue<bool>());
        Assert.NotNull(resp["state"]);
        Assert.NotEmpty(resp["events"]!.AsArray());
        var poses = Poses(resp);
        Assert.NotEmpty(poses);
        Assert.StartsWith("u=0/4 t=0 entity=", poses[0]);
        Assert.Contains(poses, l => l.StartsWith("u=2/4 ", StringComparison.Ordinal));
    }

    /// <summary>The VM and events of a response are exactly the Core handler's (the poses half adds a field, nothing else).</summary>
    [Fact]
    public void Response_MatchesCoreHandler_ApartFromPoses()
    {
        var a = PoseStreamTests.DemoController();
        var b = PoseStreamTests.DemoController();
        foreach (var cmd in new[] { """{"action":"key","key":"Primary"}""", """{"action":"key","key":"Right"}""", """{"action":"tick"}""", """{"action":"state"}""" })
        {
            var withPoses = (JsonObject)Send(a, cmd);
            var core = (JsonObject)JsonNode.Parse(DebugCommandHandler.Handle(b, cmd))!;
            Assert.True(withPoses.Remove("poses"));
            Assert.Equal(core.ToJsonString(), withPoses.ToJsonString());
        }
    }

    /// <summary>A state query is not a commit: its poses are empty, and the next <c>poses</c> query still returns the last commit's lines.</summary>
    [Fact]
    public void State_IsNotACommit_PosesQueryReturnsLastCommit()
    {
        var c = PoseStreamTests.DemoController();
        Assert.Empty(Poses(Send(c, """{"action":"poses"}""")));

        Send(c, """{"action":"key","key":"Primary"}""");
        var right = Poses(Send(c, """{"action":"key","key":"Right"}"""));
        var state = Send(c, """{"action":"state"}""");
        Assert.Empty(Poses(state));

        var query = Send(c, """{"action":"poses"}""");
        Assert.Equal(right, Poses(query));
        Assert.NotNull(query["state"]);
    }

    /// <summary>Errors pass through without a poses field.</summary>
    [Fact]
    public void Error_PassesThrough()
    {
        var c = PoseStreamTests.DemoController();
        var resp = Send(c, """{"action":"nope"}""");
        Assert.NotNull(resp["error"]);
        Assert.Null(resp["poses"]);
        Assert.NotNull(JsonNode.Parse(PoseDebugCommandHandler.Handle(c, "{not json"))!["error"]);
    }

    /// <summary>The commit's <c>atMs</c> is the clock delta: advanceTime's ms, one tick's length, else 0. Never wall-clock.</summary>
    [Fact]
    public void AtMs_IsTheClockDelta()
    {
        Assert.Equal(5000, PoseDebugCommandHandler.AtMs(JsonNode.Parse("""{"action":"advanceTime","ms":5000}""")!));
        Assert.Equal(GameController.TickLengthMs, PoseDebugCommandHandler.AtMs(JsonNode.Parse("""{"action":"tick"}""")!));
        Assert.Equal(0, PoseDebugCommandHandler.AtMs(JsonNode.Parse("""{"action":"key","key":"Right"}""")!));
        Assert.Equal(0, PoseDebugCommandHandler.AtMs(JsonNode.Parse("""{"action":"click","row":0,"col":1}""")!));
    }

    /// <summary>Pose bag names continue the controller's event naming (one stream per controller, events written first).</summary>
    [Fact]
    public void Poses_NameBagsLikeTheEvents()
    {
        var c = PoseStreamTests.DemoController();
        Send(c, """{"action":"key","key":"Primary"}""");
        var resp = Send(c, """{"action":"key","key":"Right"}""");
        var moved = resp["events"]!.AsArray().Select(n => n!.GetValue<string>()).Last(l => l.Contains("CursorMoved"));
        var bag = moved.Split(' ').First(f => f.StartsWith("bag=", StringComparison.Ordinal));
        Assert.All(Poses(resp), l => Assert.Contains(bag, l));
    }
}
