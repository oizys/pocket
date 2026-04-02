using System.Collections.Immutable;
using System.Reflection;
using Pockets.Core.Models;

namespace Pockets.Core.Dsl;

/// <summary>
/// Executes DSL programs by dispatching opcodes to [Opcode]-decorated methods.
/// Dispatch table is built once via reflection at first use.
/// Run is a left fold over program nodes.
/// </summary>
public static class DslInterpreter
{
    private static ImmutableDictionary<string, OpcodeBinding>? _dispatch;
    private static readonly object _lock = new();

    /// <summary>
    /// Gets the dispatch table, building it on first access.
    /// </summary>
    public static ImmutableDictionary<string, OpcodeBinding> Dispatch
    {
        get
        {
            if (_dispatch is not null) return _dispatch;
            lock (_lock)
            {
                _dispatch ??= BuildDispatch();
                return _dispatch;
            }
        }
    }

    /// <summary>
    /// Scans all types in the Pockets.Core assembly for [Opcode]-decorated methods
    /// and builds a dispatch dictionary keyed by opcode name.
    /// </summary>
    private static ImmutableDictionary<string, OpcodeBinding> BuildDispatch()
    {
        var builder = ImmutableDictionary.CreateBuilder<string, OpcodeBinding>();
        var assembly = typeof(DslInterpreter).Assembly;

        foreach (var type in assembly.GetTypes())
        {
            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Static))
            {
                var attr = method.GetCustomAttribute<OpcodeAttribute>();
                if (attr is null) continue;

                var binding = new OpcodeBinding(method, attr);
                builder[attr.Name] = binding;
            }
        }

        return builder.ToImmutable();
    }

    /// <summary>
    /// Executes a single program node against the current state.
    /// </summary>
    public static DslState Execute(DslState state, ProgramNode node)
    {
        return node switch
        {
            OpNode op => ExecuteOp(state, op),
            QuotationNode q => state.Push(q),
            TimesNode t => ExecuteTimes(state, t),
            TryNode t => ExecuteTry(state, t),
            IfOkNode t => ExecuteIfOk(state, t),
            EachNode e => ExecuteEach(state, e),
            DefNode => state, // macros are expanded at parse time, no-op at runtime
            _ => throw new InvalidOperationException($"Unknown program node: {node.GetType().Name}")
        };
    }

    /// <summary>
    /// Executes a sequence of program nodes as a left fold.
    /// </summary>
    public static DslState Run(DslState state, IEnumerable<ProgramNode> program) =>
        program.Aggregate(state, Execute);

    /// <summary>
    /// Convenience: parse and execute a DSL string.
    /// </summary>
    public static DslState Run(DslState state, string program) =>
        Run(state, DslParser.Parse(program));

    /// <summary>
    /// Executes an opcode node by looking up its binding and dispatching.
    /// </summary>
    private static DslState ExecuteOp(DslState state, OpNode op)
    {
        // Pseudo-ops for pushing values onto the stack
        if (op.Name == "__push_location" || op.Name == "__push_int")
        {
            foreach (var imm in op.Immediates)
                state = state.Push(imm);
            return state;
        }

        // Handle immediate value pushes (ints, strings, LocationIds)
        foreach (var imm in op.Immediates)
            state = state.Push(imm);

        if (!Dispatch.TryGetValue(op.Name, out var binding))
            throw new InvalidOperationException($"Unknown opcode: '{op.Name}'");

        var (args, resolvedState) = binding.ResolveArgs(state);
        var result = binding.Invoke(resolvedState, args);
        var finalGameState = binding.ApplyWriteBack(resolvedState.Game, result, args);

        // Update game state but don't push result onto stack by default.
        // Results are only pushed in try blocks (see ExecuteTry).
        return resolvedState with { Game = finalGameState };
    }

    private static DslState ExecuteTimes(DslState state, TimesNode node)
    {
        var (count, popped) = state.Pop<int>();
        var current = popped;
        for (int i = 0; i < count; i++)
            current = Run(current, node.Body);
        return current;
    }

    private static DslState ExecuteTry(DslState state, TryNode node)
    {
        try
        {
            var result = Run(state, node.Body);
            return result.Push(DslResult.Ok(result.Game));
        }
        catch (InvalidOperationException ex)
        {
            return state.Push(DslResult.Fail(state.Game, ex.Message));
        }
    }

    private static DslState ExecuteIfOk(DslState state, IfOkNode node)
    {
        var (result, popped) = state.Pop<DslResult>();
        if (result.Success)
            return Run(popped, node.Body);
        return popped;
    }

    private static DslState ExecuteEach(DslState state, EachNode node)
    {
        // Each pops a list of positions and runs the body for each,
        // temporarily moving the cursor. For now, just runs body once.
        // Full selection support will be added when the UI emits selections.
        return Run(state, node.Body);
    }
}
