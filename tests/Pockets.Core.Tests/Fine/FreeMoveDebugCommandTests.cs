using System.Text.Json.Nodes;
using Pockets.Core.Data;
using Pockets.Core.Fine;
using Pockets.Core.Models;
using Pockets.Core.Rendering;
using Pockets.Core.Sim;

namespace Pockets.Core.Tests.Fine;

/// <summary>
/// Slice 2.7, debug protocol: <c>setMovement {loc, model}</c> (P2-D12) and <c>steer {loc?, dir}</c> (P2-D8; <c>dir</c>
/// absent, null or <c>"none"</c> releases). Both go through the controller as their shell-handled intents. The demo
/// profile walks Quiet 1 as B, so <c>setMovement B Free</c> after entering it makes the body free.
/// </summary>
public class FreeMoveDebugCommandTests
{
    private static GameController NewController() =>
        new(GameInitializer.CreateDemoProfile(ContentLoader.LoadFromDirectory(TestPaths.DataDir)).NewSession());

    private static JsonNode Send(GameController c, string json, out bool mutated) =>
        JsonNode.Parse(DebugCommandHandler.Handle(c, json, out mutated))!;

    private static JsonNode Send(GameController c, string json) => Send(c, json, out _);

    [Fact]
    public void SetMovement_SetsTheFlag_AndIsLogged()
    {
        var c = NewController();
        var resp = Send(c, """{"action":"setMovement","loc":"B","model":"Free"}""", out var mutated);

        Assert.True(mutated);
        Assert.True(resp["handled"]!.GetValue<bool>());
        Assert.Empty(resp["events"]!.AsArray());
        Assert.Equal(MovementModel.Free, c.Session.Current.ActiveBagAt(LocationId.B).Movement);
        Assert.Equal(new Intent.SetMovement(LocationId.B, MovementModel.Free), c.Session.IntentLog[^1].Intent);
    }

    [Theory]
    [InlineData("""{"action":"setMovement","loc":"B"}""")]
    [InlineData("""{"action":"setMovement","loc":"B","model":"Wobbly"}""")]
    [InlineData("""{"action":"setMovement","loc":"Z","model":"Free"}""")]
    [InlineData("""{"action":"steer","loc":"B","dir":"Up"}""")]
    [InlineData("""{"action":"steer","loc":"Q","dir":"E"}""")]
    public void BadFields_AreErrors_AndDoNotMutate(string json)
    {
        var c = NewController();
        var before = c.Session;
        var resp = Send(c, json, out var mutated);

        Assert.False(mutated);
        Assert.NotNull(resp["error"]);
        Assert.Same(before, c.Session);
    }

    [Fact]
    public void Steer_SetsTheHeldDirection_InAFreeWorldBag()
    {
        var c = NewController();
        // The demo (realtime) starts in the home room: make B's bag a free world bag directly.
        var bag = c.Session.Current.ActiveBagAt(LocationId.B);
        c.SetSession(c.Session with
        {
            Current = c.Session.Current with { Store = c.Session.Current.Store.Set(bag.Id, bag with { EnterOnly = true, Movement = MovementModel.Free }) }
        });
        Assert.True(c.Session.IsFree(LocationId.B));

        var resp = Send(c, """{"action":"steer","dir":"NE"}""", out var mutated);
        Assert.True(mutated);
        Assert.NotNull(resp["state"]);
        Assert.Equal(Direction8.NE, c.Session.Fine.BodyAt(LocationId.B, bag.Id).Held);

        Send(c, """{"action":"steer","loc":"B","dir":"none"}""");
        Assert.True(c.Session.Fine.IsEmpty);
        Send(c, """{"action":"steer","dir":"S"}""");
        Send(c, """{"action":"steer","dir":null}""");
        Assert.True(c.Session.Fine.IsEmpty);
    }

    [Fact]
    public void Steer_InAGridLockedBag_IsHandled_ButChangesNothing()
    {
        var c = NewController();
        var before = c.Session;
        var resp = Send(c, """{"action":"steer","dir":"E"}""", out var mutated);
        Assert.True(mutated);
        Assert.Null(resp["error"]);
        Assert.Same(before, c.Session);
    }
}
