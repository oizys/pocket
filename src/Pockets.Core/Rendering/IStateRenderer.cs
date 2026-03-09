using Pockets.Core.Models;

namespace Pockets.Core.Rendering;

/// <summary>
/// Renders a GameState as a plain-text string for display outside the TUI.
/// Implementations produce different formats (compact grid, markdown, flat list).
/// </summary>
public interface IStateRenderer
{
    string Render(GameState state);
}
