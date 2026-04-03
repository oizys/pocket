using System.Collections.Immutable;
using System.Reflection;
using Pockets.Core.Models;

namespace Pockets.Core.Dsl;

/// <summary>
/// Executes DSL programs by dispatching opcodes to [Opcode]-decorated methods.
/// State flows through the stack as OpResult tokens — the interpreter is just a fold.
/// Dispatch table is built once via reflection at first use.
/// </summary>
public static class DslInterpreter
{
    private static ImmutableDictionary<string, OpcodeBinding>? _dispatch;
    private static readonly object _lock = new();

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
                builder[attr.Name] = new OpcodeBinding(method, attr);
            }
        }

        return builder.ToImmutable();
    }

    // ==================== Stack helpers ====================

    private static ImmutableStack<object> Push(ImmutableStack<object> stack, object value) =>
        stack.Push(value);

    private static (T Value, ImmutableStack<object> Stack) Pop<T>(ImmutableStack<object> stack)
    {
        if (stack.IsEmpty)
            throw new InvalidOperationException($"DSL stack underflow: expected {typeof(T).Name}");
        var value = stack.Peek();
        var popped = stack.Pop();
        if (value is T typed)
            return (typed, popped);
        throw new InvalidOperationException(
            $"DSL type error: expected {typeof(T).Name}, got {value?.GetType().Name ?? "null"}");
    }

    private static object? Peek(ImmutableStack<object> stack) =>
        stack.IsEmpty ? null : stack.Peek();

    /// <summary>
    /// Finds the topmost OpResult on the stack (may be under surface values).
    /// Returns it without removing it.
    /// </summary>
    private static OpResult FindOpResult(ImmutableStack<object> stack)
    {
        var current = stack;
        while (!current.IsEmpty)
        {
            if (current.Peek() is OpResult op)
                return op;
            current = current.Pop();
        }
        throw new InvalidOperationException("No OpResult on stack");
    }

    /// <summary>
    /// Pops the topmost OpResult from the stack, which may be under surface values.
    /// Returns the OpResult and the stack with surface values preserved above.
    /// </summary>
    private static (OpResult Result, ImmutableStack<object> Stack) PopOpResult(ImmutableStack<object> stack)
    {
        // If top is OpResult, simple pop
        if (!stack.IsEmpty && stack.Peek() is OpResult op)
            return (op, stack.Pop());

        // Surface values are on top — collect them, find OpResult, restore
        var surface = new List<object>();
        var current = stack;
        while (!current.IsEmpty)
        {
            if (current.Peek() is OpResult found)
            {
                var remaining = current.Pop();
                // Push surface values back
                foreach (var val in surface.AsEnumerable().Reverse())
                    remaining = remaining.Push(val);
                return (found, remaining);
            }
            surface.Add(current.Peek());
            current = current.Pop();
        }
        throw new InvalidOperationException("No OpResult on stack");
    }

    /// <summary>
    /// Replaces the topmost OpResult on the stack (may be under surface values).
    /// </summary>
    private static ImmutableStack<object> ReplaceOpResult(ImmutableStack<object> stack, OpResult newResult)
    {
        if (!stack.IsEmpty && stack.Peek() is OpResult)
            return stack.Pop().Push(newResult);

        var surface = new List<object>();
        var current = stack;
        while (!current.IsEmpty)
        {
            if (current.Peek() is OpResult)
            {
                var remaining = current.Pop().Push(newResult);
                foreach (var val in surface.AsEnumerable().Reverse())
                    remaining = remaining.Push(val);
                return remaining;
            }
            surface.Add(current.Peek());
            current = current.Pop();
        }
        throw new InvalidOperationException("No OpResult on stack");
    }

    // ==================== Execution ====================

    /// <summary>
    /// Executes a single program node against the stack.
    /// </summary>
    public static ImmutableStack<object> Execute(ImmutableStack<object> stack, ProgramNode node)
    {
        return node switch
        {
            OpNode op => ExecuteOp(stack, op),
            QuotationNode q => Push(stack, q),
            TimesNode t => ExecuteTimes(stack, t),
            TryNode t => ExecuteTry(stack, t),
            IfOkNode t => ExecuteIfOk(stack, t),
            EachNode e => ExecuteEach(stack, e),
            DefNode => stack, // macros expanded at parse time
            _ => throw new InvalidOperationException($"Unknown node: {node.GetType().Name}")
        };
    }

    /// <summary>
    /// Runs a sequence of program nodes as a left fold over the stack.
    /// </summary>
    public static ImmutableStack<object> Run(ImmutableStack<object> stack, IEnumerable<ProgramNode> program) =>
        program.Aggregate(stack, Execute);

    /// <summary>
    /// Convenience: parse and run a DSL string. Requires an OpResult on the stack.
    /// </summary>
    public static ImmutableStack<object> Run(ImmutableStack<object> stack, string program) =>
        Run(stack, DslParser.Parse(program));

    /// <summary>
    /// Convenience: run a DSL string from a game state. Returns the final OpResult.
    /// </summary>
    public static OpResult RunProgram(GameState state, string program)
    {
        var stack = ImmutableStack<object>.Empty.Push(OpResult.Initial(state));
        stack = Run(stack, program);
        return FindOpResult(stack);
    }

    private static ImmutableStack<object> ExecuteOp(ImmutableStack<object> stack, OpNode op)
    {
        // Pseudo-ops for pushing values
        if (op.Name == "__push_location" || op.Name == "__push_int")
        {
            foreach (var imm in op.Immediates)
                stack = Push(stack, imm);
            return stack;
        }

        // Push any immediates
        foreach (var imm in op.Immediates)
            stack = Push(stack, imm);

        if (!Dispatch.TryGetValue(op.Name, out var binding))
            throw new InvalidOperationException($"Unknown opcode: '{op.Name}'");

        // Pop the OpResult from the stack (may be under surface values like LocationIds)
        var (opResult, remaining) = PopOpResult(stack);

        // Pop LocationId overrides from remaining stack (in reverse param order)
        var (args, cleanStack) = binding.ResolveArgs(remaining, opResult.State);

        // Invoke the opcode
        var result = binding.Invoke(opResult, args);

        // Push the new OpResult back
        return Push(cleanStack, result);
    }

    private static ImmutableStack<object> ExecuteTimes(ImmutableStack<object> stack, TimesNode node)
    {
        var (count, popped) = Pop<int>(stack);
        var current = popped;
        for (int i = 0; i < count; i++)
            current = Run(current, node.Body);
        return current;
    }

    private static ImmutableStack<object> ExecuteTry(ImmutableStack<object> stack, TryNode node)
    {
        // Save the current OpResult for rollback
        var beforeResult = FindOpResult(stack);

        try
        {
            var result = Run(stack, node.Body);
            var afterResult = FindOpResult(result);

            if (afterResult.IsOk)
            {
                // Success: push true indicator on top
                return Push(result, true);
            }
            else
            {
                // Body accumulated errors: rollback state, clear errors, push false
                var rolledBack = ReplaceOpResult(result, afterResult.Chain(beforeResult.State).ClearErrors());
                return Push(rolledBack, false);
            }
        }
        catch (InvalidOperationException)
        {
            // Exception: rollback, push false
            return Push(stack, false);
        }
    }

    private static ImmutableStack<object> ExecuteIfOk(ImmutableStack<object> stack, IfOkNode node)
    {
        var (flag, popped) = Pop<bool>(stack);
        if (flag)
            return Run(popped, node.Body);
        return popped;
    }

    private static ImmutableStack<object> ExecuteEach(ImmutableStack<object> stack, EachNode node)
    {
        // For now, runs body once. Full selection support added when UI emits selections.
        return Run(stack, node.Body);
    }
}
