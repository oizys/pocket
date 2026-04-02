using System.Collections.Immutable;
using System.Reflection;
using Pockets.Core.Models;

namespace Pockets.Core.Dsl;

/// <summary>
/// Cached metadata for one parameter of an opcode method.
/// </summary>
public record ParamBinding(
    string Name,
    AccessLevel Level,
    bool Source,
    bool Target,
    LocationId? DefaultLocation,
    Type ParameterType);

/// <summary>
/// Cached reflection wrapper for a single [Opcode]-decorated method.
/// Built once at startup, invoked many times. Handles argument resolution
/// (from data stack or defaults), coercion, invocation, and write-back.
/// </summary>
public class OpcodeBinding
{
    public string Name { get; }
    public LocationId? DefaultLocation { get; }
    public ImmutableArray<ParamBinding> Params { get; }
    private readonly MethodInfo _method;

    public OpcodeBinding(MethodInfo method, OpcodeAttribute attr)
    {
        _method = method;
        Name = attr.Name;
        DefaultLocation = (int)attr.DefaultLocation >= 0 ? attr.DefaultLocation : null;

        Params = method.GetParameters()
            .Where(p => p.GetCustomAttribute<ParamAttribute>() is not null)
            .Select(p =>
            {
                var pa = p.GetCustomAttribute<ParamAttribute>()!;
                return new ParamBinding(
                    p.Name!,
                    pa.Level,
                    pa.Source,
                    pa.Target,
                    pa.HasDefaultLocation ? pa.DefaultLocation : null,
                    p.ParameterType);
            })
            .ToImmutableArray();
    }

    /// <summary>
    /// Resolves arguments for this opcode from the data stack and/or defaults,
    /// coerces each to the expected access level, and returns the resolved params.
    /// </summary>
    public (ResolvedParam[] Args, DslState State) ResolveArgs(DslState state)
    {
        var args = new ResolvedParam[Params.Length];

        // Process params in reverse order (rightmost popped first from stack)
        var currentState = state;
        for (int i = Params.Length - 1; i >= 0; i--)
        {
            var param = Params[i];

            // Try to get LocationId from stack, or use default
            LocationId locId;
            if (!currentState.IsStackEmpty && currentState.Peek() is LocationId stackLocId)
            {
                var (_, newState) = currentState.Pop<LocationId>();
                currentState = newState;
                locId = stackLocId;
            }
            else if (param.DefaultLocation is { } defLoc)
            {
                locId = defLoc;
            }
            else if (DefaultLocation is { } opDefLoc)
            {
                locId = opDefLoc;
            }
            else
            {
                throw new InvalidOperationException(
                    $"Opcode '{Name}' param '{param.Name}': no LocationId on stack and no default");
            }

            args[i] = Coercion.Resolve(locId, param.Level, currentState.Game);
        }

        return (args, currentState);
    }

    /// <summary>
    /// Invokes the opcode method with the resolved arguments.
    /// The first parameter is always DslState; subsequent [Param]-decorated
    /// parameters receive the coerced values.
    /// </summary>
    public DslResult Invoke(DslState state, ResolvedParam[] args)
    {
        var methodParams = _method.GetParameters();
        var invokeArgs = new object[methodParams.Length];

        // First param is always DslState
        invokeArgs[0] = state;

        // Map resolved values to method parameters
        int argIdx = 0;
        for (int i = 1; i < methodParams.Length; i++)
        {
            if (methodParams[i].GetCustomAttribute<ParamAttribute>() is not null)
            {
                invokeArgs[i] = args[argIdx].Value;
                argIdx++;
            }
        }

        return (DslResult)_method.Invoke(null, invokeArgs)!;
    }

    /// <summary>
    /// Applies write-back for Source and Target parameters. Sources are cleared,
    /// targets are updated with new values from the result state.
    /// </summary>
    public GameState ApplyWriteBack(GameState state, DslResult result, ResolvedParam[] args)
    {
        // The opcode method already updated the state internally.
        // Write-back is handled by the opcode itself since it has full context.
        // This method exists as a hook for future generic write-back logic.
        return result.State;
    }
}
