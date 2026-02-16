namespace Pockets.Core.Tests;

public class SmokeTest
{
    [Fact]
    public void ProjectReferencesAreWired()
    {
        // Verifies that the test project can reference Pockets.Core
        var coreAssembly = typeof(SmokeTest).Assembly;
        Assert.NotNull(coreAssembly);
    }
}
