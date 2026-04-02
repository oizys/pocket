using System.Collections.Immutable;
using Pockets.Core.Models;

namespace Pockets.Core.Dsl;

/// <summary>
/// Parses DSL source text into a list of ProgramNodes.
/// Tokens are whitespace-separated. Recognizes:
///   - LocationIds (H, T, B, W, C) → pushed as immediates on the preceding or next op
///   - Integers → pushed as immediates
///   - [ ... ] → QuotationNode
///   - { ... } times → TimesNode (sugar for [ ... ] N times)
///   - try { ... } → TryNode
///   - if-ok { ... } → IfOkNode
///   - each { ... } → EachNode
///   - :def name ... ; → DefNode (expanded inline)
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
            if (source[i] is '[' or ']' or '{' or '}' or ';')
            {
                tokens.Add(source[i].ToString());
                i++;
                continue;
            }

            // Word token
            var start = i;
            while (i < source.Length && !char.IsWhiteSpace(source[i]) && source[i] is not ('[' or ']' or '{' or '}' or ';'))
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
            if (token == terminator || token == "]" || token == "}" || token == ";")
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

            // Block-based combinators: try { ... }, if-ok { ... }, each { ... }
            if (token is "try" or "if-ok" or "each" && i + 1 < tokens.Count && tokens[i + 1] == "{")
            {
                var body = ParseBlock(tokens, i + 2, out var blockEnd, macros, "}");
                ProgramNode node = token switch
                {
                    "try" => new TryNode(body),
                    "if-ok" => new IfOkNode(body),
                    "each" => new EachNode(body),
                    _ => throw new InvalidOperationException()
                };
                nodes.Add(node);
                i = blockEnd;

                // Check for trailing "times" after }
                if (token != "try" && token != "if-ok" && token != "each"
                    && i < tokens.Count && tokens[i] == "times")
                {
                    i++; // skip "times"
                }
                continue;
            }

            // times combinator: [ body ] N times → push_int(N), TimesNode(body)
            if (token == "times")
            {
                // Look for pattern: QuotationNode, push_int(N) — restructure to push_int(N), TimesNode(body)
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
                    // N [ body ] times — quotation is last, int already pushed earlier
                    nodes.RemoveAt(nodes.Count - 1);
                    nodes.Add(new TimesNode(q2.Body));
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
                // If next token is an opcode, push as immediate on the op
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
