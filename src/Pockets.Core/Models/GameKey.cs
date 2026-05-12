namespace Pockets.Core.Models;

/// <summary>
/// Abstract input keys for game actions, decoupled from any UI framework.
/// Maps from Terminal.Gui KeyEvent or other input sources to these logical keys.
/// </summary>
public enum GameKey
{
    Up,
    Down,
    Left,
    Right,
    Primary,        // Key 1 / E — grab/drop/swap/enter bag
    Secondary,      // Key 2 — half-grab / place one
    QuickSplit,     // Key 3
    Sort,           // Key 4
    AcquireRandom,  // Key 5 (debug)
    CycleRecipe,    // R
    LeaveBag,       // Q
    Undo,           // Ctrl-Z
    FocusNext,      // Tab
    FocusPrev,      // Shift-Tab
    BeginSplit,     // Shift-3 / # — enter inline split mode for cursor cell
    Confirm,        // Enter — commits inline split / other context-confirm verbs
    Cancel          // Esc — cancels inline split / other context-cancel verbs
}

/// <summary>
/// Mouse click type for grid cell interactions.
/// </summary>
public enum ClickType
{
    Primary,    // Left click — grab/drop/swap/enter
    Secondary   // Right click — half-grab / place one
}
