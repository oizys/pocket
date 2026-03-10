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
    /// Currently white for all categories; reserved for CellFrame differentiation later.
    /// </summary>
    public static Color GetBorderForeground(Category _) => Color.White;
}
