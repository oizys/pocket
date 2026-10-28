using System.Collections.Immutable;
using Pockets.Core.Fine;
using Pockets.Core.Models;
using Pockets.Core.Sim;

namespace Pockets.Core.Tests.Fine;

/// <summary>
/// Slice 2.7, the pure D2-B integrator (<see cref="Integrator"/>, fixed-point, 1/16ths): velocity from the held
/// direction's policy table (orthogonal 4, diagonal 3 per axis), <c>p += v</c>, a cell crossing past ±8 reported as a
/// <see cref="Direction"/> (X before Y), the outer grid edge clamped to ±7 with no crossing (P2-D10), a refused
/// crossing clamped to ±7 with that axis's velocity zeroed (and held there, so a tree bumps once), release settling to
/// the centre at 4 per tick, and the nudge (P2-D11).
/// </summary>
public class IntegratorTests
{
    private static readonly FinePolicy Policy = FinePolicy.Default;

    /// <summary>A body at the centre of cell (1, 1) of a 4×3 grid, held in <paramref name="held"/>.</summary>
    private static FineBody Held(Direction8? held, int px = 0, int py = 0, int vx = 0, int vy = 0) =>
        new(px, py, vx, vy, held);

    private static (FineBody Body, ImmutableArray<Direction> Crossings) Step(FineBody b, int col = 1, int row = 1) =>
        Integrator.Advance(b, new Position(row, col), columns: 4, rows: 3, Policy);

    [Fact]
    public void Policy_Default_IsTheRatifiedTable()
    {
        Assert.Equal(4, Policy.OrthogonalSpeed);
        Assert.Equal(3, Policy.DiagonalSpeed);
        Assert.Equal(4, Policy.SettleSpeed);
        Assert.Equal(8, Policy.TicksPerSecond);
        Assert.Equal(125, Policy.TickMs);
        Assert.Equal(8, FinePolicy.Half);
        Assert.Equal(7, FinePolicy.Clamp);
    }

    [Theory]
    [InlineData(Direction8.N, 0, -1)]
    [InlineData(Direction8.NE, 1, -1)]
    [InlineData(Direction8.E, 1, 0)]
    [InlineData(Direction8.SE, 1, 1)]
    [InlineData(Direction8.S, 0, 1)]
    [InlineData(Direction8.SW, -1, 1)]
    [InlineData(Direction8.W, -1, 0)]
    [InlineData(Direction8.NW, -1, -1)]
    public void Components_AreScreenAxes_YDown(Direction8 d, int x, int y)
    {
        Assert.Equal((x, y), Integrator.Components(d));
    }

    [Fact]
    public void HeldEast_CrossesOneCell_AtTheThirdTick_ThenEveryFourth()
    {
        // From centre: 4, 8, then 12 would pass 8 → a Right crossing (p is left at 12 until resolved).
        var b = Held(Direction8.E);
        var trace = new List<int>();
        var crossings = new List<int>();
        for (var tick = 1; tick <= 7; tick++)
        {
            var (next, cross) = Step(b);
            if (cross.Length > 0)
            {
                Assert.Equal(new[] { Direction.Right }, cross);
                crossings.Add(tick);
                next = Integrator.Resolve(next, Direction.Right, accepted: true);
            }
            trace.Add(next.Px);
            b = next;
        }
        Assert.Equal(new[] { 4, 8, -4, 0, 4, 8, -4 }, trace);
        Assert.Equal(new[] { 3, 7 }, crossings);
        Assert.Equal((4, 0), (b.Vx, b.Vy));
    }

    [Fact]
    public void Diagonal_UsesThreePerAxis_AndCrossesXBeforeY()
    {
        var b = Held(Direction8.SE);
        (b, var c1) = Step(b);
        (b, var c2) = Step(b);
        Assert.Empty(c1);
        Assert.Empty(c2);
        Assert.Equal((6, 6, 3, 3), (b.Px, b.Py, b.Vx, b.Vy));

        var (at9, cross) = Step(b);
        Assert.Equal(new[] { Direction.Right, Direction.Down }, cross);
        var resolved = Integrator.Resolve(Integrator.Resolve(at9, Direction.Right, true), Direction.Down, true);
        Assert.Equal((-7, -7), (resolved.Px, resolved.Py));
    }

    [Fact]
    public void RefusedCrossing_ClampsToSeven_AndZeroesThatAxis()
    {
        var b = Held(Direction8.E, px: 8, vx: 4);
        var (next, cross) = Step(b);
        Assert.Equal(new[] { Direction.Right }, cross);

        var refused = Integrator.Resolve(next, Direction.Right, accepted: false);
        Assert.Equal((7, 0), (refused.Px, refused.Vx));
        Assert.Equal(Direction8.E, refused.Held);
    }

    [Fact]
    public void BlockedAxis_StaysPressed_WithNoFurtherCrossing()
    {
        // After a refusal the body rests at +7 with Vx 0: still held E, it presses and never re-crosses (one bump).
        var b = Held(Direction8.E, px: 7, vx: 0);
        for (var i = 0; i < 5; i++)
        {
            var (next, cross) = Step(b);
            Assert.Empty(cross);
            Assert.Equal((7, 0), (next.Px, next.Vx));
            b = next;
        }
    }

    [Fact]
    public void BlockedAxis_OtherAxisStillMoves()
    {
        var b = Held(Direction8.NE, px: 7, vx: 0);
        var (next, cross) = Step(b);
        Assert.Empty(cross);
        Assert.Equal((7, -3, 0, -3), (next.Px, next.Py, next.Vx, next.Vy));
    }

    [Theory]
    [InlineData(Direction8.E, 3, 1, 7, 0)]   // east edge
    [InlineData(Direction8.W, 0, 1, -7, 0)]  // west edge
    [InlineData(Direction8.S, 1, 2, 0, 7)]   // south edge
    [InlineData(Direction8.N, 1, 0, 0, -7)]  // north edge
    public void OuterEdge_Clamps_WithNoCrossing(Direction8 d, int col, int row, int px, int py)
    {
        var (dx, dy) = Integrator.Components(d);
        var b = Held(d, px: 8 * dx, py: 8 * dy, vx: 4 * dx, vy: 4 * dy);
        var (next, cross) = Step(b, col, row);
        Assert.Empty(cross);
        Assert.Equal((px, py, 0, 0), (next.Px, next.Py, next.Vx, next.Vy));
    }

    [Fact]
    public void DiagonalIntoTheCorner_ClampsBothAxes_WithNoCrossing()
    {
        var b = Held(Direction8.SE, px: 6, py: 6, vx: 3, vy: 3);
        var (next, cross) = Step(b, col: 3, row: 2);
        Assert.Empty(cross);
        Assert.Equal((7, 7, 0, 0), (next.Px, next.Py, next.Vx, next.Vy));
    }

    [Fact]
    public void Release_SettlesToCentre_AtFourPerTick_ThenRests()
    {
        var b = Held(null, px: 7, py: -6, vx: 4, vy: 0);
        var xs = new List<(int, int)>();
        while (!b.IsRest && xs.Count < 10)
        {
            (b, var cross) = Step(b);
            Assert.Empty(cross);
            xs.Add((b.Px, b.Py));
        }
        Assert.Equal(new[] { (3, -2), (0, 0) }, xs);
        Assert.True(b.IsRest);
    }

    [Fact]
    public void HeldOrthogonal_SettlesTheOtherAxis()
    {
        var b = Held(Direction8.E, px: 0, py: 5, vx: 3, vy: 3);
        var (next, _) = Step(b);
        Assert.Equal((4, 1, 4, -4), (next.Px, next.Py, next.Vx, next.Vy));
        (next, _) = Step(next);
        Assert.Equal((8, 0, 4, 0), (next.Px, next.Py, next.Vx, next.Vy));
    }

    [Fact]
    public void Rest_StaysRest()
    {
        var (next, cross) = Step(FineBody.Rest);
        Assert.Empty(cross);
        Assert.Equal(FineBody.Rest, next);
    }

    // ==================== Nudge (P2-D11) ====================

    private static readonly ItemType Rock = new("Rock", Category.Material, IsStackable: true, MaxStackSize: 20);

    /// <summary>A 4×3 Forest bag with a rock at cell 5 = (1, 1) and a tree at cell 6 = (1, 2).</summary>
    private static Bag World() => new(Grid.Create(4, 3)
        .SetCell(5, new Cell(new ItemStack(Rock, 3)))
        .SetCell(6, new Cell(new ItemStack(Rock, 1), Frame: new TreeFrame())), "Forest");

    [Fact]
    public void Nudge_PushesTheStackInTheBodysCell_OneSixteenthPerTick_InVsDirection()
    {
        var bag = World();
        var at = new CellRef(bag.Id, 5);
        var fine = Integrator.Nudge(FineState.Empty, bag, new Position(1, 1), new FineBody(4, 0, 4, -3, Direction8.NE));
        Assert.Equal((1, -1), fine.OffsetAt(at));
    }

    [Fact]
    public void Nudge_IsCappedAtSix()
    {
        var bag = World();
        var at = new CellRef(bag.Id, 5);
        var fine = Enumerable.Range(0, 10).Aggregate(FineState.Empty,
            (f, _) => Integrator.Nudge(f, bag, new Position(1, 1), new FineBody(0, 0, -4, 0, Direction8.W)));
        Assert.Equal((-6, 0), fine.OffsetAt(at));
    }

    [Fact]
    public void Nudge_NothingWhenStill_OrTheCellIsEmpty()
    {
        var bag = World();
        Assert.Same(FineState.Empty, Integrator.Nudge(FineState.Empty, bag, new Position(1, 1), new FineBody(3, 0, 0, 0, Direction8.E)));
        Assert.Same(FineState.Empty, Integrator.Nudge(FineState.Empty, bag, new Position(0, 0), new FineBody(3, 0, 4, 0, Direction8.E)));
    }

    [Fact]
    public void Nudge_ReachesTheNeighbour_OnlyAtTheBoundary_AndNeverATree()
    {
        var bag = World();
        // In cell 4 = (1, 0) at Px = 8: the body overlaps cell 5 by 8/16, so the rock there is nudged too.
        var fine = Integrator.Nudge(FineState.Empty, bag, new Position(1, 0), new FineBody(8, 0, 4, 0, Direction8.E));
        Assert.Equal((1, 0), fine.OffsetAt(new CellRef(bag.Id, 5)));
        // At Px = 7 it overlaps by 7: no.
        Assert.Same(FineState.Empty, Integrator.Nudge(FineState.Empty, bag, new Position(1, 0), new FineBody(7, 0, 4, 0, Direction8.E)));
        // In cell 5 at Px = 8 against the tree at cell 6: the rock under the body is nudged, the tree is not.
        var pressed = Integrator.Nudge(FineState.Empty, bag, new Position(1, 1), new FineBody(8, 0, 4, 0, Direction8.E));
        Assert.Equal((1, 0), pressed.OffsetAt(new CellRef(bag.Id, 5)));
        Assert.Equal((0, 0), pressed.OffsetAt(new CellRef(bag.Id, 6)));
    }

    // ==================== World bags and the flag ====================

    [Fact]
    public void IsWorldBag_WildernessType_OrEnterOnly()
    {
        Assert.True(Integrator.IsWorldBag(new Bag(Grid.Create(2, 2), "Forest")));
        Assert.True(Integrator.IsWorldBag(new Bag(Grid.Create(2, 2), "Quiet 1") { EnterOnly = true }));
        Assert.False(Integrator.IsWorldBag(new Bag(Grid.Create(2, 2))));
    }

    [Fact]
    public void Bag_Movement_DefaultsToGridLocked_AndHasNoZeroMember()
    {
        Assert.Equal(MovementModel.GridLocked, new Bag(Grid.Create(2, 2)).Movement);
        Assert.DoesNotContain(0, Enum.GetValues<MovementModel>().Cast<int>());
        Assert.Equal(new[] { "GridLocked", "Free" }, Enum.GetNames<MovementModel>());
    }
}
