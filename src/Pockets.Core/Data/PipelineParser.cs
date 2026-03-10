using Pockets.Core.Models;

namespace Pockets.Core.Data;

/// <summary>
/// Parses a recipe output pipeline string into a list of PipelineStep AST nodes.
/// Syntax: steps separated by " -> ", each step is a static item, template ref, or generator.
/// </summary>
public static class PipelineParser
{
    /// <summary>
    /// Parses the given output pipeline string and returns the ordered list of steps.
    /// Steps are separated by " -> " (with optional surrounding whitespace).
    /// </summary>
    public static IReadOnlyList<PipelineStep> Parse(string output) =>
        output
            .Split("->")
            .Select(s => s.Trim())
            .Where(s => s.Length > 0)
            .Select(ParseStep)
            .ToList();

    private static PipelineStep ParseStep(string step)
    {
        if (step.StartsWith('@'))
            return new TemplateRefStep(step[1..].Trim());

        if (step.StartsWith('!'))
            return ParseGeneratorStep(step[1..]);

        return ParseStaticItemStep(step);
    }

    private static GeneratorStep ParseGeneratorStep(string body)
    {
        var parenIndex = body.IndexOf('(');
        if (parenIndex < 0)
            return new GeneratorStep(body.Trim(), Array.Empty<string>());

        var generatorId = body[..parenIndex].Trim();
        var argsContent = body[(parenIndex + 1)..body.LastIndexOf(')')];
        var templateArgs = argsContent
            .Split(',')
            .Select(a => a.Trim())
            .Where(a => a.StartsWith('@'))
            .Select(a => a[1..].Trim())
            .ToList();

        return new GeneratorStep(generatorId, templateArgs);
    }

    private static StaticItemStep ParseStaticItemStep(string step)
    {
        var spaceIndex = step.IndexOf(' ');
        var count = int.Parse(step[..spaceIndex]);
        var itemName = step[(spaceIndex + 1)..].Trim();
        return new StaticItemStep(itemName, count);
    }
}
