using Terminal.Gui;
using Pockets.Core.Models;

namespace Pockets.App.Rendering;

/// <summary>
/// Maps item categories to background colors for cell borders.
/// Foreground color is reserved for future CellFrame work.
/// </summary>
public static class CategoryColors
{
    /// <summary>
    /// Returns the background color for a given category.
    /// Used on the box-drawing border of grid cells.
    /// </summary>
    public static Color GetBackground(Category category) => category switch
    {
        Category.Material   => Color.DarkGray,
        Category.Weapon     => Color.Red,
        Category.Structure  => Color.Brown,
        Category.Medicine   => Color.Green,
        Category.Tool       => Color.Blue,
        Category.Bag        => Color.Magenta,
        Category.Consumable => Color.Cyan,
        Category.Misc       => Color.DarkGray,
        _                   => Color.DarkGray
    };

    /// <summary>
    /// Returns the border foreground color (the box-drawing chars themselves).
    /// White by default; frame-specific colors override via GetFrameForeground.
    /// </summary>
    public static Color GetBorderForeground(Category _) => Color.White;

    /// <summary>
    /// Returns the border foreground color for a cell with a CellFrame.
    /// Input slots render yellow, output slots render bright green.
    /// Falls back to category-based foreground if no frame.
    /// </summary>
    public static Color GetFrameForeground(CellFrame? frame) => frame switch
    {
        InputSlotFrame  => Color.BrightYellow,
        OutputSlotFrame => Color.BrightGreen,
        _               => Color.White
    };
}
