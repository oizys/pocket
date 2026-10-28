using Pockets.Core.Models;

namespace Pockets.Core.Sim;

/// <summary>
/// Facility crafting as a registered rule (Slice 1.4; moved from <c>Simulation.TickFacilities</c>): per
/// facility, in <see cref="Simulation.OrderedFacilities"/> order, consume inputs → count down → place
/// outputs (<see cref="FacilityLogic.Tick"/>). A transfer rule (it moves items between cells), and
/// event-skippable: the countdown is closed-form and completion is a discrete event to jump to
/// (Δ ≥ remaining ⇒ complete, then re-check inputs). Seedling wilderness generation is part of it, drawing
/// from the step's <see cref="SimStream.Craft"/> cursor.
/// </summary>
public sealed class FacilityCraftRule : ITickRule
{
    /// <summary>The registered instance.</summary>
    public static FacilityCraftRule Instance { get; } = new();

    private FacilityCraftRule() { }

    /// <inheritdoc />
    public RuleKind Kind => RuleKind.Transfer;

    /// <inheritdoc />
    public TimeClass Time => TimeClass.EventSkippable;

    /// <inheritdoc />
    public ImmutableArray<Type> EventKinds { get; } = ImmutableArray.Create(typeof(SimEvent.CraftCompleted));

    /// <summary>
    /// Ticks every facility bag. Each facility is applied to the state in turn, exactly as before: its bag is
    /// replaced, any new bags from the output factory are added, and its owning stack's <c>Progress</c> is
    /// written into the parent grid (through <see cref="ImmutableBagWriter"/>). Emits one
    /// <see cref="SimEvent.CraftCompleted"/> per completion. Output factories draw from
    /// <see cref="RuleContext.CraftRandom"/> in facility order.
    /// </summary>
    public GameState Apply(GameState state, RuleContext context)
    {
        if (context.Content.Recipes.IsDefaultOrEmpty)
            return state;

        var facilities = Simulation.OrderedFacilities(state).ToList();
        if (facilities.Count == 0)
            return state;

        var step = context.Step;
        foreach (var facility in facilities)
        {
            var facilityRecipes = context.Content.RecipesForFacility(facility.EnvironmentType, context.KnownRecipes);
            if (facilityRecipes.Count == 0)
                continue;

            // Read current progress from the owning ItemStack's properties (shared owner lookup).
            var owner = state.Store.GetOwnerOf(facility.Id);
            var ownerStack = owner is not null
                ? state.Store.GetById(owner.ParentBagId)?.Grid.GetCell(owner.CellIndex).Stack
                : null;
            int currentProgress = ownerStack?.GetInt("Progress") ?? 0;

            var wasCrafting = facility.FacilityState?.RecipeId;
            var (ticked, newProgress, newBags) = FacilityLogic.Tick(facility, currentProgress, facilityRecipes, context.CraftRandom);
            if (ticked == facility && newProgress == currentProgress)
                continue;

            // Detect craft completion: was crafting, now reset to null
            if (wasCrafting is not null && ticked.FacilityState?.RecipeId is null)
            {
                var facilityId = facility.Id;
                context.Emit(seq => new SimEvent.CraftCompleted(step, seq, facilityId, wasCrafting));
            }

            // Register any newly created bags from the output factory
            if (newBags.Count > 0)
                state = state with { Store = state.Store.AddRange(newBags) };

            state = state with { Store = ReplaceWithProgress(state.Store, facility.Id, ticked, owner, newProgress) };
        }

        return state;
    }

    /// <summary>
    /// Replaces the facility bag and sets <c>Progress</c> on its owning stack (the old
    /// <c>GameState.ReplaceBagById</c> with an owner transform, writing the parent cell through
    /// <see cref="IBagWriter"/>). <paramref name="owner"/> is the facility's owner, looked up on the store
    /// before the replacement, as <c>ReplaceBagById</c> did.
    /// </summary>
    private static BagStore ReplaceWithProgress(BagStore store, Guid facilityId, Bag ticked, BagOwnerInfo? owner, int progress)
    {
        store = store.Set(facilityId, ticked);
        if (owner is null)
            return store;

        var parent = store.GetById(owner.ParentBagId)!;
        var cell = parent.Grid.GetCell(owner.CellIndex);
        if (cell.Stack is null)
            return store;

        var writer = new ImmutableBagWriter(parent.Grid);
        writer.Set(owner.CellIndex, cell with { Stack = cell.Stack.WithProperty("Progress", new IntValue(progress)) });
        return store.Set(owner.ParentBagId, parent with { Grid = writer.Commit() });
    }
}
