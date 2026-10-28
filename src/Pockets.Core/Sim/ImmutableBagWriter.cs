using System.Runtime.InteropServices;
using Pockets.Core.Models;

namespace Pockets.Core.Sim;

/// <summary>
/// The one Phase 1 <see cref="IBagWriter"/> (spike B's a1 style, re-implemented under TDD): the first
/// <see cref="Set"/> copies the source cells into one private array, later writes go into it in place, and
/// <see cref="Commit"/> wraps that array as the new grid's <see cref="ImmutableArray{T}"/> without copying.
/// One array per bag per commit, against one per change for chained <c>SetCell</c>s.
/// <para>
/// A struct so a writer costs no allocation of its own: use it as a local and call it directly (copying it
/// copies the staged writes' owner, so don't). Boxing it to <see cref="IBagView"/> is fine for reads.
/// </para>
/// </summary>
public struct ImmutableBagWriter : IBagWriter
{
    private readonly Grid _source;
    private Cell[]? _cells;
    private bool _committed;

    /// <summary>Starts a writer over <paramref name="source"/>; nothing is copied until the first write.</summary>
    public ImmutableBagWriter(Grid source)
    {
        _source = source;
        _cells = null;
        _committed = false;
    }

    /// <inheritdoc />
    public readonly int Columns => _source.Columns;

    /// <inheritdoc />
    public readonly int Rows => _source.Rows;

    /// <inheritdoc />
    public readonly int Count => _source.Cells.Length;

    /// <inheritdoc />
    public readonly Cell Cell(int index) => _cells is null ? _source.Cells[index] : _cells[index];

    /// <inheritdoc />
    public readonly IEnumerable<int> Neighbourhood(int index)
    {
        var columns = Columns;
        var rows = Rows;
        var row = index / columns;
        var col = index % columns;
        return Enumerable.Range(-1, 3)
            .SelectMany(dr => Enumerable.Range(-1, 3).Select(dc => (Row: row + dr, Col: col + dc)))
            .Where(p => (p.Row, p.Col) != (row, col) && p.Row >= 0 && p.Row < rows && p.Col >= 0 && p.Col < columns)
            .Select(p => p.Row * columns + p.Col)
            .ToList();
    }

    /// <inheritdoc />
    public void Set(int index, Cell cell)
    {
        if (_committed)
            throw new InvalidOperationException("ImmutableBagWriter was already committed.");
        if (_cells is null)
        {
            _cells = new Cell[_source.Cells.Length];
            _source.Cells.CopyTo(_cells);
        }
        _cells[index] = cell;
    }

    /// <inheritdoc />
    public Grid Commit()
    {
        if (_committed)
            throw new InvalidOperationException("ImmutableBagWriter was already committed.");
        _committed = true;
        return _cells is null
            ? _source
            : _source with { Cells = ImmutableCollectionsMarshal.AsImmutableArray(_cells) };
    }
}
