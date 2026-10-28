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
    public ImmutableArray<Type> EventKinds { get; } = ImmutableArray<Type>.Empty;

    /// <inheritdoc />
    public GameState Apply(GameState state, RuleContext context) => PlantLogic.TickPlants(state);
}
