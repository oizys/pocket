using Pockets.Core.Models;

namespace Pockets.Core.Sim;

/// <summary>
/// Plant growth (<see cref="PlantLogic.TickPlants"/>) as a registered rule. A field rule on its own cell
/// only (no neighbours), and closed-form: Δ steps of growth is <c>min(Progress + Δ, Duration)</c>.
/// </summary>
public sealed class PlantGrowthRule : ITickRule
{
    /// <summary>The registered instance.</summary>
    public static PlantGrowthRule Instance { get; } = new();

    private PlantGrowthRule() { }

    /// <inheritdoc />
    public RuleKind Kind => RuleKind.Field;

    /// <inheritdoc />
    public TimeClass Time => TimeClass.ClosedForm;

    /// <inheritdoc />
    public ImmutableArray<Type> EventKinds { get; } = ImmutableArray.Create(typeof(SimEvent.PlantGrew));

    /// <summary>
    /// Grows every plant one step (<see cref="PlantLogic.TickPlants"/>) and emits one
    /// <see cref="SimEvent.PlantGrew"/> per plant that grew, bags in canonical order, cells by index.
    /// </summary>
    public GameState Apply(GameState state, RuleContext context)
    {
        var next = PlantLogic.TickPlants(state);
        if (ReferenceEquals(next.Store, state.Store))
            return next;

        var grown = next.Store.Entries
            .Where(e => state.Store.GetById(e.Key) is { } old && !ReferenceEquals(old.Grid, e.Value.Grid))
            .Select(e => e.Key)
            .ToList();
        var step = context.Step;
        foreach (var id in EventDeriver.CanonicalOrder(next, grown))
        {
            var (a, b) = (state.Store.GetById(id)!.Grid, next.Store.GetById(id)!.Grid);
            for (var i = 0; i < b.Cells.Length; i++)
            {
                if (a.Cells[i] == b.Cells[i] || b.Cells[i].Stack is not { } plant) continue;
                var cell = new CellRef(id, i);
                var (stage, of) = (plant.GetInt("Progress") ?? 0, plant.GetInt("Duration") ?? 0);
                context.Emit(seq => new SimEvent.PlantGrew(step, seq, cell, stage, of));
            }
        }
        return next;
    }
}
