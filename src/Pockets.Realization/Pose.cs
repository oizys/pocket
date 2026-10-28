namespace Pockets.Realization;

/// <summary>
/// Where one entity is drawn: a cell plus a sub-cell offset. <see cref="SubX"/> and <see cref="SubY"/> are
/// centre-relative, in −8…+8 sub-units; <see cref="Height"/> is in sub-units too (1/16 cell). No validation here.
/// </summary>
/// <param name="Entity">The entity this pose belongs to.</param>
/// <param name="Bag">The bag whose grid the pose is in.</param>
/// <param name="CellCol">The cell column.</param>
/// <param name="CellRow">The cell row.</param>
/// <param name="SubX">Horizontal offset from the cell centre, in sub-units.</param>
/// <param name="SubY">Vertical offset from the cell centre, in sub-units.</param>
/// <param name="Height">Lift above the grid in sub-units, or null when not applicable.</param>
/// <param name="Facing">The facing, or null when not applicable.</param>
public readonly record struct Pose(
    EntityKey Entity, Guid Bag, int CellCol, int CellRow, int SubX, int SubY, int? Height, Facing? Facing)
{
    /// <summary>Sub-units per cell (D4): 1/16 cell.</summary>
    public const int SubUnits = 16;
}
