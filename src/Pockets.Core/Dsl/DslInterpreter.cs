using System.Collections.Immutable;
using System.Reflection;
using Pockets.Core.Models;

namespace Pockets.Core.Dsl;

/// <summary>
/// Executes DSL programs by dispatching opcodes to [Opcode]/[Query]-decorated methods.
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
                var opcodeAttr = method.GetCustomAttribute<OpcodeAttribute>();
                if (opcodeAttr is not null)
                {
                    builder[opcodeAttr.Name] = new OpcodeBinding(method, opcodeAttr);
                    continue;
                }

                var queryAttr = method.GetCustomAttribute<QueryAttribute>();
                if (queryAttr is not null)
                {
                    builder[queryAttr.Name] = new OpcodeBinding(method, queryAttr);
                }
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
    /// </summary>
    public static OpResult FindOpResult(ImmutableStack<object> stack)
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
    /// Pops the topmost OpResult, preserving surface values above it.
    /// </summary>
    private static (OpResult Result, ImmutableStack<object> Stack) PopOpResult(ImmutableStack<object> stack)
    {
        if (!stack.IsEmpty && stack.Peek() is OpResult op)
            return (op, stack.Pop());

        var surface = new List<object>();
        var current = stack;
        while (!current.IsEmpty)
        {
            if (current.Peek() is OpResult found)
            {
                var remaining = current.Pop();
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
    /// Replaces the topmost OpResult on the stack, preserving surface values above it.
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
            CondNode c => ExecuteCond(stack, c.Pairs),
            WhenNode w => ExecuteWhen(stack, w),
            UnlessNode u => ExecuteUnless(stack, u),
            DefNode => stack,
            _ => throw new InvalidOperationException($"Unknown node: {node.GetType().Name}")
        };
    }

    public static ImmutableStack<object> Run(ImmutableStack<object> stack, IEnumerable<ProgramNode> program) =>
        program.Aggregate(stack, Execute);

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
        if (op.Name is "__push_location" or "__push_int")
        {
            foreach (var imm in op.Immediates)
                stack = Push(stack, imm);
            return stack;
        }

        // Built-in logic/value ops (no reflection needed)
        switch (op.Name)
        {
            case "and": { var (b, s1) = Pop<bool>(stack); var (a, s2) = Pop<bool>(s1); return Push(s2, a && b); }
            case "or":  { var (b, s1) = Pop<bool>(stack); var (a, s2) = Pop<bool>(s1); return Push(s2, a || b); }
            case "not": { var (a, s1) = Pop<bool>(stack); return Push(s1, !a); }
            case "true":  return Push(stack, true);
            case "false": return Push(stack, false);
            case "lte": { var (b, s1) = Pop<int>(stack); var (a, s2) = Pop<int>(s1); return Push(s2, a <= b); }
            case "gte": { var (b, s1) = Pop<int>(stack); var (a, s2) = Pop<int>(s1); return Push(s2, a >= b); }
            case "eq":  { var (b, s1) = Pop<int>(stack); var (a, s2) = Pop<int>(s1); return Push(s2, a == b); }
        }

        // Push any immediates
        foreach (var imm in op.Immediates)
            stack = Push(stack, imm);

        if (!Dispatch.TryGetValue(op.Name, out var binding))
            throw new InvalidOperationException($"Unknown opcode: '{op.Name}'");

        if (binding.IsQuery)
        {
            // Queries peek at the OpResult without popping it, and push a value on top
            var opResult = FindOpResult(stack);
            var result = binding.Invoke(opResult, Array.Empty<ResolvedParam>());
            // Push only the Pushed values (query value), not the OpResult
            if (!result.Pushed.IsDefaultOrEmpty)
            {
                foreach (var val in result.Pushed)
                    stack = Push(stack, val);
            }
            return stack;
        }

        // Action opcodes: pop OpResult, resolve args, invoke, push result back
        var (actionOpResult, remaining) = PopOpResult(stack);
        var (args, cleanStack) = binding.ResolveArgs(remaining, actionOpResult.State);
        var actionResult = binding.Invoke(actionOpResult, args);

        // Push the OpResult back (without Pushed field), then unpack Pushed values
        var output = Push(cleanStack, actionResult with { Pushed = default });
        if (!actionResult.Pushed.IsDefaultOrEmpty)
        {
            foreach (var val in actionResult.Pushed)
                output = Push(output, val);
        }

        return output;
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
        var beforeResult = FindOpResult(stack);

        try
        {
            var result = Run(stack, node.Body);
            var afterResult = FindOpResult(result);

            if (afterResult.IsOk)
            {
                return Push(result, true);
            }
            else
            {
                var rolledBack = ReplaceOpResult(result, afterResult.Chain(beforeResult.State).ClearErrors());
                return Push(rolledBack, false);
            }
        }
        catch (InvalidOperationException)
        {
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
        return Run(stack, node.Body);
    }

    private static ImmutableStack<object> ExecuteWhen(ImmutableStack<object> stack, WhenNode node)
    {
        var (flag, popped) = Pop<bool>(stack);
        return flag ? Run(popped, node.Body) : popped;
    }

    private static ImmutableStack<object> ExecuteUnless(ImmutableStack<object> stack, UnlessNode node)
    {
        var (flag, popped) = Pop<bool>(stack);
        return flag ? popped : Run(popped, node.Body);
    }

    /// <summary>
    /// Executes a cond table: pops a QuotationNode containing paired [test] [body] quotations.
    /// Runs each test; if it pushes true, runs the corresponding body and stops.
    /// </summary>
    public static ImmutableStack<object> ExecuteCond(ImmutableStack<object> stack, ImmutableArray<ProgramNode> pairs)
    {
        // Pairs are consecutive: [test0] [body0] [test1] [body1] ...
        for (int i = 0; i + 1 < pairs.Length; i += 2)
        {
            var testQuot = pairs[i] as QuotationNode
                ?? throw new InvalidOperationException("cond: expected [test] quotation");
            var bodyQuot = pairs[i + 1] as QuotationNode
                ?? throw new InvalidOperationException("cond: expected [body] quotation");

            // Run the test — it should push a bool
            var afterTest = Run(stack, testQuot.Body);
            var (testResult, cleanStack) = Pop<bool>(afterTest);

            if (testResult)
            {
                // Run the body and return
                return Run(cleanStack, bodyQuot.Body);
            }
            // Test failed — continue with the stack as it was before the test
            // (test should be pure / non-mutating, but we use cleanStack to consume the bool)
        }

        // No test matched — return stack unchanged
        return stack;
    }
}
