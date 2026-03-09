namespace Pockets.Core.Models;

/// <summary>
/// Project-level game configuration. Controls defaults like max stack size.
/// Use small values (e.g. 9) for compact test/feedback scenarios.
/// </summary>
public record GameConfig(
    int DefaultMaxStackSize = 20);
