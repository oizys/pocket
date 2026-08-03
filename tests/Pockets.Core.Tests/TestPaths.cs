namespace Pockets.Core.Tests;

/// <summary>
/// Shared test helper for locating repo-relative paths (walks up to the solution root).
/// </summary>
public static class TestPaths
{
    /// <summary>The repository root (directory containing Pockets.sln).</summary>
    public static string RepoRoot
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Pockets.sln")))
                dir = dir.Parent;
            if (dir is null)
                throw new DirectoryNotFoundException("Could not locate Pockets.sln above the test assembly.");
            return dir.FullName;
        }
    }

    /// <summary>The repo's canonical item/facility/recipe data directory.</summary>
    public static string DataDir => Path.Combine(RepoRoot, "data");
}
