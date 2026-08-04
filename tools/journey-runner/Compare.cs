using System.Text;

namespace Pockets.JourneyRunner;

/// <summary>
/// EOL-normalized file/directory comparison folded into the runner so the Makefile parity
/// recipes are shell-agnostic (no <c>diff</c>/<c>mkdir</c>/<c>if</c> — those are sh-only and break
/// under cmd-spawned make on Windows). Every comparison normalizes line endings first (CRLF/CR → LF)
/// and ignores a trailing final newline, so a golden checked out as CRLF (Git for Windows
/// <c>autocrlf=true</c>) still matches an artifact the runner wrote with LF. This is the belt to the
/// .gitattributes suspenders: even a re-clone with stale attributes passes.
///
/// PASS/FAIL is written to stdout with a compact unified-style diff on failure; the process exit code
/// (0 pass, 1 fail) is what the Makefile gates on.
/// </summary>
public static class Compare
{
    /// <summary>Max diverging lines printed per file before the diff is truncated.</summary>
    private const int MaxDiffLines = 40;

    /// <summary>
    /// Splits text into logical lines independent of platform EOL. Recognizes CRLF, CR, and LF as
    /// line separators and drops a single trailing empty element so "a\n" and "a" compare equal.
    /// </summary>
    public static string[] NormalizeLines(string text)
    {
        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        // A trailing newline yields a final empty element; ignore it so files that differ only by a
        // trailing newline compare equal (goldens and artifacts are line-oriented).
        if (lines.Length > 0 && lines[^1].Length == 0)
            return lines[..^1];
        return lines;
    }

    /// <summary>
    /// Compares two files line-by-line, EOL-normalized. Returns 0 on match, 1 on mismatch (or a
    /// missing file). Prints a PASS/FAIL line prefixed with <paramref name="label"/> and, on failure,
    /// the first diverging lines.
    /// </summary>
    public static int Files(string expected, string actual, string label)
    {
        var tag = label.Length > 0 ? $"[{label}] " : "";

        if (!File.Exists(expected)) { Console.WriteLine($"COMPARE FAIL — {tag}missing file: {expected}"); return 1; }
        if (!File.Exists(actual)) { Console.WriteLine($"COMPARE FAIL — {tag}missing file: {actual}"); return 1; }

        var e = NormalizeLines(File.ReadAllText(expected));
        var a = NormalizeLines(File.ReadAllText(actual));

        var diff = DiffLines(e, a);
        if (diff.Count == 0)
        {
            Console.WriteLine($"COMPARE OK — {tag}{Path.GetFileName(expected)} matches (EOL-normalized)");
            return 0;
        }

        Console.WriteLine($"COMPARE FAIL — {tag}{Path.GetFileName(expected)} differs from {Path.GetFileName(actual)} " +
                          $"({diff.Count} diverging line(s), EOL-normalized):");
        PrintDiff(expected, actual, diff);
        return 1;
    }

    /// <summary>
    /// Compares every file under <paramref name="expectedDir"/> against the same relative path under
    /// <paramref name="actualDir"/> (EOL-normalized), and flags files present in actual but absent
    /// from expected. Returns 0 when every file matches and no extras exist, else 1.
    /// </summary>
    public static int Dirs(string expectedDir, string actualDir, string label)
    {
        var tag = label.Length > 0 ? $"[{label}] " : "";

        if (!Directory.Exists(expectedDir)) { Console.WriteLine($"COMPARE FAIL — {tag}missing dir: {expectedDir}"); return 1; }
        if (!Directory.Exists(actualDir)) { Console.WriteLine($"COMPARE FAIL — {tag}missing dir: {actualDir}"); return 1; }

        var expectedFiles = RelFiles(expectedDir);
        var actualFiles = RelFiles(actualDir);

        var failures = 0;
        foreach (var rel in expectedFiles)
        {
            var ePath = Path.Combine(expectedDir, rel);
            var aPath = Path.Combine(actualDir, rel);
            if (!File.Exists(aPath)) { Console.WriteLine($"COMPARE FAIL — {tag}missing in actual: {rel}"); failures++; continue; }

            var e = NormalizeLines(File.ReadAllText(ePath));
            var a = NormalizeLines(File.ReadAllText(aPath));
            var diff = DiffLines(e, a);
            if (diff.Count > 0)
            {
                Console.WriteLine($"COMPARE FAIL — {tag}{rel} differs ({diff.Count} diverging line(s), EOL-normalized):");
                PrintDiff(ePath, aPath, diff);
                failures++;
            }
        }

        // Extra artifacts the goldens don't cover are a divergence too (a new render surface that was
        // never recorded), so surface them rather than silently passing.
        foreach (var rel in actualFiles)
            if (!expectedFiles.Contains(rel))
            {
                Console.WriteLine($"COMPARE FAIL — {tag}unexpected file not in goldens: {rel}");
                failures++;
            }

        if (failures == 0)
        {
            Console.WriteLine($"COMPARE OK — {tag}{expectedFiles.Count} file(s) match (EOL-normalized)");
            return 0;
        }

        Console.WriteLine($"COMPARE FAIL — {tag}{failures} file(s) diverged");
        return 1;
    }

    /// <summary>Relative POSIX-style paths of every file under <paramref name="root"/>, sorted.</summary>
    private static HashSet<string> RelFiles(string root) =>
        Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(root, f).Replace('\\', '/'))
            .ToHashSet(StringComparer.Ordinal);

    /// <summary>Line indices (0-based) that differ between the two normalized line arrays.</summary>
    private static List<int> DiffLines(string[] expected, string[] actual)
    {
        var diffs = new List<int>();
        var max = Math.Max(expected.Length, actual.Length);
        for (var i = 0; i < max; i++)
        {
            var e = i < expected.Length ? expected[i] : null;
            var a = i < actual.Length ? actual[i] : null;
            if (!string.Equals(e, a, StringComparison.Ordinal)) diffs.Add(i);
        }
        return diffs;
    }

    private static void PrintDiff(string expectedPath, string actualPath, List<int> diffLines)
    {
        var e = NormalizeLines(File.ReadAllText(expectedPath));
        var a = NormalizeLines(File.ReadAllText(actualPath));
        var shown = 0;
        foreach (var i in diffLines)
        {
            if (shown++ >= MaxDiffLines) { Console.WriteLine($"  … ({diffLines.Count - MaxDiffLines} more)"); break; }
            var eLine = i < e.Length ? e[i] : "(absent)";
            var aLine = i < a.Length ? a[i] : "(absent)";
            Console.WriteLine($"  L{i + 1,-4} - {eLine}");
            Console.WriteLine($"  L{i + 1,-4} + {aLine}");
        }
    }
}
