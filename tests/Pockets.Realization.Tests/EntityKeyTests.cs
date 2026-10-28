using Pockets.Core.Models;
using Pockets.Core.Sim;

namespace Pockets.Realization.Tests;

/// <summary>Value semantics of <see cref="EntityKey"/> and <see cref="Pose"/>.</summary>
public class EntityKeyTests
{
    private static readonly Guid Bag = Guid.NewGuid();

    /// <summary>Keys compare by value, and never across kinds.</summary>
    [Fact]
    public void Equality()
    {
        Assert.Equal<EntityKey>(new EntityKey.Body(LocationId.B), new EntityKey.Body(LocationId.B));
        Assert.NotEqual<EntityKey>(new EntityKey.Body(LocationId.B), new EntityKey.Body(LocationId.H));
        Assert.Equal<EntityKey>(new EntityKey.Stack(new CellRef(Bag, 3)), new EntityKey.Stack(new CellRef(Bag, 3)));
        Assert.NotEqual<EntityKey>(new EntityKey.Stack(new CellRef(Bag, 3)), new EntityKey.Stack(new CellRef(Bag, 4)));
        Assert.NotEqual<EntityKey>(new EntityKey.Body(LocationId.B), new EntityKey.Stack(new CellRef(Bag, 0)));
    }

    /// <summary>Keys work as dictionary and set keys.</summary>
    [Fact]
    public void HashKeys()
    {
        var d = new Dictionary<EntityKey, int> { [new EntityKey.Stack(new CellRef(Bag, 1))] = 7 };
        Assert.Equal(7, d[new EntityKey.Stack(new CellRef(Bag, 1))]);
        var set = new HashSet<EntityKey> { new EntityKey.Body(LocationId.W), new EntityKey.Body(LocationId.W) };
        Assert.Single(set);
    }

    /// <summary>A pose's equality includes its entity.</summary>
    [Fact]
    public void Pose_EqualityIncludesEntity()
    {
        var a = new Pose(new EntityKey.Body(LocationId.B), Bag, 1, 1, 0, 0, null, null);
        Assert.Equal(a, a with { });
        Assert.NotEqual(a, a with { Entity = new EntityKey.Body(LocationId.H) });
    }
}
