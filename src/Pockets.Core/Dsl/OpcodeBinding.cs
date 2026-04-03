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
/// Cached reflection wrapper for a single [Opcode] or [Query]-decorated method.
/// Built once at startup, invoked many times.
///
/// Opcodes: (OpResult, coerced args) → OpResult
/// Queries: (GameState) → object (wrapped into OpResult.WithPushed automatically)
/// </summary>
public class OpcodeBinding
{
    public string Name { get; }
    public LocationId? DefaultLocation { get; }
    public bool IsQuery { get; }
    public ImmutableArray<ParamBinding> Params { get; }
    private readonly MethodInfo _method;

    /// <summary>
    /// Creates a binding for an [Opcode]-decorated method.
    /// </summary>
    public OpcodeBinding(MethodInfo method, OpcodeAttribute attr)
    {
        _method = method;
        Name = attr.Name;
        DefaultLocation = (int)attr.DefaultLocation >= 0 ? attr.DefaultLocation : null;
        IsQuery = false;

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
    /// Creates a binding for a [Query]-decorated method.
    /// </summary>
    public OpcodeBinding(MethodInfo method, QueryAttribute attr)
    {
        _method = method;
        Name = attr.Name;
        DefaultLocation = null;
        IsQuery = true;
        Params = ImmutableArray<ParamBinding>.Empty; // queries take no [Param] args
    }

    /// <summary>
    /// Resolves arguments from the stack and/or defaults, coercing each to the expected
    /// access level. Queries have no params so this is a no-op for them.
    /// </summary>
    public (ResolvedParam[] Args, ImmutableStack<object> Stack) ResolveArgs(
        ImmutableStack<object> stack, GameState state)
    {
        if (IsQuery)
            return (Array.Empty<ResolvedParam>(), stack);

        var args = new ResolvedParam[Params.Length];
        var current = stack;

        for (int i = Params.Length - 1; i >= 0; i--)
        {
            var param = Params[i];

            LocationId locId;
            if (!current.IsEmpty && current.Peek() is LocationId stackLocId)
            {
                current = current.Pop();
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

            args[i] = Coercion.Resolve(locId, param.Level, state);
        }

        return (args, current);
    }

    /// <summary>
    /// Invokes the method. For opcodes: (OpResult, coerced args) → OpResult.
    /// For queries: (GameState) → object, wrapped into OpResult.WithPushed.
    /// </summary>
    public OpResult Invoke(OpResult input, ResolvedParam[] args)
    {
        if (IsQuery)
        {
            var queryResult = _method.Invoke(null, new object[] { input.State })!;
            return input.WithPushed(queryResult);
        }

        var methodParams = _method.GetParameters();
        var invokeArgs = new object[methodParams.Length];

        // First param is always OpResult
        invokeArgs[0] = input;

        int argIdx = 0;
        for (int i = 1; i < methodParams.Length; i++)
        {
            if (methodParams[i].GetCustomAttribute<ParamAttribute>() is not null)
            {
                invokeArgs[i] = args[argIdx].Value;
                argIdx++;
            }
        }

        return (OpResult)_method.Invoke(null, invokeArgs)!;
    }
}
