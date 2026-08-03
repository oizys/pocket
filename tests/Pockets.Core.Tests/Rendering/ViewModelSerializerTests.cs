using System.Text.Json.Nodes;
using Pockets.Core;
using Pockets.Core.Data;
using Pockets.Core.Models;
using Pockets.Core.Rendering;

namespace Pockets.Core.Tests.Rendering;

/// <summary>
/// The view-model serializer is the source of truth for the parity checkpoint stream.
/// These tests pin its determinism (same state → identical bytes) and the absence of
/// nondeterministic fields (no raw bag GUIDs leak into the stream).
/// </summary>
public class ViewModelSerializerTests
{
    private static DemoProfile Profile() =>
        GameInitializer.CreateDemoProfile(ContentLoader.LoadFromDirectory(TestPaths.DataDir));

    [Fact]
    public void Serialize_IsDeterministic_ForIdenticalSessions()
    {
        var a = Profile().NewSession();
        var b = Profile().NewSession();

        Assert.Equal(
            ViewModelSerializer.SerializeToString(a),
            ViewModelSerializer.SerializeToString(b));
    }

    [Fact]
    public void Serialize_ContainsNoRawBagGuids()
    {
        var session = Profile().NewSession();
        var json = ViewModelSerializer.SerializeToString(session);

        // Every bag id in the store must be absent from the serialized stream —
        // GUIDs are nondeterministic and would break cross-driver diffing.
        foreach (var bag in session.Current.Store.All)
            Assert.DoesNotContain(bag.Id.ToString(), json);
    }

    [Fact]
    public void Serialize_ExposesGridCursorHandAndTickFields()
    {
        var session = Profile().NewSession();
        var vm = ViewModelSerializer.Serialize(session);

        Assert.NotNull(vm["cells"]);
        Assert.NotNull(vm["cursor"]);
        Assert.NotNull(vm["hand"]);
        Assert.Equal("Rogue", vm["tickMode"]!.GetValue<string>());
        Assert.Equal(8, vm["gridColumns"]!.GetValue<int>());
        Assert.Equal(4, vm["gridRows"]!.GetValue<int>());
        Assert.True(vm["handEmpty"]!.GetValue<bool>());
        Assert.False(vm["isNested"]!.GetValue<bool>());
    }

    [Fact]
    public void Serialize_ReflectsStateChange_AfterCursorMove()
    {
        var session = Profile().NewSession();
        var before = ViewModelSerializer.SerializeToString(session);

        var moved = session.MoveCursor(Direction.Right);
        var after = ViewModelSerializer.SerializeToString(moved);

        Assert.NotEqual(before, after);
        Assert.Equal(1, ViewModelSerializer.Serialize(moved)["cursor"]!["col"]!.GetValue<int>());
    }
}
