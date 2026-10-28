using System.Reflection;
using System.Text.RegularExpressions;
using Pockets.Core.Fine;
using Pockets.Core.Models;
using Pockets.Core.Sim;

namespace Pockets.Core.Tests.Fine;

/// <summary>
/// Slice 2.6, the L1 wall (principle 6, P2-D9): L1 never references L2a. No file under <c>Sim/**</c> or
/// <c>Models/**</c> except the shell glue in <c>Models/GameSession.cs</c> names <c>Pockets.Core.Fine</c> or one of its
/// types; <see cref="Simulation.Step"/> takes and returns no fine type; and the L1 state records carry none.
/// </summary>
public class FineWallTests
{
    /// <summary>The one file allowed past the wall: the session shell, which owns the fine state.</summary>
    private static readonly string[] Exempt = { "src/Pockets.Core/Models/GameSession.cs" };

    /// <summary>Every public type of the <c>Pockets.Core.Fine</c> namespace.</summary>
    private static IReadOnlyList<Type> FineTypes() =>
        typeof(FineState).Assembly.GetTypes()
            .Where(t => t.Namespace == "Pockets.Core.Fine" && t.IsPublic)
            .ToList();

    /// <summary>True when <paramref name="t"/> is, or is built from, a fine type (generic arguments, arrays, nullables).</summary>
    private static bool MentionsFine(Type t) =>
        t.Namespace == "Pockets.Core.Fine"
        || (t.HasElementType && MentionsFine(t.GetElementType()!))
        || (t.IsGenericType && t.GetGenericArguments().Any(MentionsFine));

    [Fact]
    public void TheFineNamespace_HasTheStoreTypes()
    {
        var names = FineTypes().Select(t => t.Name).ToList();
        Assert.Contains("FineState", names);
        Assert.Contains("FineBody", names);
        // Direction8 is a plain L1-side enum (Models): 2.7's Steer intent, which lives in Sim, carries it.
        Assert.DoesNotContain("Direction8", names);
        Assert.Equal("Pockets.Core.Models", typeof(Direction8).Namespace);
    }

    [Fact]
    public void SimAndModels_DoNotReferenceFine_ExceptTheSessionShell()
    {
        var root = TestPaths.RepoRoot;
        var typeNames = FineTypes().Select(t => Regex.Escape(t.Name)).ToList();
        var rx = new Regex(@"\bPockets\.Core\.Fine\b|\b(" + string.Join("|", typeNames) + @")\b");
        var hits = new[] { "src/Pockets.Core/Sim", "src/Pockets.Core/Models" }
            .SelectMany(d => Directory.EnumerateFiles(Path.Combine(root, d), "*.cs", SearchOption.AllDirectories))
            .Where(f => !f.Split(Path.DirectorySeparatorChar).Any(part => part is "obj" or "bin"))
            .Where(f => !Exempt.Contains(Path.GetRelativePath(root, f).Replace(Path.DirectorySeparatorChar, '/')))
            .SelectMany(f => File.ReadLines(f).Select((line, i) => (File: f, Line: i + 1, Text: line)))
            .Where(l => rx.IsMatch(l.Text))
            .Select(l => $"{Path.GetRelativePath(root, l.File)}:{l.Line}: {l.Text.Trim()}")
            .ToList();

        Assert.True(hits.Count == 0, "L1 references L2a:\n" + string.Join("\n", hits));
    }

    [Fact]
    public void SimulationStep_TakesAndReturnsNoFineType()
    {
        var steps = typeof(Simulation).GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            .Where(m => m.Name.StartsWith("Step", StringComparison.Ordinal))
            .ToList();
        Assert.NotEmpty(steps);
        var offenders = steps
            .Where(m => MentionsFine(m.ReturnType) || m.GetParameters().Any(p => MentionsFine(p.ParameterType)))
            .Select(m => m.ToString())
            .ToList();
        Assert.True(offenders.Count == 0, "Step mentions a fine type:\n" + string.Join("\n", offenders));
    }

    [Theory]
    [InlineData(typeof(GameState))]
    [InlineData(typeof(SimState))]
    [InlineData(typeof(SimContent))]
    [InlineData(typeof(Bag))]
    public void L1State_CarriesNoFineType(Type type)
    {
        var offenders = type.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .Where(p => MentionsFine(p.PropertyType))
            .Select(p => p.Name)
            .ToList();
        Assert.True(offenders.Count == 0, $"{type.Name} carries fine state: " + string.Join(", ", offenders));
    }

    [Fact]
    public void TheSession_HoldsTheFineState()
    {
        Assert.Equal(typeof(FineState), typeof(GameSession).GetProperty(nameof(GameSession.Fine))!.PropertyType);
    }
}
