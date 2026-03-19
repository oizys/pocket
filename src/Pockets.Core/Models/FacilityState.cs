namespace Pockets.Core.Models;

/// <summary>
/// Tracks crafting state for a facility bag. Attached to Bag as optional state.
/// Progress is stored as an ItemProperty ("Progress") on the owning ItemStack, not here.
/// When a facility has matching inputs and is active, the owning stack's Progress increments each tick.
/// On completion (Progress >= recipe Duration), inputs are consumed and output is produced.
/// </summary>
public record FacilityState(
    string? RecipeId = null,
    bool IsActive = true,
    string? ActiveRecipeId = null);
