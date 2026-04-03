using System.Collections.Immutable;

namespace Pockets.Core.Dsl;

/// <summary>
/// Base type for parsed DSL program elements.
/// </summary>
public abstract record ProgramNode;

/// <summary>
/// An opcode invocation with optional inline immediate values (ints, strings).
/// </summary>
public sealed record OpNode(string Name, ImmutableArray<object> Immediates) : ProgramNode
{
    public OpNode(string name) : this(name, ImmutableArray<object>.Empty) { }
}

/// <summary>
/// A quotation (deferred block of code pushed as a value).
/// </summary>
public sealed record QuotationNode(ImmutableArray<ProgramNode> Body) : ProgramNode;

/// <summary>
/// Pops an int from the stack, runs Body that many times.
/// </summary>
public sealed record TimesNode(ImmutableArray<ProgramNode> Body) : ProgramNode;

/// <summary>
/// Runs Body, catches errors, pushes Ok/Err result.
/// </summary>
public sealed record TryNode(ImmutableArray<ProgramNode> Body) : ProgramNode;

/// <summary>
/// Pops a DslResult, runs Body only if it was Ok.
/// </summary>
public sealed record IfOkNode(ImmutableArray<ProgramNode> Body) : ProgramNode;

/// <summary>
/// Iterates over a collection (e.g. selected cells), running Body for each.
/// </summary>
public sealed record EachNode(ImmutableArray<ProgramNode> Body) : ProgramNode;

/// <summary>
/// Defines a named macro. Body is expanded inline at parse time.
/// </summary>
public sealed record DefNode(string Name, ImmutableArray<ProgramNode> Body) : ProgramNode;

/// <summary>
/// Pops a quotation of paired [test] [body] sub-quotations.
/// Runs each test; first to push true has its body executed.
/// </summary>
public sealed record CondNode(ImmutableArray<ProgramNode> Pairs) : ProgramNode;

/// <summary>
/// Pops a bool from the stack, runs Body if true.
/// </summary>
public sealed record WhenNode(ImmutableArray<ProgramNode> Body) : ProgramNode;

/// <summary>
/// Pops a bool from the stack, runs Body if false.
/// </summary>
public sealed record UnlessNode(ImmutableArray<ProgramNode> Body) : ProgramNode;

/// <summary>
/// Pops a bool, runs TrueBody if true, FalseBody if false.
/// Syntax: [ true-body ] [ false-body ] if-else
/// </summary>
public sealed record IfElseNode(ImmutableArray<ProgramNode> TrueBody, ImmutableArray<ProgramNode> FalseBody) : ProgramNode;

/// <summary>
/// Runs Test, pops bool; if true, runs Body and loops. If false, stops.
/// Breaks on error in OpResult or after MaxIterations (default 512).
/// Syntax: [ test ] [ body ] while
/// </summary>
public sealed record WhileNode(ImmutableArray<ProgramNode> Test, ImmutableArray<ProgramNode> Body, int MaxIterations = 512) : ProgramNode;

/// <summary>
/// Immediately exits the current Run/quotation/call. The OpResult stays on the stack.
/// </summary>
public sealed record BreakNode : ProgramNode
{
    public static readonly BreakNode Instance = new();
}
