namespace Pockets.DepthRecipes;

/// <summary>Walks up from the running assembly to find the repo root (folder holding Pockets.sln).</summary>
public static class RepoLocator
{
    public static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Pockets.sln")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate Pockets.sln above the assembly directory.");
    }
}
