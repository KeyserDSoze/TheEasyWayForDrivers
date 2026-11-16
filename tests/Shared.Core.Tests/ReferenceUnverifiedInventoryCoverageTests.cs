using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;
using TheEasyWayForDrivers.Core.Update;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

/// <summary>
/// #491: a completed WUA scan does not prove zero PnP associations when
/// the local inventory was unavailable or failed verification.
/// </summary>
public sealed class ReferenceUnverifiedInventoryCoverageTests
{
    private const string GpuId = @"PCI\VEN_10DE&DEV_28A0";
    private const string GpuGuid = "00000000-0000-4000-8000-000000000491";
    private const string SoftwareGuid = "00000000-0000-4000-8000-000000000492";

    private static DriverInfo Gpu() => new(
        "PRIVATE_GPU_INSTANCE", "Microsoft Basic Display Adapter",
        "Microsoft", "Display", "Microsoft", "1.0.0.0", null,
        "PRIVATE_LOCAL_DRIVER.inf", true, true, 0, [GpuId], []);

    private static DriverUpdateInfo Offer() => new(
        GpuGuid, "NVIDIA - Display", null, "Display", "NVIDIA",
        "31.0", null, false, "NVIDIA", null, GpuId, null,
        SearchSource: "Windows Update online");

    private static DriverSearchReport Report(bool partial = false) =>
        new([Offer()],
            partial
                ? [
                    new DriverSearchPassStatus(
                        "Windows Update online", "Completed", 1, "", 1),
                    new DriverSearchPassStatus(
                        "Microsoft Update online", "NetworkError", 0,
                        "PRIVATE_PROXY_HOST")
                  ]
                : [
                    new DriverSearchPassStatus(
                        "Windows Update online", "Completed", 1, "", 1)
                  ],
            RelatedSoftware:
            [
                new WuaDriverRelatedSoftware(SoftwareGuid,
                    "Killer Networking Software",
                    "Windows Update online · componenti software")
            ],
            RelatedSoftwareSources:
            [
                new DriverSearchPassStatus(
                    "Windows Update online · componenti software",
                    "Completed", 1, "", 1)
            ]);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnverifiedPnpInventoryNeverClaimsZeroCorrelatedDriverGuid(
        bool partial)
    {
        var report = Report(partial);
        var rows = ReferenceCoveragePresentation.Create(
            [Gpu()], report, inventoryComplete: false);
        var gpu = rows[5];

        Assert.Contains("non verificato", gpu.PnpStatus);
        Assert.Contains("non valutabili", gpu.WindowsDriverStatus);
        Assert.DoesNotContain("0 candidati", gpu.WindowsDriverStatus);
        Assert.DoesNotContain("1 candidati", gpu.WindowsDriverStatus);
        Assert.Equal(partial,
            gpu.WindowsDriverStatus.Contains("parziale",
                StringComparison.OrdinalIgnoreCase));
        Assert.Contains("Ripetere l'inventario", gpu.NextStep);
        Assert.False(gpu.IsInstallable);

        var copy = ScanCoverageText.Create(
            report, [], 1, 0, 0, [Gpu()], inventoryComplete: false);
        Assert.Contains("Windows Update: 1 candidati distinti", copy);
        Assert.Contains("inventario PnP non verificato", copy);
        Assert.Contains("associazioni WUA Type=Driver per ID non valutabili", copy);
        Assert.DoesNotContain("0 candidati WUA Type=Driver correlati per ID", copy);
        Assert.DoesNotContain(GpuId, copy);
        Assert.DoesNotContain(GpuGuid, copy);
        Assert.DoesNotContain("PRIVATE_", copy);
    }

    [Fact]
    public void UnverifiedInventoryDoesNotInvalidateIndependentSoftwareHints()
    {
        var rows = ReferenceCoveragePresentation.Create(
            [Gpu()], Report(), inventoryComplete: false);
        Assert.Contains("1 titoli Software WUA", rows[3].SoftwareStatus);
        Assert.Contains("Non è un candidato driver", rows[3].WindowsDriverStatus);
        Assert.Contains("NON installabile", rows[3].NextStep);
    }

    [Fact]
    public void FailedWuaSourceStillReportsUnverifiedSearchNotMissingDriver()
    {
        var report = new DriverSearchReport([Offer()],
        [
            new DriverSearchPassStatus("Windows Update online",
                "NetworkError", 0, "PRIVATE_WSUS_ENDPOINT")
        ]);
        var gpu = ReferenceCoveragePresentation.Create(
            [Gpu()], report, inventoryComplete: false)[5];
        Assert.Contains("Ricerca driver Windows non verificata",
            gpu.WindowsDriverStatus);
        Assert.DoesNotContain("0 candidati", gpu.WindowsDriverStatus);
        Assert.DoesNotContain("non valutabili", gpu.WindowsDriverStatus);
        var copy = ScanCoverageText.Create(report, [], 1, 0, 0,
            [Gpu()], inventoryComplete: false);
        Assert.Contains("ricerca WUA Type=Driver non verificata", copy);
        Assert.DoesNotContain("PRIVATE_WSUS_ENDPOINT", copy);
    }

    [Fact]
    public void VerifiedInventoryStillRetainsExactMatchingAndNoFalseCaveat()
    {
        var report = Report();
        var gpu = ReferenceCoveragePresentation.Create(
            [Gpu()], report, inventoryComplete: true)[5];
        Assert.Contains("1 candidati driver Windows correlati per ID",
            gpu.WindowsDriverStatus);
        Assert.DoesNotContain("non valutabili", gpu.WindowsDriverStatus);
        var text = ReferenceDriverCoverage.Create([Gpu()], report)[5]
            .DisplayLine(inventoryComplete: true,
                driverSourceQueried: true, softwareSourceQueried: true);
        Assert.Contains("1 candidati WUA Type=Driver correlati per ID", text);
        Assert.DoesNotContain("non valutabili", text);
    }
}
