using Pockets.Spikes.Plaque;
using Xunit.Abstractions;

namespace Pockets.Spikes.Tests;

/// <summary>Prints the Spike C comparison tables (copy into the design doc).</summary>
public class ReportTests
{
    private readonly ITestOutputHelper _out;
    public ReportTests(ITestOutputHelper o) => _out = o;

    [Fact]
    public void Print_Comparison_Tables()
    {
        var text = ComparisonTable.Build(new[] { 240, 241, 2400 }, new[] { 2, 4, 8 });
        _out.WriteLine(text);
        Assert.Contains("p2-catchup", text);
        Assert.Contains("belt-catchup", text);
    }
}
