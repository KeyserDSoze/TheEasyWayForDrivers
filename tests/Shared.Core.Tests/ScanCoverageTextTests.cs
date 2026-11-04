using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

public sealed class ScanCoverageTextTests
{
    [Fact]
    public void ReportSeparatesQueriedMissingAndFailedSourcesWithoutPrivateIds()
    {
        var wua = new DriverSearchReport(
            [
                new DriverUpdateInfo("71906b24-4e3a-46ae-8d99-7352d941eeb1",
                    "NVIDIA Display", null, null, "NVIDIA", null, null,
                    false, null, null, @"PCI\VEN_10DE&DEV_28A0", null)
            ],
            [
                new DriverSearchPassStatus("Windows Update online", "Completed",
                    2, "OK", NewUniqueCandidates: 1),
                new DriverSearchPassStatus("Microsoft Update online", "Failed",
                    0, "C:\\Users\\private\\path", NewUniqueCandidates: 0),
                new DriverSearchPassStatus("Servizio aggiuntivo", "NotRegistered",
                    0, "Not registered")
            ]);

        var text = ScanCoverageText.Create(wua,
        [
            new VendorCoverageRow("NVIDIA", 3, 0, 0, 0),
            new VendorCoverageRow("Killer / Intel", 2, 1, 1, 0)
        ], 113, 1, 0);

        Assert.Contains("Windows Update online: interrogata, 2 restituiti, 1 nuovi", text);
        Assert.Contains("Microsoft Update online: ricerca non riuscita", text);
        Assert.Contains("Servizio aggiuntivo: non registrata, non interrogata", text);
        Assert.Contains("NVIDIA: 3 dispositivi", text);
        Assert.Contains("Windows Update: 1 candidati distinti", text);
        Assert.DoesNotContain(@"PCI\VEN_", text);
        Assert.DoesNotContain(@"C:\Users", text);
        Assert.DoesNotContain("71906b24", text);
    }

    [Fact]
    public void MissingScanMustNotClaimZeroAvailableDrivers()
    {
        var text = ScanCoverageText.Create(null, [], 50, 1, 0);
        Assert.Contains("ricerca non verificata", text);
        Assert.Contains("nessuna fonte verificata", text);
        Assert.DoesNotContain("0 candidati distinti", text);
    }
}
