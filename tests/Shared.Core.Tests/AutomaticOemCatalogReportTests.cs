using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;
using TheEasyWayForDrivers.Core.Update;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

/// <summary>
/// #471: the copyable evidence must disclose automatic Dell/HP catalog
/// completeness without disclosing model/serial/URL or altering the WUA
/// Type=Driver / Type=Software conclusions used by Update All.
/// </summary>
public sealed class AutomaticOemCatalogReportTests
{
    private const string DriverGuid = "00000000-0000-4000-8000-000000000471";
    private const string SoftwareGuid = "00000000-0000-4000-8000-000000000472";
    private const string Nvidia = @"PCI\VEN_10DE&DEV_28A0";

    private static DriverSearchReport Report(bool software = false) =>
        new([
            new DriverUpdateInfo(DriverGuid, "NVIDIA Display",
                null, "Display", "Microsoft", null, null, false,
                "Microsoft", null, Nvidia, null,
                SearchSource: "Windows Update online")
        ], [
            new DriverSearchPassStatus("Windows Update online",
                "Completed", 1, "PRIVATE_WSUS_ADDRESS", 1)
        ], software
            ? [new WuaDriverRelatedSoftware(SoftwareGuid,
                "Killer Networking Software",
                "Windows Update online · componenti software")]
            : [], software
            ? [new DriverSearchPassStatus(
                "Windows Update online · componenti software",
                "Completed", 1, "PRIVATE_WSUS_ADDRESS", 1)]
            : []);

    private static string Copy(
        AutomaticOemCatalogCoverage.CatalogQueryStatus status,
        DriverSearchReport? report = null) =>
        ScanCoverageText.Create(report ?? Report(true), [],
            8, 0, 0,
            automaticOemCatalogStatus: status);

    [Theory]
    [InlineData(false, false, false,
        AutomaticOemCatalogCoverage.CatalogQueryStatus.NotApplicable)]
    [InlineData(true, false, false,
        AutomaticOemCatalogCoverage.CatalogQueryStatus.Completed)]
    [InlineData(false, true, false,
        AutomaticOemCatalogCoverage.CatalogQueryStatus.MissingPrerequisites)]
    [InlineData(false, true, true,
        AutomaticOemCatalogCoverage.CatalogQueryStatus.Failed)]
    [InlineData(true, true, false,
        AutomaticOemCatalogCoverage.CatalogQueryStatus.MissingPrerequisites)]
    [InlineData(true, true, true,
        AutomaticOemCatalogCoverage.CatalogQueryStatus.Failed)]
    [InlineData(false, false, true,
        AutomaticOemCatalogCoverage.CatalogQueryStatus.Failed)]
    [InlineData(true, false, true,
        AutomaticOemCatalogCoverage.CatalogQueryStatus.Failed)]
    public void OutcomeClassificationDoesNotUseRowsOrLocalizedText(
        bool queried, bool incomplete, bool canRetry,
        AutomaticOemCatalogCoverage.CatalogQueryStatus expected)
    {
        Assert.Equal(expected, AutomaticOemCatalogCoverage.Classify(
            queried, incomplete, canRetry));
    }

    [Theory]
    [InlineData(
        AutomaticOemCatalogCoverage.CatalogQueryStatus.NotApplicable,
        "non interrogato / non applicabile")]
    [InlineData(
        AutomaticOemCatalogCoverage.CatalogQueryStatus.Completed,
        "interrogato; solo metadati")]
    [InlineData(
        AutomaticOemCatalogCoverage.CatalogQueryStatus.MissingPrerequisites,
        "non interrogato, prerequisiti")]
    [InlineData(
        AutomaticOemCatalogCoverage.CatalogQueryStatus.Failed,
        "query fallita/non verificata")]
    public void CopiedReportDisclosesOnlyCanonicalOemStatus(
        AutomaticOemCatalogCoverage.CatalogQueryStatus status,
        string expected)
    {
        var copy = Copy(status);
        Assert.Contains("Catalogo OEM automatico Dell/HP: ", copy);
        Assert.Contains(expected, copy);
        Assert.Equal(1, copy.Split('\n').Count(line =>
            line.StartsWith("Catalogo OEM automatico Dell/HP: ",
                StringComparison.Ordinal)));
        Assert.Contains("Windows Update: 1 candidati distinti", copy);
        Assert.Contains("Fonti WUA:", copy);
        Assert.Contains("Componenti software produttore da verificare (NON driver)",
            copy);
        Assert.DoesNotContain("PRIVATE_", copy);
        Assert.DoesNotContain(DriverGuid, copy);
        Assert.DoesNotContain(SoftwareGuid, copy);
        Assert.DoesNotContain(Nvidia, copy);
        Assert.DoesNotContain("RTX", copy);
        Assert.DoesNotContain("WSUS_ADDRESS", copy);
        Assert.DoesNotContain("PRIVATE_SOFTWARE_DRIVER_NAME", copy);
        Assert.DoesNotContain("Killer Networking Software", copy);
    }

    [Fact]
    public void CompletedZeroRowCatalogDoesNotClaimAnApplicableDriver()
    {
        var status = AutomaticOemCatalogCoverage.Classify(
            wasQueried: true,
            isVerificationIncomplete: false,
            canRetry: false);
        var report = new DriverSearchReport([], [
            new DriverSearchPassStatus("Windows configurato", "Completed", 0,
                "PRIVATE_HOST", 0)
        ]);
        var copy = Copy(status, report);
        Assert.Contains("interrogato; solo metadati", copy);
        Assert.Contains("NON prova driver necessari", copy);
        Assert.Contains("anche con zero risultati", copy);
        Assert.Contains("Windows Update: 0 candidati distinti", copy);
        Assert.DoesNotContain("driver consigliati da HP", copy);
        Assert.DoesNotContain("PRIVATE_", copy);
    }

    [Fact]
    public void MissingPrerequisitesDoesNotConflateUnqueriedWithEmptyCatalog()
    {
        var status = AutomaticOemCatalogCoverage.Classify(
            wasQueried: false,
            isVerificationIncomplete: true,
            canRetry: false);
        var copy = Copy(status);
        Assert.Contains("non interrogato, prerequisiti", copy);
        Assert.Contains("NON significa zero driver OEM", copy);
        Assert.DoesNotContain("interrogato; solo metadati", copy);
    }

    [Fact]
    public void CatalogRetryDoesNotChangeWuaOrSoftwareCountsOrEligibility()
    {
        var report = Report(software: true);
        var beforeWua = WindowsDriverCandidateMerge
            .ConfirmedDistinctForReport(report);
        var beforeIds = QuickUpdatePolicy.AllOrdinaryIds(beforeWua);
        var beforeSoftware = WuaDriverRelatedSoftwareClassifier
            .ConfirmedForReport(report);

        var failed = Copy(AutomaticOemCatalogCoverage.CatalogQueryStatus.Failed,
            report);
        var success = Copy(AutomaticOemCatalogCoverage.CatalogQueryStatus.Completed,
            report);
        var noModel = Copy(
            AutomaticOemCatalogCoverage.CatalogQueryStatus.MissingPrerequisites,
            report);

        foreach (var copy in new[] { failed, success, noModel })
        {
            Assert.Contains("Windows Update: 1 candidati distinti", copy);
            Assert.Contains("interrogata, 1 restituiti, 1 nuovi", copy);
            Assert.Contains("Componenti software produttore da verificare (NON driver)",
                copy);
            Assert.DoesNotContain("PRIVATE_", copy);
            Assert.DoesNotContain(DriverGuid, copy);
            Assert.DoesNotContain(SoftwareGuid, copy);
            Assert.DoesNotContain("Killer Networking Software", copy);
        }
        Assert.Single(beforeIds);
        Assert.Equal(beforeIds,
            QuickUpdatePolicy.AllOrdinaryIds(
                WindowsDriverCandidateMerge.ConfirmedDistinctForReport(report)));
        Assert.Equal(beforeSoftware.Count,
            WuaDriverRelatedSoftwareClassifier.ConfirmedForReport(report).Count);
        Assert.False(report.IsPartial);
        Assert.False(report.SoftwareSearchIsPartial);
        Assert.False(Assert.Single(beforeSoftware).IsInstallable);
    }

    [Fact]
    public void FailedWuaPassAndCompletedOemCatalogStayIndependent()
    {
        var report = new DriverSearchReport([], [
            new DriverSearchPassStatus("Windows Update online",
                "NetworkError", 0, "PRIVATE_PROXY", 0)
        ]);
        var copy = Copy(AutomaticOemCatalogCoverage.CatalogQueryStatus.Completed,
            report);
        Assert.Contains("Catalogo OEM automatico Dell/HP: interrogato", copy);
        Assert.Contains("Windows Update: ricerca non verificata", copy);
        Assert.Contains("errore di connessione/proxy/server", copy);
        Assert.Empty(QuickUpdatePolicy.AllOrdinaryIds(
            WindowsDriverCandidateMerge.ConfirmedDistinctForReport(report)));
        Assert.DoesNotContain("PRIVATE_", copy);
    }

    [Fact]
    public void UnrecognizedOemStateFailsClosedRatherThanInventsSuccess()
    {
        var unknown = (AutomaticOemCatalogCoverage.CatalogQueryStatus)999;
        var copy = Copy(unknown);
        Assert.Contains("stato non riconosciuto, verifica NON disponibile",
            copy);
        Assert.DoesNotContain(
            "Catalogo OEM automatico Dell/HP: interrogato;", copy);
        Assert.DoesNotContain("PRIVATE_", copy);
    }

    [Fact]
    public void OmittedStatusIsExplicitlyNotApplicableForOldCallers()
    {
        var report = ScanCoverageText.Create(Report(), [], 8, 0, 0);
        Assert.Contains(
            "Catalogo OEM automatico Dell/HP: non interrogato / non applicabile",
            report);
        Assert.Contains("Windows Update: 1 candidati distinti", report);
        Assert.DoesNotContain("PRIVATE_", report);
    }

    [Fact]
    public void RetryTransitionsAreStableAndOnlyReplaceOemGap()
    {
        var missing = AutomaticOemCatalogCoverage.Classify(
            false, true, false);
        var firstFailure = AutomaticOemCatalogCoverage.Classify(
            false, true, true);
        var requery = AutomaticOemCatalogCoverage.Classify(
            true, false, false);

        Assert.Equal(
            AutomaticOemCatalogCoverage.CatalogQueryStatus.MissingPrerequisites,
            missing);
        Assert.Equal(AutomaticOemCatalogCoverage.CatalogQueryStatus.Failed,
            firstFailure);
        Assert.Equal(AutomaticOemCatalogCoverage.CatalogQueryStatus.Completed,
            requery);
        var failures = AutomaticOemCatalogCoverage.WithOutcome([
            "inventario dispositivi",
            AutomaticOemCatalogCoverage.UnavailableStep,
            "ricerca WUA non riuscita"
        ], isVerificationIncomplete: false);
        Assert.Equal(["inventario dispositivi", "ricerca WUA non riuscita"],
            failures);
        var copy = Copy(requery);
        Assert.Contains("interrogato; solo metadati", copy);
        Assert.DoesNotContain("PRIVATE_", copy);
    }
}
