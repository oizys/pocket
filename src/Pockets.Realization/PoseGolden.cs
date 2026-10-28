using System.Globalization;

namespace Pockets.Realization;

/// <summary>
/// The serialized form of the pose golden (Slice 2.4): each line of commit k within journey step s is prefixed
/// <c>{s}:{k}</c>, k being the 0-based ordinal of the commit in the step (an empty commit still counts).
/// </summary>
public static class PoseGolden
{
    /// <summary>Prefixes every line of every commit with <c>{journeyStep}:{k} </c>, invariant culture.</summary>
    public static IReadOnlyList<string> Lines(int journeyStep, IEnumerable<IReadOnlyList<string>> commits)
    {
        var prefix = journeyStep.ToString(CultureInfo.InvariantCulture);
        return commits
            .SelectMany((lines, k) => lines.Select(l => $"{prefix}:{k.ToString(CultureInfo.InvariantCulture)} {l}"))
            .ToList();
    }
}
