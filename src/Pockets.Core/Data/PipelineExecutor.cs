using System.Collections.Immutable;
using Pockets.Core.Models;

namespace Pockets.Core.Data;

/// <summary>
/// Executes a list of PipelineSteps left-to-right, threading a PipelineValue through each step.
/// StaticItemStep produces or appends ItemStacks, TemplateRefStep resolves a template by id,
/// and GeneratorStep calls a registered generator function with the current value and resolved args.
/// </summary>
public static class PipelineExecutor
{
    /// <summary>
    /// Executes the pipeline and returns the final PipelineValue.
    /// Throws InvalidOperationException if the pipeline is empty (no steps produce a value).
    /// </summary>
    public static PipelineValue Execute(
        IReadOnlyList<PipelineStep> steps,
        ImmutableDictionary<string, ItemType> items,
        ImmutableDictionary<string, object> templates,
        ImmutableDictionary<string, GeneratorFunc> generators)
    {
        PipelineValue? current = null;

        foreach (var step in steps)
        {
            current = step switch
            {
                StaticItemStep s => ExecuteStaticItem(s, items, current),
                TemplateRefStep t => new TemplateValue(t.TemplateId, templates[t.TemplateId]),
                GeneratorStep g => ExecuteGenerator(g, templates, generators, current),
                _ => throw new InvalidOperationException($"Unknown step type: {step.GetType().Name}")
            };
        }

        return current ?? throw new InvalidOperationException("Pipeline produced no value: steps list is empty.");
    }

    private static StacksValue ExecuteStaticItem(
        StaticItemStep step,
        ImmutableDictionary<string, ItemType> items,
        PipelineValue? current)
    {
        var itemType = items[step.ItemName];
        var newStack = new ItemStack(itemType, step.Count);

        if (current is StacksValue existing)
            return new StacksValue(existing.Stacks.Append(newStack).ToList());

        return new StacksValue(new[] { newStack });
    }

    private static PipelineValue ExecuteGenerator(
        GeneratorStep step,
        ImmutableDictionary<string, object> templates,
        ImmutableDictionary<string, GeneratorFunc> generators,
        PipelineValue? current)
    {
        var resolvedArgs = step.TemplateArgs
            .Select(argId => templates[argId])
            .ToList<object>();

        return generators[step.GeneratorId](current, resolvedArgs);
    }
}
