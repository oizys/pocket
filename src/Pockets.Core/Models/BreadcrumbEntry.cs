namespace Pockets.Core.Models;

/// <summary>
/// One entry in the breadcrumb stack. Records which cell in the parent bag was entered
/// and where the cursor was at that time, enabling navigation back up.
/// </summary>
public record BreadcrumbEntry(int CellIndex, Cursor SavedCursor);
