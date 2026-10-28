using System.Reflection;
using System.Text.RegularExpressions;
using Pockets.Core.Models;
using Pockets.Core.Sim;

namespace Pockets.Core.Tests.Sim;

/// <summary>
/// These pin Slice 1.3's rule: frontends and tools change state only by handing intents to
/// GameController / GameSession.Apply — never by calling session mutators directly.
/// </summary>
public class IntentArchitectureTests
{
    private static readonly string[] MutatorNames =
    {
        "ApplyToolResult", "AdvanceDialogue", "FireFailedPeek", "FirePeekWhileCarrying", "BeginSplit",
        "AdjustSplit", "CommitSplit", "CancelSplit", "OpenRecipeMenu", "MoveRecipeMenu", "CloseRecipeMenu",
        "ConfirmRecipeMenu", "MoveCursorAt", "MoveCursor", "TryMoveCursorAt", "WithElapsed"
    };

    /// <summary>
    /// A text scan of every .cs file under src/Pockets.App, src/Pockets.Godot and tools/ (excluding obj/bin):
    /// no non-comment line calls a session mutator directly (PipelineExecutor.Execute is exempt).
    /// </summary>
    [Fact]
    public void Frontends_DoNotCallSessionMutatorsDirectly()
    {
        var banned = new Regex(
            @"\.(Execute[A-Za-z]*|ApplyToolResult|AdvanceDialogue|FireFailedPeek|FirePeekWhileCarrying|BeginSplit|AdjustSplit|CommitSplit|CancelSplit|OpenRecipeMenu|MoveRecipeMenu|CloseRecipeMenu|ConfirmRecipeMenu|MoveCursorAt|MoveCursor|TryMoveCursorAt|WithElapsed)\s*\(");
        var root = TestPaths.RepoRoot;
        var hits = new[] { "src/Pockets.App", "src/Pockets.Godot", "tools" }
            .Select(d => Path.Combine(root, d))
            .Where(Directory.Exists)
            .SelectMany(d => Directory.EnumerateFiles(d, "*.cs", SearchOption.AllDirectories))
            .Where(f => !f.Split(Path.DirectorySeparatorChar).Any(part => part is "obj" or "bin"))
            .SelectMany(f => File.ReadLines(f).Select((line, i) => (File: f, Line: i + 1, Text: line)))
            .Select(l => (l.File, l.Line, l.Text, Code: StripLineComment(l.Text).Replace("PipelineExecutor.Execute(", "")))
            .Where(l => banned.IsMatch(l.Code))
            .Select(l => $"{Path.GetRelativePath(root, l.File)}:{l.Line}: {l.Text.Trim()}")
            .ToList();

        Assert.True(hits.Count == 0, "Direct session mutator calls outside the shell:\n" + string.Join("\n", hits));
    }

    /// <summary>Removes a trailing // comment from a line (naive: does not account for // inside string literals).</summary>
    private static string StripLineComment(string line)
    {
        var idx = line.IndexOf("//", StringComparison.Ordinal);
        return idx < 0 ? line : line[..idx];
    }

    /// <summary>
    /// Reflection: GameSession exposes no public mutator methods (Execute*, the named mutators, Undo, Tick),
    /// and exposes a public Apply(Intent).
    /// </summary>
    [Fact]
    public void GameSession_PublicSurface_HasApplyAndNoDirectMutators()
    {
        var methods = typeof(GameSession).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        var offenders = methods
            .Where(m => m.Name.StartsWith("Execute", StringComparison.Ordinal)
                        || MutatorNames.Contains(m.Name)
                        || m.Name is "Undo" or "Tick")
            .Select(m => m.ToString())
            .ToList();

        Assert.True(offenders.Count == 0, "Public mutators on GameSession:\n" + string.Join("\n", offenders));

        var apply = typeof(GameSession).GetMethod("Apply", BindingFlags.Public | BindingFlags.Instance, null,
            new[] { typeof(Intent) }, null);
        Assert.NotNull(apply);
    }
}
