using Pockets.Core.Models;

namespace Pockets.Core.Sim;

/// <summary>
/// Read-only view of one bag's grid for rules (two-layer #23, hot-set option; Slice 1.4). Rules read cells
/// through this rather than a concrete <see cref="Grid"/>, so a mutable struct-of-arrays bag (model B) can
/// slot in behind it in Phase 7 without rule rewrites.
/// </summary>
public interface IBagView
{
    /// <summary>Grid width in cells.</summary>
    int Columns { get; }

    /// <summary>Grid height in cells.</summary>
    int Rows { get; }

    /// <summary>Number of cells (<c>Columns × Rows</c>).</summary>
    int Count { get; }

    /// <summary>The cell at row-major index <paramref name="index"/>, including any uncommitted write.</summary>
    Cell Cell(int index);

    /// <summary>
    /// The in-bounds Moore neighbours of <paramref name="index"/> (up to 8 row-major indices; edges do not
    /// wrap), in row-major order. Field rules gather from these.
    /// </summary>
    IEnumerable<int> Neighbourhood(int index);
}

/// <summary>
/// The write side of the bag seam. Inside any <see cref="IRule"/> and any code reachable from the step's
/// rule phase, grid writes go through this, never per-change <c>Grid.SetCell</c> / <c>Cells.SetItem</c>
/// (the SetItem ban, enforced by <c>RuleArchitectureTests</c>). A writer is single-use: after
/// <see cref="Commit"/> it rejects further writes.
/// </summary>
public interface IBagWriter : IBagView
{
    /// <summary>Stages <paramref name="cell"/> at <paramref name="index"/>; visible to <see cref="IBagView.Cell"/> at once.</summary>
    void Set(int index, Cell cell);

    /// <summary>
    /// Materializes the staged writes as a new <see cref="Grid"/> (the source grid is untouched). With no
    /// writes it returns the source grid instance itself, so unchanged state stays reference- and record-equal.
    /// </summary>
    Grid Commit();
}
