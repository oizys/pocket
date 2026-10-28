using System.Collections.Immutable;
using System.Text.Json.Nodes;
using Pockets.Core;
using Pockets.Core.Data;
using Pockets.Core.Models;
using Pockets.Core.Rendering;
using Pockets.Core.Sim;

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

    // ==================== events (Slice 1.6) ====================

    /// <summary>
    /// Root (0,0) = a Chest (enter-only when <paramref name="enterOnly"/>), (0,1) empty; hand + toolbar.
    /// The cursor starts on the chest.
    /// </summary>
    private static GameController ChestController(bool enterOnly = false)
    {
        var rock = new ItemType("Rock", Category.Material, IsStackable: true, MaxStackSize: 20);
        var chestType = new ItemType("Chest", Category.Bag, IsStackable: false);
        var chest = new Bag(Grid.Create(4, 2), "Chest") { EnterOnly = enterOnly }.AcquireItems(new[] { new ItemStack(rock, 3) }).UpdatedBag;
        var root = new Bag(Grid.Create(4, 2).SetCell(0, new Cell(new ItemStack(chestType, 1, ContainedBagId: chest.Id))));
        var hand = GameState.CreateHandBag();
        var toolbar = new Bag(Grid.Create(4, 1), "Toolbar");
        var store = BagStore.Empty.Add(root).Add(hand).Add(toolbar).Add(chest);
        var locations = LocationMap.Create(hand.Id, root.Id).Set(LocationId.T, Location.AtOrigin(toolbar.Id));
        return new GameController(GameSession.New(new GameState(store, locations, ImmutableArray.Create(rock, chestType))));
    }

    private static string[] Events(JsonNode resp) =>
        resp["events"]!.AsArray().Select(e => e!.GetValue<string>()).ToArray();

    [Fact]
    public void MutatingResponse_CarriesTheStepsEvents_AsCanonicalLines()
    {
        var controller = NewController();
        var direct = NewController();
        var resp = JsonNode.Parse(DebugCommandHandler.Handle(controller, """{"action":"key","key":"Right"}"""))!;

        var expected = EventText.Lines(direct.HandleKey(GameKey.Right).Events);
        Assert.NotEmpty(expected);
        Assert.Equal(expected, Events(resp));
        Assert.Contains(Events(resp), l => l.Contains(" CursorMoved "));
        Assert.DoesNotContain(Events(resp), l => System.Text.RegularExpressions.Regex.IsMatch(l, "[0-9a-f]{8}-[0-9a-f]{4}"));
    }

    [Fact]
    public void StateQuery_HasAnEmptyEventsArray()
    {
        var resp = JsonNode.Parse(DebugCommandHandler.Handle(NewController(), """{"action":"state"}"""))!;
        Assert.Empty(Events(resp));
    }

    [Fact]
    public void BagNames_StayStableAcrossResponses_OneStreamPerController()
    {
        var controller = ChestController();
        var peek = JsonNode.Parse(DebugCommandHandler.Handle(controller, """{"action":"key","key":"Peek"}"""))!;
        Assert.Contains(Events(peek), l => l.Contains("PanelOpened loc=C bag=bag#1"));   // the chest is named first

        // The next response's first bag is the root: it continues the stream (bag#2), it does not restart.
        var click = JsonNode.Parse(DebugCommandHandler.Handle(controller, """{"action":"click","row":0,"col":1}"""))!;
        Assert.Contains(Events(click), l => l.Contains("CursorMoved loc=B bag=bag#2"));

        // A different controller is a different run: its naming starts again at bag#1.
        var other = ChestController();
        var otherClick = JsonNode.Parse(DebugCommandHandler.Handle(other, """{"action":"click","row":0,"col":1}"""))!;
        Assert.Contains(Events(otherClick), l => l.Contains("CursorMoved loc=B bag=bag#1"));
    }

    [Fact]
    public void RefusedPeek_ReachesTheResponse_AsABumpedLine()
    {
        var resp = JsonNode.Parse(DebugCommandHandler.Handle(ChestController(enterOnly: true), """{"action":"key","key":"Peek"}"""))!;
        var bump = Assert.Single(Events(resp), l => l.Contains(" Bumped "));
        Assert.Contains("reason=FailedPeek", bump);
        Assert.DoesNotContain(Events(resp), l => l.Contains(" PanelOpened "));
    }
}
