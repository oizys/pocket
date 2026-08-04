namespace Pockets.Core.Models;

/// <summary>
/// Tracks crafting state for a facility bag. Attached to Bag as optional state.
/// Progress is stored as an ItemProperty ("Progress") on the owning ItemStack, not here.
/// When a facility has matching inputs and is active, the owning stack's Progress increments each tick.
/// On completion (Progress >= recipe Duration), inputs are consumed and output is produced.
/// </summary>
/// <param name="RequiresSelectedRecipe">
/// When true the facility is a <b>manual assembler</b> (the demo Crafting Table): it crafts ONLY the
/// recipe explicitly selected via the modal recipe menu (<see cref="ActiveRecipeId"/>), and never
/// auto-scans its inputs for a match. This is what makes the empty starting table inert until the
/// player learns a recipe and picks it — no "ingredients pre-sat there → instant craft" surprise
/// (playtest fix, 2026-08-04). Auto-scanning facilities (the Stage-3 Workshop) leave it false.
/// </param>
public record FacilityState(
    string? RecipeId = null,
    bool IsActive = true,
    string? ActiveRecipeId = null,
    bool RequiresSelectedRecipe = false);
