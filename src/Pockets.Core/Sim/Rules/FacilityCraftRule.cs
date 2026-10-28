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
    public ImmutableArray<Type> EventKinds { get; } = ImmutableArray.Create(
        typeof(SimEvent.CraftStarted), typeof(SimEvent.CraftProgressed),
        typeof(SimEvent.CraftAborted), typeof(SimEvent.CraftCompleted));

    /// <summary>
    /// Ticks every facility bag. Each facility is applied to the state in turn, exactly as before: its bag is
    /// replaced, any new bags from the output factory are added, and its owning stack's <c>Progress</c> is
    /// written into the parent grid (through <see cref="ImmutableBagWriter"/>). Emits, per facility that
    /// changed: <see cref="SimEvent.CraftStarted"/>, <see cref="SimEvent.CraftProgressed"/> (a countdown step),
    /// <see cref="SimEvent.CraftAborted"/> (inputs no longer match) or <see cref="SimEvent.CraftCompleted"/>
    /// (with its consumed input cells and placed outputs). Output factories draw from
    /// <see cref="RuleContext.CraftRandom"/> in facility order.
    /// <para>
    /// Payloads are built lazily, inside the <see cref="RuleContext.Emit"/> factory (bags are immutable, so
    /// the result is the same), and every captured local is declared inside the branch that emits: a
    /// countdown pass allocates only its one event factory (the allocation bound of Slice 1.4).
    /// </para>
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

            Emit(context, step, facility, ticked, wasCrafting, owner, newProgress, facilityRecipes);

            // Register any newly created bags from the output factory
            if (newBags.Count > 0)
                state = state with { Store = state.Store.AddRange(newBags) };

            state = state with { Store = ReplaceWithProgress(state.Store, facility.Id, ticked, owner, newProgress) };
        }

        return state;
    }

    /// <summary>
    /// Emits the event for one facility that changed this step (see <see cref="Apply"/>). The facility's
    /// owner cell is named on every event, since that is where its progress is written. Kept as its own
    /// method so the factories' captured state is allocated only on the branch taken.
    /// </summary>
    private static void Emit(
        RuleContext context, int step, Bag facility, Bag ticked, string? wasCrafting, BagOwnerInfo? owner,
        int newProgress, IReadOnlyList<Recipe> recipes)
    {
        var now = ticked.FacilityState?.RecipeId;
        if (wasCrafting is null)
        {
            if (now is not null)
                context.Emit(seq => new SimEvent.CraftStarted(step, seq, facility.Id, now, Cell(owner)));
            return;
        }

        if (now is null)
        {
            // Completion consumes inputs, so the grid changed; an abort only clears the recipe.
            if (ReferenceEquals(ticked.Grid, facility.Grid))
                context.Emit(seq => new SimEvent.CraftAborted(step, seq, facility.Id, wasCrafting, Cell(owner)));
            else
                context.Emit(seq => Completed(step, seq, facility, ticked, wasCrafting, owner));
            return;
        }

        context.Emit(seq => new SimEvent.CraftProgressed(step, seq, facility.Id, wasCrafting,
            Remaining(recipes, wasCrafting, newProgress), Cell(owner)));
    }

    /// <summary>Steps left on a craft of <paramref name="recipeId"/> at <paramref name="progress"/>.</summary>
    private static int Remaining(IReadOnlyList<Recipe> recipes, string recipeId, int progress)
    {
        foreach (var r in recipes)
            if (r.Id == recipeId)
                return r.Duration - progress;
        return 0;
    }

    /// <summary>The owner's cell, or null for an unowned facility.</summary>
    private static CellRef? Cell(BagOwnerInfo? owner) =>
        owner is null ? null : new CellRef(owner.ParentBagId, owner.CellIndex);

    /// <summary>
    /// The completion event: input cells whose stack shrank or emptied are <c>Consumed</c>; output-slot cells
    /// that gained a stack are the <c>Outputs</c>.
    /// </summary>
    private static SimEvent.CraftCompleted Completed(int step, int seq, Bag before, Bag after, string recipeId, BagOwnerInfo? owner)
    {
        var consumed = ImmutableArray.CreateBuilder<CellRef>();
        var outputs = ImmutableArray.CreateBuilder<Placed>();
        for (var i = 0; i < after.Grid.Cells.Length; i++)
        {
            var (a, b) = (before.Grid.Cells[i], after.Grid.Cells[i]);
            if (a == b) continue;
            if (b.Frame is OutputSlotFrame && b.Stack is { } made)
                outputs.Add(new Placed(made.ItemType, made.Count, new CellRef(before.Id, i)));
            else
                consumed.Add(new CellRef(before.Id, i));
        }
        return new SimEvent.CraftCompleted(step, seq, before.Id, recipeId, outputs.ToImmutable(), consumed.ToImmutable(), Cell(owner));
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
