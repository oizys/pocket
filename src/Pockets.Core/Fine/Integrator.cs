using System.Collections.Immutable;
using Pockets.Core.Models;
using Pockets.Core.Sim;

namespace Pockets.Core.Fine;

/// <summary>
/// The pure D2-B integrator (P2-D8, P2-D10, P2-D11): fixed-point, ints only. The session shell calls it; it never
/// touches L1 state itself, it only reports cell crossings for the shell to run as ordinary moves.
/// </summary>
public static class Integrator
{
    /// <summary>True when <paramref name="bag"/> is a world bag (must agree with the realization's scene rule).</summary>
    public static bool IsWorldBag(Bag bag) => GameState.IsWildernessType(bag.EnvironmentType) || bag.EnterOnly;

    /// <summary>
    /// True when the body of <paramref name="at"/> is integrated: the session is realtime, the location exists, and
    /// its active bag is a world bag whose movement flag is <see cref="MovementModel.Free"/>.
    /// </summary>
    public static bool IsFree(GameState game, TickMode mode, LocationId at)
    {
        if (mode != TickMode.Realtime || game.Locations.TryGet(at) is null) return false;
        var bag = game.ActiveBagAt(at);
        return IsWorldBag(bag) && bag.Movement == MovementModel.Free;
    }

    /// <summary>The screen-axis components of <paramref name="d"/>, y down (row increasing).</summary>
    public static (int X, int Y) Components(Direction8 d) => d switch
    {
        Direction8.N => (0, -1),
        Direction8.NE => (1, -1),
        Direction8.E => (1, 0),
        Direction8.SE => (1, 1),
        Direction8.S => (0, 1),
        Direction8.SW => (-1, 1),
        Direction8.W => (-1, 0),
        _ => (-1, -1)
    };

    /// <summary>
    /// Sets (or, with null, releases) the held direction of <paramref name="at"/>'s body. Returns the same instance
    /// when the body is not free or nothing changes.
    /// </summary>
    public static FineState Steer(FineState fine, GameState game, TickMode mode, LocationId at, Direction8? dir)
    {
        if (!IsFree(game, mode, at)) return fine;
        var bag = game.ActiveBagAt(at).Id;
        return fine.WithBody(at, bag, fine.BodyAt(at, bag) with { Held = dir });
    }

    /// <summary>
    /// The stored bodies that are integrated now: their bag is the active bag at their location and that location is
    /// free. Ordered by location.
    /// </summary>
    public static ImmutableArray<(LocationId Loc, Guid Bag)> ActiveBodies(FineState fine, GameState game, TickMode mode) =>
        fine.Bodies.Keys.Where(k => IsActive(k, game, mode)).OrderBy(k => k.Loc).ToImmutableArray();

    /// <summary>The stored bodies that are not integrated now (a stale bag, a no-longer-free location).</summary>
    public static ImmutableArray<(LocationId Loc, Guid Bag)> StaleBodies(FineState fine, GameState game, TickMode mode) =>
        fine.Bodies.Keys.Where(k => !IsActive(k, game, mode)).OrderBy(k => k.Loc).ToImmutableArray();

    /// <summary>True when the body keyed <paramref name="key"/> is in its location's active bag and the location is free.</summary>
    private static bool IsActive((LocationId Loc, Guid Bag) key, GameState game, TickMode mode) =>
        IsFree(game, mode, key.Loc) && game.ActiveBagAt(key.Loc).Id == key.Bag;

    /// <summary>
    /// One tick of one body in cell <paramref name="cell"/> of a <paramref name="columns"/> × <paramref name="rows"/>
    /// grid. Each axis: a held axis moves at the policy speed unless it is blocked (at the refusal clamp with no
    /// velocity), a free axis settles to the centre. Passing the half-cell mark is a crossing (X before Y, position
    /// left unresolved for <see cref="Resolve"/>), except on the outer edge, which clamps with no crossing.
    /// </summary>
    public static (FineBody Body, ImmutableArray<Direction> Crossings) Advance(
        FineBody body, Position cell, int columns, int rows, FinePolicy policy)
    {
        var (hx, hy) = body.Held is { } d ? Components(d) : (0, 0);
        var speed = hx != 0 && hy != 0 ? policy.DiagonalSpeed : policy.OrthogonalSpeed;
        var crossings = ImmutableArray.CreateBuilder<Direction>(2);
        var (px, vx) = Axis(body.Px, body.Vx, hx, speed, policy, cell.Col, columns, Direction.Left, Direction.Right, crossings);
        var (py, vy) = Axis(body.Py, body.Vy, hy, speed, policy, cell.Row, rows, Direction.Up, Direction.Down, crossings);
        return (body with { Px = px, Py = py, Vx = vx, Vy = vy }, crossings.ToImmutable());
    }

    /// <summary>One axis of <see cref="Advance"/>: the new position and velocity, adding a crossing when one happens.</summary>
    private static (int P, int V) Axis(int p, int v, int h, int speed, FinePolicy policy, int index, int count,
        Direction negative, Direction positive, ImmutableArray<Direction>.Builder crossings)
    {
        int np, nv;
        if (h != 0)
        {
            if (v == 0 && p == h * FinePolicy.Clamp) { np = p; nv = 0; }
            else { nv = h * speed; np = p + nv; }
        }
        else
        {
            var step = Math.Clamp(-p, -policy.SettleSpeed, policy.SettleSpeed);
            np = p + step;
            nv = np == 0 ? 0 : step;
        }

        if (np > FinePolicy.Half)
        {
            if (index == count - 1) { np = FinePolicy.Clamp; nv = 0; }
            else crossings.Add(positive);
        }
        else if (np < -FinePolicy.Half)
        {
            if (index == 0) { np = -FinePolicy.Clamp; nv = 0; }
            else crossings.Add(negative);
        }
        return (np, nv);
    }

    /// <summary>
    /// Settles one crossing reported by <see cref="Advance"/>: accepted, the position wraps into the next cell
    /// (one cell is 16 units); refused, it clamps to ±<see cref="FinePolicy.Clamp"/> with that axis's velocity zeroed.
    /// </summary>
    public static FineBody Resolve(FineBody body, Direction crossing, bool accepted)
    {
        var s = crossing is Direction.Right or Direction.Down ? 1 : -1;
        var horizontal = crossing is Direction.Right or Direction.Left;
        if (accepted)
            return horizontal ? body with { Px = body.Px - 16 * s } : body with { Py = body.Py - 16 * s };
        return horizontal
            ? body with { Px = s * FinePolicy.Clamp, Vx = 0 }
            : body with { Py = s * FinePolicy.Clamp, Vy = 0 };
    }

    /// <summary>
    /// The nudge (P2-D11): a moving body pushes every non-empty, navigable stack it overlaps by at least half a cell
    /// on both axes one 1/16 per tick in its velocity's direction. Returns the same instance when nothing changes.
    /// </summary>
    public static FineState Nudge(FineState fine, Bag bag, Position cell, FineBody body)
    {
        if (body.Vx == 0 && body.Vy == 0) return fine;
        var (sx, sy) = (Math.Sign(body.Vx), Math.Sign(body.Vy));
        var grid = bag.Grid;
        foreach (var dy in new[] { -1, 0, 1 })
            foreach (var dx in new[] { -1, 0, 1 })
            {
                var col = cell.Col + dx;
                var row = cell.Row + dy;
                if (col < 0 || col >= grid.Columns || row < 0 || row >= grid.Rows) continue;
                if (Overlap(body.Px, dx) < 8 || Overlap(body.Py, dy) < 8) continue;
                var index = row * grid.Columns + col;
                var target = grid.Cells[index];
                if (target.IsEmpty || target.IsUnnavigable) continue;
                var at = new CellRef(bag.Id, index);
                var o = fine.OffsetAt(at);
                fine = fine.WithOffset(at, o.Dx + sx, o.Dy + sy);
            }
        return fine;
    }

    /// <summary>How far, in 1/16 units, a body at offset <paramref name="p"/> overlaps the neighbour at <paramref name="d"/> on one axis.</summary>
    private static int Overlap(int p, int d) => d == 0 ? 16 - Math.Abs(p) : Math.Sign(p) == d ? Math.Abs(p) : 0;
}
