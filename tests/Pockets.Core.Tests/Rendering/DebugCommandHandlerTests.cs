using System.Text.Json.Nodes;
using Pockets.Core;
using Pockets.Core.Data;
using Pockets.Core.Models;
using Pockets.Core.Rendering;

namespace Pockets.Core.Tests.Rendering;

/// <summary>
/// The debug command handler is the shared server-side dispatch for the agent protocol.
/// Both the real Godot WebSocket server and the runner's mock transport route through it,
/// so these tests pin its contract (key/click/back/tick/state, error handling).
/// </summary>
public class DebugCommandHandlerTests
{
    private static GameController NewController() =>
        new(GameInitializer.CreateDemoProfile(ContentLoader.LoadFromDirectory(TestPaths.DataDir)).NewSession());

    [Fact]
    public void KeyAction_MovesCursor()
    {
        var controller = NewController();
        var resp = JsonNode.Parse(DebugCommandHandler.Handle(controller, """{"action":"key","key":"Right"}""", out var mutated))!;

        Assert.True(mutated);
        Assert.True(resp["handled"]!.GetValue<bool>());
        Assert.Equal(1, resp["state"]!["cursor"]!["col"]!.GetValue<int>());
    }

    [Fact]
    public void StateAction_DoesNotMutate()
    {
        var controller = NewController();
        var resp = JsonNode.Parse(DebugCommandHandler.Handle(controller, """{"action":"state"}""", out var mutated))!;

        Assert.False(mutated);
        Assert.Equal(0, resp["state"]!["cursor"]!["col"]!.GetValue<int>());
        Assert.Equal(0, resp["state"]!["cursor"]!["row"]!.GetValue<int>());
    }

    [Fact]
    public void UnknownAction_ReturnsError()
    {
        var controller = NewController();
        var resp = JsonNode.Parse(DebugCommandHandler.Handle(controller, """{"action":"frobnicate"}"""))!;
        Assert.NotNull(resp["error"]);
    }

    [Fact]
    public void UnknownKey_ReturnsError()
    {
        var controller = NewController();
        var resp = JsonNode.Parse(DebugCommandHandler.Handle(controller, """{"action":"key","key":"Xyzzy"}"""))!;
        Assert.NotNull(resp["error"]);
    }

    [Fact]
    public void MalformedJson_ReturnsError()
    {
        var controller = NewController();
        var resp = JsonNode.Parse(DebugCommandHandler.Handle(controller, "not json at all"))!;
        Assert.NotNull(resp["error"]);
    }

    [Fact]
    public void HandlerAndSerializer_ProduceSameStatePayload()
    {
        // The handler's "state" payload must equal a direct serialize of the same session —
        // this is what makes the mock transport a faithful stand-in for the real server.
        var controller = NewController();
        var resp = JsonNode.Parse(DebugCommandHandler.Handle(controller, """{"action":"state"}"""))!;
        var direct = ViewModelSerializer.Serialize(controller.Session);

        Assert.Equal(direct.ToJsonString(), resp["state"]!.ToJsonString());
    }
}
