using System.Reflection;
using System.Text.RegularExpressions;
using Pockets.Core.Sim;

namespace Pockets.Core.Tests.Sim;

/// <summary>
/// Slice 1.4's architecture pins: every <see cref="IRule"/> declares its kind and time class explicitly and
/// is registered, and the step paths never write grids cell-by-cell (the per-change SetItem ban; writes go
/// through <see cref="IBagWriter"/>).
/// </summary>
public class RuleArchitectureTests
{
    /// <summary>
    /// Ban exemptions, each with its reason. A hit is exempt only when both the file and the line text match.
    /// </summary>
    private static readonly (string File, string Contains, string Reason)[] BanExemptions =
    {
        ("src/Pockets.Core/Sim/SimRng.cs", "_draws.SetItem(",
            "the RNG's per-stream draw-counter dictionary, not a grid; one write per draw"),
    };

    /// <summary>Every concrete IRule type in Pockets.Core.</summary>
    private static IReadOnlyList<Type> RuleTypes() =>
        typeof(IRule).Assembly.GetTypes()
            .Where(t => typeof(IRule).IsAssignableFrom(t) && t is { IsInterface: false, IsAbstract: false })
            .ToList();

    [Fact]
    public void Registry_HoldsEveryRuleType_ExactlyOnce()
    {
        var registered = RuleRegistry.TickPhase.Select(r => r.GetType()).ToList();

        Assert.Equal(registered.Count, registered.Distinct().Count());
        Assert.Equal(RuleTypes().OrderBy(t => t.FullName).ToList(), registered.OrderBy(t => t.FullName).ToList());
    }

    [Fact]
    public void Registry_TickPhaseOrder_IsFacilitiesThenPlants()
    {
        Assert.Equal(new[] { typeof(FacilityCraftRule), typeof(PlantGrowthRule) },
            RuleRegistry.TickPhase.Select(r => r.GetType()).ToArray());
    }

    [Fact]
    public void EveryRule_DeclaresADefinedKindAndTimeClass()
    {
        var offenders = RuleRegistry.TickPhase
            .Where(r => !Enum.IsDefined(r.Kind) || !Enum.IsDefined(r.Time))
            .Select(r => r.GetType().Name)
            .ToList();

        Assert.True(offenders.Count == 0, "Rules with an undeclared kind or time class: " + string.Join(", ", offenders));
    }

    [Fact]
    public void EveryRule_DeclaresTimeOnItsOwnType()
    {
        // The interface has no default for Time; this pins that each rule type itself implements it.
        var offenders = RuleTypes()
            .Where(t => t.GetProperty(nameof(IRule.Time), BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly) is null
                        || t.GetProperty(nameof(IRule.Kind), BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly) is null)
            .Select(t => t.Name)
            .ToList();

        Assert.True(offenders.Count == 0, "Rules not declaring Kind/Time themselves: " + string.Join(", ", offenders));
    }

    [Fact]
    public void BoundedCatchUpRules_HavePositiveMaxDelta_OthersZero()
    {
        var offenders = RuleRegistry.TickPhase
            .Where(r => r.Time == TimeClass.BoundedCatchUp ? r.MaxDelta <= 0 : r.MaxDelta != 0)
            .Select(r => $"{r.GetType().Name} ({r.Time}, MaxDelta {r.MaxDelta})")
            .ToList();

        Assert.True(offenders.Count == 0, "Bad MaxDelta: " + string.Join(", ", offenders));
    }

    [Fact]
    public void EveryRule_EventKinds_AreSimEventCases()
    {
        var offenders = RuleRegistry.TickPhase
            .SelectMany(r => r.EventKinds.Select(k => (Rule: r, Kind: k)))
            .Where(p => !p.Kind.IsSealed || p.Kind.BaseType != typeof(SimEvent))
            .Select(p => $"{p.Rule.GetType().Name}: {p.Kind.Name}")
            .ToList();

        Assert.True(offenders.Count == 0, "Non-SimEvent event kinds: " + string.Join(", ", offenders));
    }

    [Fact]
    public void Rules_DeclareThePlannedKindAndTimeClass()
    {
        Assert.Equal((RuleKind.Transfer, TimeClass.EventSkippable),
            (FacilityCraftRule.Instance.Kind, FacilityCraftRule.Instance.Time));
        Assert.Equal((RuleKind.Field, TimeClass.ClosedForm),
            (PlantGrowthRule.Instance.Kind, PlantGrowthRule.Instance.Time));
        // Slice 1.5: the facility rule's full craft lifecycle, and the plant rule's first event.
        Assert.Equal(new[]
            {
                typeof(SimEvent.CraftStarted), typeof(SimEvent.CraftProgressed),
                typeof(SimEvent.CraftAborted), typeof(SimEvent.CraftCompleted)
            },
            FacilityCraftRule.Instance.EventKinds.ToArray());
        Assert.Equal(new[] { typeof(SimEvent.PlantGrew) }, PlantGrowthRule.Instance.EventKinds.ToArray());
    }

    [Fact]
    public void Bag_Clock_DefaultsToObservedAtRateOne()
    {
        var bag = new Pockets.Core.Models.Bag(Pockets.Core.Models.Grid.Create(1, 1));

        Assert.Equal(new BagClock(1, BagClockMode.Observed), bag.Clock);
        Assert.Same(BagClock.Observed, bag.Clock);
    }

    /// <summary>
    /// The per-change SetItem ban: no non-comment line under Models/*Logic.cs, Sim/** or Rules/** in
    /// Pockets.Core calls <c>.SetCell(</c> or <c>.SetItem(</c>, except the listed exemptions.
    /// </summary>
    [Fact]
    public void StepPaths_HaveNoPerChangeGridWrites()
    {
        var root = TestPaths.RepoRoot;
        var core = Path.Combine(root, "src", "Pockets.Core");
        var banned = new Regex(@"\.(SetCell|SetItem)\s*\(");

        var files = Directory.EnumerateFiles(Path.Combine(core, "Models"), "*Logic.cs")
            .Concat(new[] { "Sim", "Rules" }
                .Select(d => Path.Combine(core, d))
                .Where(Directory.Exists)
                .SelectMany(d => Directory.EnumerateFiles(d, "*.cs", SearchOption.AllDirectories)))
            .Where(f => !f.Split(Path.DirectorySeparatorChar).Any(part => part is "obj" or "bin"))
            .ToList();
        Assert.Contains(files, f => f.EndsWith("FacilityLogic.cs"));
        Assert.Contains(files, f => f.EndsWith("PlantLogic.cs"));

        var hits = files
            .SelectMany(f => File.ReadLines(f).Select((line, i) => (
                File: Path.GetRelativePath(root, f).Replace(Path.DirectorySeparatorChar, '/'), Line: i + 1, Text: line)))
            .Where(l => banned.IsMatch(StripComment(l.Text)))
            .Where(l => !BanExemptions.Any(e => e.File == l.File && l.Text.Contains(e.Contains)))
            .Select(l => $"{l.File}:{l.Line}: {l.Text.Trim()}")
            .ToList();

        Assert.True(hits.Count == 0, "Per-change grid writes in step paths (use IBagWriter):\n" + string.Join("\n", hits));
    }

    /// <summary>Removes a trailing // (or ///) comment from a line (naive: ignores // inside literals).</summary>
    private static string StripComment(string line)
    {
        var idx = line.IndexOf("//", StringComparison.Ordinal);
        return idx < 0 ? line : line[..idx];
    }
}
