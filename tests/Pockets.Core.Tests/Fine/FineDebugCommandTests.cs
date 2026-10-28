using System.Text.Json.Nodes;
using Pockets.Core.Data;
using Pockets.Core.Fine;
using Pockets.Core.Models;
using Pockets.Core.Rendering;
using Pockets.Core.Sim;

namespace Pockets.Core.Tests.Fine;

/// <summary>
/// Slice 2.6, debug protocol: a <c>fine</c> query (the session's <see cref="FineText"/> lines, named through the
/// controller's event naming stream; no mutation) and the test-only <c>setOffset {loc, index, dx, dy}</c> command,
/// which goes through the controller as <see cref="Intent.SetFineOffset"/> (P2-D11).
/// </summary>
public class FineDebugCommandTests
{
    private static GameController NewController() =>
        new(GameInitializer.CreateDemoProfile(ContentLoader.LoadFromDirectory(TestPaths.DataDir)).NewSession());

    private static JsonNode Send(GameController c, string json, out bool mutated) =>
        JsonNode.Parse(DebugCommandHandler.Handle(c, json, out mutated))!;

    /// <summary>The first occupied cell index of B's active bag.</summary>
    private static int Occupied(GameController c) =>
        c.Session.Current.ActiveBagAt(LocationId.B).Grid.Cells.Select((cell, i) => (cell, i)).First(t => !t.cell.IsEmpty).i;

    private static List<string> Fine(JsonNode resp) => resp["fine"]!.AsArray().Select(n => n!.GetValue<string>()).ToList();

    [Fact]
    public void FineQuery_OnAFreshSession_IsEmpty_AndDoesNotMutate()
    {
        var c = NewController();
        var resp = Send(c, """{"action":"fine"}""", out var mutated);

        Assert.False(mutated);
        Assert.True(resp["handled"]!.GetValue<bool>());
        Assert.NotNull(resp["state"]);
        Assert.Empty(resp["events"]!.AsArray());
        Assert.Empty(Fine(resp));
    }

    [Fact]
    public void SetOffset_SetsTheSessionFineState_AndTheFineQueryShowsIt()
    {
        var c = NewController();
        var index = Occupied(c);
        var vmBefore = ViewModelSerializer.Serialize(c.Session).ToJsonString();

        var resp = Send(c, $$"""{"action":"setOffset","loc":"B","index":{{index}},"dx":3,"dy":-2}""", out var mutated);

        Assert.True(mutated);
        Assert.True(resp["handled"]!.GetValue<bool>());
        Assert.Empty(resp["events"]!.AsArray());
        Assert.Equal(vmBefore, resp["state"]!.ToJsonString()); // L2a is never in the VM
        var bag = c.Session.Current.ActiveBagAt(LocationId.B).Id;
        Assert.Equal((3, -2), c.Session.Fine.OffsetAt(new CellRef(bag, index)));
        Assert.Contains(c.Session.IntentLog, e => e.Intent == new Intent.SetFineOffset(LocationId.B, index, 3, -2));

        var names = DebugCommandHandler.EventTextFor(c).Names;
        var expected = FineText.Lines(c.Session.Fine, names);
        Assert.Equal(expected, Fine(Send(c, """{"action":"fine"}""", out _)));
        Assert.Single(expected);
        Assert.StartsWith("offset cell=bag#", expected[0]);
        Assert.EndsWith($"[{index}] dx=3 dy=-2", expected[0]);
    }

    [Theory]
    [InlineData("""{"action":"setOffset","index":0,"dx":1,"dy":1}""")]
    [InlineData("""{"action":"setOffset","loc":"Q","index":0,"dx":1,"dy":1}""")]
    [InlineData("""{"action":"setOffset","loc":"B","dx":1,"dy":1}""")]
    [InlineData("""{"action":"setOffset","loc":"B","index":0,"dy":1}""")]
    public void SetOffset_WithMissingOrBadFields_IsAnError(string json)
    {
        var c = NewController();
        var resp = Send(c, json, out var mutated);
        Assert.False(mutated);
        Assert.NotNull(resp["error"]);
        Assert.Same(FineState.Empty, c.Session.Fine);
    }
}
