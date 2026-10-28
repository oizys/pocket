using System.Text.RegularExpressions;

namespace Pockets.Realization.Tests;

/// <summary>Architecture guards for the Realization layer (P2-D1, principle 6).</summary>
public class RealizationArchitectureTests
{
    /// <summary>The .cs files under a repo-relative directory, skipping obj and bin.</summary>
    private static IEnumerable<string> Sources(string rel) =>
        Directory.EnumerateFiles(Path.Combine(TestPaths.RepoRoot, rel), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                        && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"));

    /// <summary>The assembly pulls in no engine or terminal-UI dependency.</summary>
    [Fact]
    public void Assembly_ReferencesNoGodotOrTerminalGui()
    {
        var names = typeof(Pose).Assembly.GetReferencedAssemblies().Select(a => a.Name ?? "").ToList();
        Assert.DoesNotContain(names, n => n.StartsWith("Godot", StringComparison.Ordinal));
        Assert.DoesNotContain(names, n => n.StartsWith("Terminal.Gui", StringComparison.Ordinal));

        var csproj = File.ReadAllText(Path.Combine(TestPaths.RepoRoot, "src/Pockets.Realization/Pockets.Realization.csproj"));
        Assert.DoesNotContain("Godot", csproj);
        Assert.DoesNotContain("Terminal.Gui", csproj);
        Assert.DoesNotContain("PackageReference", csproj);
        var refs = Regex.Matches(csproj, "<ProjectReference\\s+Include=\"([^\"]+)\"").Select(m => m.Groups[1].Value).ToList();
        var only = Assert.Single(refs);
        Assert.EndsWith("Pockets.Core.csproj", only);
    }

    /// <summary>Realization source is integer-only (P2-D1).</summary>
    [Fact]
    public void Source_HasNoFloatingPoint()
    {
        var rx = new Regex(@"\b(float|double|decimal|Single|Double|Half)\b|\bMathF?\.");
        var hits = Sources("src/Pockets.Realization")
            .SelectMany(f => File.ReadAllLines(f).Select((line, i) => (f, i, line)))
            .Where(t => rx.IsMatch(t.line.Contains("//") ? t.line[..t.line.IndexOf("//", StringComparison.Ordinal)] : t.line))
            .Select(t => $"{Path.GetFileName(t.f)}:{t.i + 1}")
            .ToList();
        Assert.True(hits.Count == 0, "Floating point found at " + string.Join(", ", hits));
    }

    /// <summary>Core's Sim and Models never depend on Realization.</summary>
    [Fact]
    public void CoreSimAndModels_DoNotReferenceRealization()
    {
        var hits = Sources("src/Pockets.Core/Sim").Concat(Sources("src/Pockets.Core/Models"))
            .Where(f => File.ReadAllText(f).Contains("Pockets.Realization"))
            .Select(Path.GetFileName)
            .ToList();
        Assert.True(hits.Count == 0, "Realization referenced from " + string.Join(", ", hits));
        Assert.DoesNotContain(typeof(Pockets.Core.Sim.Simulation).Assembly.GetReferencedAssemblies(),
            a => a.Name == "Pockets.Realization");
    }
}
