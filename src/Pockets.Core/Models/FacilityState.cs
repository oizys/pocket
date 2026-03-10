namespace Pockets.Core.Models;

/// <summary>
/// Tracks crafting state for a facility bag. Attached to Bag as optional state.
/// When a facility has matching inputs and is active, Progress increments each tick.
/// On completion (Progress >= recipe Duration), inputs are consumed and output is produced.
/// </summary>
public record FacilityState(
    string? RecipeId = null,
    int Progress = 0,
    bool IsActive = true);
