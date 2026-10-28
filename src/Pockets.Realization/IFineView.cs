using Pockets.Core.Fine;
using Pockets.Core.Models;
using Pockets.Core.Sim;

namespace Pockets.Realization;

/// <summary>
/// The L2a offset source (P2-D9): fine, centre-relative offsets within a cell, in 1/16-cell sub-units.
/// Slice 2.6 backs it with the session's <see cref="FineState"/> (<see cref="FineView.Of"/>); <see cref="FineView.Empty"/> is the all-zero view.
/// </summary>
public interface IFineView
{
    /// <summary>The fine offset of the stack resting in <paramref name="cell"/>, centre-relative.</summary>
    (int Dx, int Dy) StackOffset(CellRef cell);

    /// <summary>The fine offset of the body of <paramref name="loc"/> in bag <paramref name="bag"/>, centre-relative.</summary>
    (int Px, int Py) BodyOffset(LocationId loc, Guid bag);

    /// <summary>
    /// True when the body of <paramref name="loc"/> in <paramref name="bag"/> is being integrated: stored with a held
    /// direction or a non-zero velocity (D2-B). Views that know nothing of bodies in motion say false.
    /// </summary>
    bool IsDriven(LocationId loc, Guid bag) => false;
}

/// <summary>Ready-made <see cref="IFineView"/> instances.</summary>
public static class FineView
{
    /// <summary>A fine view with no offsets: everything sits at its cell centre.</summary>
    public static IFineView Empty { get; } = new EmptyFineView();

    /// <summary>
    /// A view over <paramref name="fine"/>: the same <see cref="Empty"/> instance when it holds nothing, else an
    /// adapter reading its stack offsets and its bodies' positions.
    /// </summary>
    public static IFineView Of(FineState fine) => fine.IsEmpty ? Empty : new StateFineView(fine);

    /// <summary>An <see cref="IFineView"/> reading a <see cref="FineState"/>.</summary>
    private sealed class StateFineView(FineState fine) : IFineView
    {
        /// <summary>The stored offset of <paramref name="cell"/>, else (0, 0).</summary>
        public (int Dx, int Dy) StackOffset(CellRef cell) => fine.OffsetAt(cell);

        /// <summary>The stored body position of <paramref name="loc"/> in <paramref name="bag"/>, else (0, 0).</summary>
        public (int Px, int Py) BodyOffset(LocationId loc, Guid bag)
        {
            var body = fine.BodyAt(loc, bag);
            return (body.Px, body.Py);
        }

        /// <summary>True when the stored body has a held direction or a non-zero velocity.</summary>
        public bool IsDriven(LocationId loc, Guid bag)
        {
            var body = fine.BodyAt(loc, bag);
            return body.Held is not null || body.Vx != 0 || body.Vy != 0;
        }
    }

    /// <summary>The all-zero fine view.</summary>
    private sealed class EmptyFineView : IFineView
    {
        /// <summary>Always (0, 0).</summary>
        public (int Dx, int Dy) StackOffset(CellRef cell) => (0, 0);

        /// <summary>Always (0, 0).</summary>
        public (int Px, int Py) BodyOffset(LocationId loc, Guid bag) => (0, 0);

        /// <summary>Always false.</summary>
        public bool IsDriven(LocationId loc, Guid bag) => false;
    }
}
