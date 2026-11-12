using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;
using TheEasyWayForDrivers.Core.Update;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

/// <summary>
/// #402: the copyable support report, WUA status UI and GUID provenance
/// must agree on which Type=Driver passes really completed.
/// </summary>
public sealed class WuaSourceCopyStatusConsistencyTests
{
    private const string IdNvidia = "00000000-0000-4000-8000-000000000401";
    private const string IdKiller = "00000000-0000-4000-8000-000000000402";

    private static DriverUpdateInfo Update(string id, string source) =>
        new(id, "Windows generic device driver", null, "Display",
            "Microsoft", null, null, false, "Microsoft", null,
            @"PCI\VEN_10DE&DEV_28A0", null, SearchSource: source);

    private static DriverSearchPassStatus Pass(string source,
        string outcome, int raw = 0, int added = 0) =>
        new(source, outcome, raw, "PRIVATE_WSUS_PROXY", added);

    private static string Copy(DriverSearchReport report) =>
        ScanCoverageText.Create(report, [], 2, 0, 0);

    [Fact]
    public void DuplicateCompletedSourceCannotClaimQueriedInExportOrHome()
    {
        var report = new DriverSearchReport([
            Update(IdNvidia, "Windows Update online"),
            Update(IdKiller, "Windows configurato")
        ], [
            Pass("Windows configurato", "Completed", 1, 1),
            Pass("Windows Update online", "Completed", 3, 3),
            Pass("WINDOWS UPDATE ONLINE", "Failed")
        ]);

        Assert.True(report.IsPartial);
        Assert.Equal(IdKiller, Assert.Single(
            WindowsDriverCandidateMerge.ConfirmedDistinctForReport(report)).Id);
        var copied = Copy(report);
        var displayed = string.Join(" ", WuaPassStatusPresentation.Create(report));
        Assert.Equal(2, copied.Split("fonte NON verificata").Length - 1);
        Assert.DoesNotContain("3 restituiti", copied);
        Assert.DoesNotContain("3 risultati restituiti", displayed);
        Assert.Contains("Windows configurato: interrogata, 1 restituiti", copied);
        Assert.Contains("Windows configurato: ricerca completata", displayed);
        Assert.DoesNotContain("PRIVATE_WSUS_PROXY", copied);
        Assert.DoesNotContain("PRIVATE_WSUS_PROXY", displayed);
    }

    [Fact]
    public void UnknownCompletedSourceNeverBecomesVerifiedWithPrivateName()
    {
        var report = new DriverSearchReport([
            Update(IdNvidia, "PRIVATE_WSUS_PROXY")
        ], [Pass("PRIVATE_WSUS_PROXY", "Completed", 921, 1)]);

        Assert.True(report.IsPartial);
        Assert.Empty(WindowsDriverCandidateMerge.ConfirmedDistinctForReport(report));
        var copied = Copy(report);
        var displayed = string.Join(" ", WuaPassStatusPresentation.Create(report));
        Assert.Contains("Fonte Windows non riconosciuta: fonte NON verificata", copied);
        Assert.Contains("Fonte Windows non riconosciuta: fonte o stato NON verificati",
            displayed);
        Assert.DoesNotContain("921", copied);
        Assert.DoesNotContain("921", displayed);
        Assert.DoesNotContain("PRIVATE_WSUS_PROXY", copied);
        Assert.DoesNotContain("PRIVATE_WSUS_PROXY", displayed);
    }

    [Theory]
    [InlineData(-2, 0)]
    [InlineData(1, 2)]
    [InlineData(2, -1)]
    public void ImpossibleCompletedCountersDoNotAuthenticateCandidateGuid(
        int raw, int added)
    {
        var report = new DriverSearchReport([
            Update(IdNvidia, "Windows Update online")
        ], [Pass("Windows Update online", "Completed", raw, added)]);

        Assert.True(report.IsPartial);
        Assert.False(WindowsDriverCandidateMerge.HasCompletedKnownWuaPass(report));
        Assert.Empty(WindowsDriverCandidateMerge.ConfirmedDistinctForReport(report));
        Assert.Contains("fonte NON verificata", Copy(report));
        Assert.Contains("NON verificati",
            Assert.Single(WuaPassStatusPresentation.Create(report)));
        Assert.DoesNotContain("interrogata, ", Copy(report));
    }

    [Theory]
    [InlineData("Windows configurato", 0, 0)]
    [InlineData("Windows Update online", 0, 0)]
    [InlineData("Microsoft Update online", 3, 0)]
    public void InvalidNotRegisteredNeverActsLikeHarmlessMissingService(
        string source, int raw, int added)
    {
        var report = new DriverSearchReport([], [
            Pass("Windows configurato", "Completed"),
            Pass(source, "NotRegistered", raw, added)
        ]);
        Assert.True(report.IsPartial);
        Assert.Contains("fonte NON verificata", Copy(report));
        Assert.Contains("NON verificati",
            string.Join(" ", WuaPassStatusPresentation.Create(report)));
    }

    [Fact]
    public void LegitimateEmptyWuaAndUnregisteredMicrosoftRemainDistinguishable()
    {
        var report = new DriverSearchReport([], [
            Pass("Windows configurato", "Completed"),
            WindowsDriverSearchPlan.NotRegisteredStatus()
        ]);
        Assert.False(report.IsPartial);
        Assert.Contains("Windows configurato: interrogata, 0 restituiti", Copy(report));
        Assert.Contains("Microsoft Update online: non registrata, non interrogata",
            Copy(report));
        Assert.DoesNotContain("fonte NON verificata", Copy(report));
        Assert.Contains("0 risultati restituiti",
            string.Join(" ", WuaPassStatusPresentation.Create(report)));
    }
}
