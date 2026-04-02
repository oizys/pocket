using System.Collections.Immutable;
using Pockets.Core.Models;

namespace Pockets.Core.Dsl;

/// <summary>
/// Parses DSL source text into a list of ProgramNodes.
/// Tokens are whitespace-separated. All blocks use [ ] quotation syntax.
/// Combinators are postfix and consume quotations from the preceding nodes.
///   - LocationIds (H, T, B, W, C) → pushed as values
///   - Integers → pushed as values
///   - [ ... ] → QuotationNode (pushed, not executed)
///   - try, if-ok, each → postfix combinators (consume preceding quotation)
///   - times → postfix combinator (consumes preceding quotation + int)
///   - :def name ... ; → macro definition (expanded inline)
///   - Everything else → OpNode
/// </summary>
public static class DslParser
{
    private static readonly HashSet<string> LocationNames = new(StringComparer.OrdinalIgnoreCase)
        { "H", "T", "B", "W", "C" };

    /// <summary>
    /// Parses a DSL source string into a list of program nodes.
    /// Macro definitions are collected and expanded inline.
    /// </summary>
    public static ImmutableArray<ProgramNode> Parse(string source)
    {
        var tokens = Tokenize(source);
        var macros = new Dictionary<string, ImmutableArray<ProgramNode>>();
        var result = ParseBlock(tokens, 0, out _, macros);
        return result;
    }

    private static List<string> Tokenize(string source)
    {
        var tokens = new List<string>();
        var i = 0;
        while (i < source.Length)
        {
            if (char.IsWhiteSpace(source[i]))
            {
                i++;
                continue;
            }

            // Single-char tokens
            if (source[i] is '[' or ']' or ';')
            {
                tokens.Add(source[i].ToString());
                i++;
                continue;
            }

            // Word token
            var start = i;
            while (i < source.Length && !char.IsWhiteSpace(source[i]) && source[i] is not ('[' or ']' or ';'))
                i++;
            tokens.Add(source[start..i]);
        }
        return tokens;
    }

    private static ImmutableArray<ProgramNode> ParseBlock(
        List<string> tokens, int start, out int end, Dictionary<string, ImmutableArray<ProgramNode>> macros,
        string? terminator = null)
    {
        var nodes = ImmutableArray.CreateBuilder<ProgramNode>();
        var i = start;

        while (i < tokens.Count)
        {
            var token = tokens[i];

            // Block terminators
            if (token == terminator || token == "]" || token == ";")
            {
                end = i + 1;
                return nodes.ToImmutable();
            }

            // Quotation: [ ... ]
            if (token == "[")
            {
                var body = ParseBlock(tokens, i + 1, out var blockEnd, macros, "]");
                nodes.Add(new QuotationNode(body));
                i = blockEnd;
                continue;
            }

            // Postfix combinators: consume preceding quotation
            if (token is "try" or "if-ok" or "each")
            {
                if (nodes.Count >= 1 && nodes[^1] is QuotationNode q)
                {
                    nodes.RemoveAt(nodes.Count - 1);
                    ProgramNode node = token switch
                    {
                        "try" => new TryNode(q.Body),
                        "if-ok" => new IfOkNode(q.Body),
                        "each" => new EachNode(q.Body),
                        _ => throw new InvalidOperationException()
                    };
                    nodes.Add(node);
                }
                else
                {
                    throw new InvalidOperationException(
                        $"'{token}' requires a preceding [ ] quotation");
                }
                i++;
                continue;
            }

            // times: consumes preceding quotation (and int before it)
            // [ body ] N times → push_int(N), TimesNode(body)
            if (token == "times")
            {
                if (nodes.Count >= 2
                    && nodes[^1] is OpNode intPush && intPush.Name == "__push_int"
                    && nodes[^2] is QuotationNode q)
                {
                    nodes.RemoveAt(nodes.Count - 1); // remove push_int
                    nodes.RemoveAt(nodes.Count - 1); // remove quotation
                    nodes.Add(intPush); // re-add push_int before TimesNode
                    nodes.Add(new TimesNode(q.Body));
                }
                else if (nodes.Count >= 1 && nodes[^1] is QuotationNode q2)
                {
                    nodes.RemoveAt(nodes.Count - 1);
                    nodes.Add(new TimesNode(q2.Body));
                }
                else
                {
                    throw new InvalidOperationException(
                        "'times' requires a preceding [ ] quotation");
                }
                i++;
                continue;
            }

            // Macro definition: :def name ... ;
            if (token == ":def")
            {
                if (i + 1 >= tokens.Count)
                    throw new InvalidOperationException("Expected macro name after :def");
                var macroName = tokens[i + 1];
                var body = ParseBlock(tokens, i + 2, out var blockEnd, macros, ";");
                macros[macroName] = body;
                nodes.Add(new DefNode(macroName, body));
                i = blockEnd;
                continue;
            }

            // LocationId as value push
            if (LocationNames.Contains(token.ToUpperInvariant()))
            {
                var locId = Enum.Parse<LocationId>(token.ToUpperInvariant());
                nodes.Add(new OpNode("__push_location", ImmutableArray.Create<object>(locId)));
                i++;
                continue;
            }

            // Integer literal
            if (int.TryParse(token, out var intVal))
            {
                nodes.Add(new OpNode("__push_int", ImmutableArray.Create<object>(intVal)));
                i++;
                continue;
            }

            // Macro expansion
            if (macros.TryGetValue(token, out var macroBody))
            {
                nodes.AddRange(macroBody);
                i++;
                continue;
            }

            // Regular opcode
            nodes.Add(new OpNode(token));
            i++;
        }

        end = tokens.Count;
        return nodes.ToImmutable();
    }
}
