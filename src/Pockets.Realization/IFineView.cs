using Pockets.Core.Models;
using Pockets.Core.Sim;

namespace Pockets.Realization;

/// <summary>
/// The L2a offset source (P2-D9): fine, centre-relative offsets within a cell, in 1/16-cell sub-units.
/// Slice 2.6 backs it with real fine state; until then only <see cref="FineView.Empty"/> exists.
/// </summary>
public interface IFineView
{
    /// <summary>The fine offset of the stack resting in <paramref name="cell"/>, centre-relative.</summary>
    (int Dx, int Dy) StackOffset(CellRef cell);

    /// <summary>The fine offset of the body of <paramref name="loc"/> in bag <paramref name="bag"/>, centre-relative.</summary>
    (int Px, int Py) BodyOffset(LocationId loc, Guid bag);
}

/// <summary>Ready-made <see cref="IFineView"/> instances.</summary>
public static class FineView
{
    /// <summary>A fine view with no offsets: everything sits at its cell centre.</summary>
    public static IFineView Empty { get; } = new EmptyFineView();

    /// <summary>The all-zero fine view.</summary>
    private sealed class EmptyFineView : IFineView
    {
        /// <summary>Always (0, 0).</summary>
        public (int Dx, int Dy) StackOffset(CellRef cell) => (0, 0);

        /// <summary>Always (0, 0).</summary>
        public (int Px, int Py) BodyOffset(LocationId loc, Guid bag) => (0, 0);
    }
}
