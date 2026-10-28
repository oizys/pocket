using Pockets.Core;
using Pockets.Core.Data;
using Pockets.Core.Models;
using Pockets.Core.Sim;

namespace Pockets.Realization.Tests;

/// <summary>Rest-pose construction from a game state.</summary>
public class SceneTests
{
    private static readonly LocationId[] B = [LocationId.B];

    /// <summary>A fresh demo state.</summary>
    private static GameState Demo()
    {
        var registry = ContentLoader.LoadFromDirectory(TestPaths.DataDir);
        var dialogue = DialogueLoader.LoadFromDirectory(TestPaths.DataDir);
        return GameInitializer.CreateDemoProfile(registry, null, dialogue).NewSession().Current;
    }

    /// <summary>The demo state with B's bag turned into a Forest world bag.</summary>
    private static GameState WorldState()
    {
        var s = Demo();
        var bag = s.ActiveBagAt(LocationId.B);
        return s with { Store = s.Store.Set(bag.Id, bag with { EnvironmentType = "Forest" }) };
    }

    /// <summary>A fine view with fixed offsets.</summary>
    private sealed class FakeFine : IFineView
    {
        public (int Dx, int Dy) StackOffset(CellRef cell) => (3, -2);
        public (int Px, int Py) BodyOffset(LocationId loc, Guid bag) => (-5, 7);
    }

    /// <summary>The demo's B scene has no body and one centred stack per non-empty cell.</summary>
    [Fact]
    public void DemoStart_HasNoBody_AndCentredStacks()
    {
        var s = Demo();
        var bag = s.ActiveBagAt(LocationId.B);
        var scene = Scene.FromState(s, B);
        var expected = bag.Grid.Cells.Select((c, i) => (c, i)).Where(t => !t.c.IsEmpty).Select(t => t.i).ToList();
        Assert.DoesNotContain(scene.Poses, p => p.Entity is EntityKey.Body);
        Assert.Equal(expected, scene.Poses.Select(p => ((EntityKey.Stack)p.Entity).At.Index).ToList());
        Assert.All(scene.Poses, p =>
        {
            Assert.Equal(bag.Id, p.Bag);
            Assert.Equal((0, 0), (p.SubX, p.SubY));
            Assert.Null(p.Height);
            Assert.Null(p.Facing);
        });
    }

    /// <summary>A world bag in B yields a body at the cursor, facing S, before the stacks.</summary>
    [Fact]
    public void WorldBag_YieldsBodyAtCursor_FacingSouth()
    {
        var s = WorldState();
        var cur = s.Locations.Get(LocationId.B).Cursor.Position;
        var scene = Scene.FromState(s, B);
        var body = Assert.IsType<EntityKey.Body>(scene.Poses[0].Entity);
        Assert.Equal(LocationId.B, body.Loc);
        Assert.Equal((cur.Col, cur.Row), (scene.Poses[0].CellCol, scene.Poses[0].CellRow));
        Assert.Equal(Facing.S, scene.Poses[0].Facing);
        Assert.Null(scene.Poses[0].Height);
    }

    /// <summary>An enter-only bag counts as a world bag, a plain one does not.</summary>
    [Fact]
    public void IsWorldBag_ByWildernessOrEnterOnly()
    {
        var plain = new Bag(Grid.Create(1, 1));
        Assert.False(Scene.IsWorldBag(plain));
        Assert.True(Scene.IsWorldBag(plain with { EnvironmentType = "Forest" }));
        Assert.True(Scene.IsWorldBag(plain with { EnterOnly = true }));
    }

    /// <summary>The facings override replaces the default S.</summary>
    [Fact]
    public void Facings_Override()
    {
        var scene = Scene.FromState(WorldState(), B, null, new Dictionary<LocationId, Facing> { [LocationId.B] = Facing.NE });
        Assert.Equal(Facing.NE, scene.Find(new EntityKey.Body(LocationId.B))!.Value.Facing);
    }

    /// <summary>Fine-view offsets reach stack and body poses.</summary>
    [Fact]
    public void FineView_OffsetsReachPoses()
    {
        var scene = Scene.FromState(WorldState(), B, new FakeFine());
        Assert.All(scene.Poses, p =>
        {
            var want = p.Entity is EntityKey.Body ? (-5, 7) : (3, -2);
            Assert.Equal(want, (p.SubX, p.SubY));
        });
        Assert.Contains(scene.Poses, p => p.Entity is EntityKey.Stack);
    }

    /// <summary>A location the state does not have is skipped.</summary>
    [Fact]
    public void AbsentLocation_Skipped()
    {
        var s = Demo();
        var absent = Enum.GetValues<LocationId>().First(l => s.Locations.TryGet(l) is null);
        Assert.Equal(Scene.FromState(s, B).Poses, Scene.FromState(s, [absent, LocationId.B]).Poses);
    }

    /// <summary>The same bag shown twice emits its stacks once, and bodies per location.</summary>
    [Fact]
    public void SameBagTwice_StacksOnce_BodiesPerLocation()
    {
        var s = WorldState();
        var rootId = s.Locations.Get(LocationId.B).BagId;
        var twin = s with { Locations = s.Locations.Set(LocationId.C, s.Locations.Get(LocationId.B)) };
        var scene = Scene.FromState(twin, [LocationId.B, LocationId.C]);
        Assert.Equal(2, scene.Poses.Count(p => p.Entity is EntityKey.Body));
        var stacks = scene.Poses.Where(p => p.Entity is EntityKey.Stack).Select(p => p.Entity).ToList();
        Assert.Equal(stacks.Distinct().Count(), stacks.Count);
        Assert.NotEmpty(stacks);
        Assert.Equal(rootId, scene.Poses[0].Bag);
    }

    /// <summary>[H, T] give the hand and toolbar bag stacks.</summary>
    [Fact]
    public void HandAndToolbar_GiveTheirStacks()
    {
        var s = Demo();
        var scene = Scene.FromState(s, [LocationId.H, LocationId.T]);
        var handCount = s.HandBag.Grid.Cells.Count(c => !c.IsEmpty);
        var toolbar = s.Store.GetById(s.ToolbarBagId!.Value)!;
        Assert.Equal(handCount, scene.Poses.Count(p => p.Bag == s.HandBag.Id));
        Assert.Equal(toolbar.Grid.Cells.Count(c => !c.IsEmpty), scene.Poses.Count(p => p.Bag == toolbar.Id));
    }

    /// <summary>The empty fine view is all zero.</summary>
    [Fact]
    public void FineViewEmpty_IsZero()
    {
        Assert.Equal((0, 0), FineView.Empty.StackOffset(new CellRef(Guid.NewGuid(), 3)));
        Assert.Equal((0, 0), FineView.Empty.BodyOffset(LocationId.B, Guid.NewGuid()));
    }

    /// <summary>Find returns the pose with that entity, or null.</summary>
    [Fact]
    public void Find_ReturnsPoseOrNull()
    {
        var scene = Scene.FromState(WorldState(), B);
        Assert.NotNull(scene.Find(new EntityKey.Body(LocationId.B)));
        Assert.Null(scene.Find(new EntityKey.Body(LocationId.W)));
    }

    /// <summary>The scene records each shown bag's grid width (for cell-index → (col,row) in timelines).</summary>
    [Fact]
    public void Columns_PerShownBag()
    {
        var s = Demo();
        var bag = s.ActiveBagAt(LocationId.B);
        var scene = Scene.FromState(s, [LocationId.B, LocationId.H]);
        Assert.Equal(bag.Grid.Columns, scene.Columns[bag.Id]);
        Assert.Equal(s.HandBag.Grid.Columns, scene.Columns[s.HandBag.Id]);
        Assert.Equal(2, scene.Columns.Count);
    }
}
