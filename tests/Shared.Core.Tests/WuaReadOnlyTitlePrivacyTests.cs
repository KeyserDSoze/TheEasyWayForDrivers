using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;
using TheEasyWayForDrivers.Core.Update;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

/// <summary>
/// #489: no private data in informational WUA partial/cache title previews,
/// including legacy IPC records that bypass initial COM title filtering.
/// </summary>
public sealed class WuaReadOnlyTitlePrivacyTests
{
    private const string IdA = "00000000-0000-4000-8000-000000000489";
    private const string IdB = "00000000-0000-4000-8000-000000000490";
    private const string Online = "Windows Update online";
    private const string CacheNotInstalled = "Cache Windows locale · non installati";
    private const string CacheInstalled = "Cache Windows locale · già installati";

    [Theory]
    [InlineData("NVIDIA Display owner@example.invalid")]
    [InlineData("Intel Network PRIVATE_ACCOUNT_123")]
    [InlineData("Realtek Audio SECRET_KEY_123")]
    [InlineData("Intel Killer ORDER_12345")]
    [InlineData("NVIDIA DEV_28A0")]
    [InlineData("Intel VEN_8086")]
    [InlineData("Intel SerialNumber 9876")]
    [InlineData("Realtek INSTANCEID123")]
    [InlineData("NVIDIA\u200B Display")]
    [InlineData("NVIDIA\u00A0Display")]
    [InlineData("NVIDIA\uFFFDDisplay")]
    [InlineData("NVIDIA\u2060Display")]
    [InlineData("NVIDIA\tDisplay")]
    [InlineData(@"C:\Users\PRIVATE\driver.inf")]
    [InlineData("C:/Users/private/driver.inf")]
    [InlineData("NVIDIA - 32.0:bad")]
    public void UntrustedTitlesAreRejectedInBothReadOnlyCollections(string title)
    {
        Assert.Null(WuaPartialDriverHintCollector.TryCreate(IdA, title, Online));
        Assert.Null(WuaCachedDriverHintCollector.TryCreate(IdA, title));
    }

    [Theory]
    [InlineData("NVIDIA - Display - 32.0.16.2000")]
    [InlineData("Intel(R) Killer(TM) Wi-Fi AX1675i")]
    [InlineData("Realtek Áudio 2.0")]
    [InlineData("AMD Radeon (TM) Graphics - 24.8")]
    [InlineData("Intel - Net NVIDIA\u0301ware 1.0")]
    public void OrdinaryNamesAndVisibleUnicodeStillPass(string title)
    {
        var partial = Assert.IsType<WuaPartialDriverHint>(
            WuaPartialDriverHintCollector.TryCreate(IdA, title, Online));
        var cached = Assert.IsType<WuaCachedDriverHint>(
            WuaCachedDriverHintCollector.TryCreate(IdA, title));
        Assert.Equal(title, partial.Title);
        Assert.Equal(title, cached.Title);
        Assert.False(partial.IsInstallable);
        Assert.False(cached.IsInstallable);
        Assert.False(partial.IsVerified);
        Assert.False(cached.IsVerifiedAvailable);
    }

    [Fact]
    public void LegacyIpcPartialHintsCannotLeakThroughFairnessOrCompletedReport()
    {
        var dirty = new WuaPartialDriverHint(IdA,
            "NVIDIA Display owner@example.invalid", Online);
        var clean = new WuaPartialDriverHint(IdB, "NVIDIA - Display", Online);

        var staged = new Dictionary<string, WuaPartialDriverHint>();
        Assert.Equal(1, WuaPartialDriverHintCollector.MergeStaged(
            staged, [dirty, clean]));
        Assert.Single(staged);
        Assert.Equal(1, WuaPartialDriverHintCollector.Merge(
            new Dictionary<string, WuaPartialDriverHint>(),
            [dirty, clean], out var omitted));
        Assert.Equal(0, omitted);
        Assert.Equal(IdB, Assert.Single(
            WuaPartialDriverHintCollector.SelectFair([dirty, clean]).Hints).UpdateId);

        var report = new DriverSearchReport([],
            [new DriverSearchPassStatus(Online, "Partial", 2, "private error")],
            PartialDriverHints: [dirty, clean]);
        var visible = Assert.Single(
            WuaPartialDriverHintCollector.ConfirmedForReport(report));
        Assert.Equal(IdB, visible.UpdateId);
        Assert.False(visible.IsVerified);
        Assert.False(visible.IsInstallable);
        Assert.Empty(WindowsDriverCandidateMerge.ConfirmedForReport(report));
        Assert.Empty(report.Updates);

        var qa = ScanCoverageText.Create(report, [], 1, 0, 0);
        Assert.Contains("1 visualizzati", qa);
        Assert.DoesNotContain("owner@example.invalid", qa);
        Assert.DoesNotContain(IdA, qa);
    }

    [Fact]
    public void LegacyIpcCacheTitlesCannotLeakThroughMergeOrReport()
    {
        var dirty = new WuaCachedDriverHint(IdA,
            "Intel Network PRIVATE_ACCOUNT_123", false);
        var clean = new WuaCachedDriverHint(IdB,
            "Intel(R) Wi-Fi AX211", false);
        var merged = new Dictionary<string, WuaCachedDriverHint>();
        Assert.Equal(1, WuaCachedDriverHintCollector.Merge(
            merged, [dirty, clean], [], out var omitted));
        Assert.Equal(0, omitted);
        Assert.Equal(IdB, Assert.Single(merged.Values).UpdateId);

        var report = new DriverSearchReport([], [],
            CachedDriverHints: [dirty, clean],
            CachedDriverSources:
            [
                new DriverSearchPassStatus(CacheNotInstalled, "Completed", 2, ""),
                new DriverSearchPassStatus(CacheInstalled, "Completed", 0, "")
            ]);
        var visible = Assert.Single(
            WuaCachedDriverHintCollector.ConfirmedForReport(report));
        Assert.Equal(IdB, visible.UpdateId);
        Assert.False(visible.IsInstallable);
        Assert.False(visible.IsVerifiedAvailable);
        Assert.Empty(report.Updates);

        var qa = ScanCoverageText.Create(report, [], 1, 0, 0);
        Assert.Contains("1 titoli distinti", qa);
        Assert.DoesNotContain("PRIVATE_ACCOUNT", qa);
        Assert.DoesNotContain(IdA, qa);
    }
}
