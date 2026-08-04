using System.Globalization;
using Pockets.Core.Data;
using Pockets.Core.Models;
using Pockets.Core.Rendering;

namespace Pockets.Core.Tests;

/// <summary>
/// Guards the parity gate against host-locale drift. Every value that feeds the VM checkpoint stream,
/// the TUI buffer goldens, or the demo profile they derive from must be byte-identical regardless of
/// the machine's culture. Two classic traps are exercised here on Aaron's kind of Windows box:
///
///   • <b>de-DE</b> (comma-decimal) — <c>double.Parse("0.5")</c> would silently yield 5.0, corrupting
///     loot tables → the demo profile → every downstream golden. And any <c>double.ToString()</c> in a
///     render path would emit "0,5".
///   • <b>tr-TR</b> (the Turkish-I) — <c>"iron".ToUpper()</c> yields "İRON", drifting item
///     abbreviations in the toolbar/hand buffer goldens.
///
/// Each test computes the InvariantCulture baseline, then re-runs under the awkward culture and asserts
/// identical output. A regression that reintroduces culture-sensitive formatting fails here, not on
/// Aaron's machine.
/// </summary>
public class CultureInvarianceTests
{
    /// <summary>Cultures with formatting conventions most likely to expose a culture leak.</summary>
    public static IEnumerable<object[]> AwkwardCultures =>
        new[] { new object[] { "de-DE" }, new object[] { "tr-TR" } };

    /// <summary>Runs <paramref name="body"/> with the thread pinned to <paramref name="culture"/>, restoring after.</summary>
    private static T Under<T>(string culture, Func<T> body)
    {
        var prevCulture = CultureInfo.CurrentCulture;
        var prevUi = CultureInfo.CurrentUICulture;
        try
        {
            var c = CultureInfo.GetCultureInfo(culture);
            CultureInfo.CurrentCulture = c;
            CultureInfo.CurrentUICulture = c;
            return body();
        }
        finally
        {
            CultureInfo.CurrentCulture = prevCulture;
            CultureInfo.CurrentUICulture = prevUi;
        }
    }

    private static string SerializeDemoVm() =>
        ViewModelSerializer.SerializeToString(
            GameInitializer.CreateDemoProfile(ContentLoader.LoadFromDirectory(TestPaths.DataDir)).NewSession());

    /// <summary>
    /// The representative end-to-end serialization: load the real demo data, build the demo profile,
    /// and project the whole view-model to its canonical string — under a comma-decimal / Turkish-I
    /// locale it must match the invariant baseline exactly. This is the direct proxy for a golden.
    /// </summary>
    [Theory]
    [MemberData(nameof(AwkwardCultures))]
    public void DemoViewModelSerialization_IsCultureInvariant(string culture)
    {
        var baseline = Under(CultureInfo.InvariantCulture.Name, SerializeDemoVm);
        var underCulture = Under(culture, SerializeDemoVm);
        Assert.Equal(baseline, underCulture);
    }

    /// <summary>
    /// Loot tables carry '.'-decimal weights + FillRatio parsed from the data files. Under de-DE a
    /// naive <c>double.Parse</c> misreads "0.5" as 5.0 — silent corruption that never throws. Pin the
    /// exact parsed values so the InvariantCulture parse is proven, not just self-consistent.
    /// </summary>
    [Theory]
    [MemberData(nameof(AwkwardCultures))]
    public void LootTableDecimals_ParseCultureInvariant(string culture)
    {
        var loot = Under(culture, () =>
            ContentLoader.LoadFromDirectory(TestPaths.DataDir).LootTableTemplates["forest-materials"]);

        Assert.Equal(0.6, loot.FillRatio);
        Assert.Equal(0.5, loot.Entries.Single(e => e.ItemName == "Forest Seed").Weight);
        Assert.Equal(0.3, loot.Entries.Single(e => e.ItemName == "Iron Ore").Weight);
        Assert.Equal(2.0, loot.Entries.Single(e => e.ItemName == "Rough Wood").Weight);
    }

    /// <summary>The mm:ss clock readout (VM + TUI buffer) must not pick up locale digit shaping.</summary>
    [Theory]
    [MemberData(nameof(AwkwardCultures))]
    public void FormatClock_IsCultureInvariant(string culture)
    {
        var elapsed = TimeSpan.FromMilliseconds(754_000); // 12:34
        var baseline = Under(CultureInfo.InvariantCulture.Name, () => ViewModelSerializer.FormatClock(elapsed));
        Assert.Equal("12:34", baseline);
        Assert.Equal(baseline, Under(culture, () => ViewModelSerializer.FormatClock(elapsed)));
    }

    /// <summary>
    /// Item abbreviations feed the toolbar/hand buffer goldens. Under tr-TR the default ToUpper turns
    /// 'i' into 'İ'; the invariant upper-casing must keep "iron" → "IRON" everywhere.
    /// </summary>
    [Theory]
    [MemberData(nameof(AwkwardCultures))]
    public void AbbreviateName_IsCultureInvariant(string culture)
    {
        foreach (var name in new[] { "iron ingot", "iron", "Illicit Idol", "willow" })
        {
            var baseline = Under(CultureInfo.InvariantCulture.Name, () => RenderHelpers.AbbreviateName(name));
            Assert.Equal(baseline, Under(culture, () => RenderHelpers.AbbreviateName(name)));
        }
    }
}
