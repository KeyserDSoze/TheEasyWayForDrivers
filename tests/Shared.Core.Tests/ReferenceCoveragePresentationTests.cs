using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;
using TheEasyWayForDrivers.Core.Update;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

public sealed class ReferenceCoveragePresentationTests
{
    private static DriverInfo Device(
        string privateInstance, string name, string hardwareId, string deviceClass) =>
        new(privateInstance, name, "Microsoft", deviceClass, "Microsoft",
            "31.0.0.1", null, "driver.inf", true, true, 0, [hardwareId], []);

    private static DriverUpdateInfo Wua(string id, string hardwareId,
        IReadOnlyList<string>? additional = null) =>
        new(id, "Synthetic update", null, "Display", "Microsoft", null,
            null, false, "Microsoft", null, hardwareId, null,
            SearchSource: "Windows Update online",
            AdditionalHardwareIds: additional);

    private static DriverSearchPassStatus Success(string source) =>
        new(source, "Completed", 1, "OK");

    private static DriverSearchPassStatus Failure(string source) =>
        new(source, "Failed", 0, "private:\\User\\Data");

    [Fact]
    public void EightReferenceCategoriesStayInformationalWithoutPnpIds()
    {
        var rows = ReferenceCoveragePresentation.Create([], null, false);

        Assert.Equal(8, rows.Count);
        Assert.All(rows, row => Assert.False(row.IsInstallable));
        Assert.All(rows, row => Assert.DoesNotContain("private", string.Join(" ",
            row.Component, row.PnpStatus, row.WindowsDriverStatus,
            row.SoftwareStatus, row.NextStep)));
        Assert.All(rows, row => Assert.True(
            row.WindowsDriverStatus.Contains("non verificat",
                StringComparison.OrdinalIgnoreCase) ||
            row.WindowsDriverStatus.Contains("Non è un candidato",
                StringComparison.Ordinal)));
        Assert.Contains(rows, row => row.Component == "NVIDIA · GPU");
        Assert.Contains(rows, row => row.Component == "Killer · Networking Software");
    }

    [Fact]
    public void GenericNvidiaGpuCanBeRecognizedWithoutWuaUpdate()
    {
        var device = Device("PRIVATE_GPU_INSTANCE",
            "Microsoft Basic Display Adapter",
            @"PCI\VEN_10DE&DEV_28A0", "Display");
        var report = new DriverSearchReport([],
            [Success("Windows Update online")]);

        var rows = ReferenceCoveragePresentation.Create([device], report, true);
        var gpu = Assert.Single(rows, row => row.Component == "NVIDIA · GPU");

        Assert.Contains("1 dispositivo", gpu.PnpStatus);
        Assert.Contains("0 candidati driver Windows", gpu.WindowsDriverStatus);
        Assert.Contains("verificare sul sito", gpu.NextStep);
        Assert.DoesNotContain("aggiornato", gpu.WindowsDriverStatus);
        Assert.DoesNotContain("PRIVATE_GPU_INSTANCE", gpu.PnpStatus + gpu.NextStep);
    }

    [Fact]
    public void PartiallySucceededDriverSourceNeverClaimsFullCoverage()
    {
        var device = Device("PRIVATE_NVIDIA", "Microsoft Basic Display Adapter",
            @"PCI\VEN_10DE&DEV_28A0", "Display");
        var report = new DriverSearchReport([], [
            Success("Windows configurato"),
            Failure("Windows Update online")
        ]);

        var gpu = Assert.Single(ReferenceCoveragePresentation.Create(
            [device], report, true), row => row.Component == "NVIDIA · GPU");
        Assert.Contains("ricerca parziale", gpu.WindowsDriverStatus);
        Assert.DoesNotContain("aggiornato", gpu.WindowsDriverStatus);
    }

    [Fact]
    public void FailedDriverSourceNeverDisplaysDefinitiveZeroUpdates()
    {
        var device = Device("PRIVATE_NVIDIA", "Microsoft Basic Display Adapter",
            @"PCI\VEN_10DE&DEV_28A0", "Display");
        var report = new DriverSearchReport([], [
            Failure("Windows configurato"),
            Failure("Windows Update online")
        ]);

        var gpu = Assert.Single(ReferenceCoveragePresentation.Create(
            [device], report, true), row => row.Component == "NVIDIA · GPU");
        Assert.Contains("non verificata", gpu.WindowsDriverStatus);
        Assert.DoesNotContain("0 candidati", gpu.WindowsDriverStatus);
    }

    [Fact]
    public void IntelWifiExtensionTypeDriverIsShownWithoutBecomingInstallable()
    {
        const string id = @"SWC\VEN_8086&DEV_51F1";
        var extension = Device("PRIVATE_PROSET",
            "Intel Wi-Fi 6 AX201 Extension", id, "Extension") with
        {
            Manufacturer = "Intel", DriverProvider = "Intel"
        };
        var report = new DriverSearchReport(
            [Wua(Guid.NewGuid().ToString("D"), id)],
            [Success("Windows Update online")],
            [new WuaDriverRelatedSoftware(
                Guid.NewGuid().ToString("D"),
                "Intel PROSet/Wireless WiFi Software extension",
                "Windows Update online · componenti software")],
            [Success("Windows Update online · componenti software")]);

        var row = Assert.Single(ReferenceCoveragePresentation.Create(
            [extension], report, true),
            result => result.Component == "Intel · PROSet / Wi-Fi extension");

        Assert.Contains("1 dispositivo", row.PnpStatus);
        Assert.Contains("1 candidati driver Windows correlati per ID",
            row.WindowsDriverStatus);
        Assert.Contains("1 titoli Software WUA", row.SoftwareStatus);
        Assert.False(row.IsInstallable);
        Assert.DoesNotContain("PRIVATE_PROSET", row.PnpStatus +
            row.WindowsDriverStatus + row.SoftwareStatus + row.NextStep);
    }

    [Fact]
    public void SoftwareOnlyHintsHaveNoInventedPnpDeviceOrDriverMatch()
    {
        var report = new DriverSearchReport(
            [], [Success("Windows Update online")],
            [new WuaDriverRelatedSoftware(
                Guid.NewGuid().ToString("D"), "Killer Networking Software",
                "Windows Update online · componenti software")],
            [Success("Windows Update online · componenti software")]);

        var row = Assert.Single(ReferenceCoveragePresentation.Create([], report, true),
            row => row.Component == "Killer · Networking Software");
        Assert.Contains("non associato", row.PnpStatus);
        Assert.Contains("Non è un candidato driver", row.WindowsDriverStatus);
        Assert.Contains("1 titoli Software", row.SoftwareStatus);
        Assert.Contains("NON installabile", row.NextStep);
        Assert.False(row.IsInstallable);
    }

    [Fact]
    public void IncompleteSoftwareSearchIsVisibleButDoesNotPoisonDriverStatus()
    {
        var report = new DriverSearchReport(
            [], [Success("Windows Update online")],
            [new WuaDriverRelatedSoftware(
                Guid.NewGuid().ToString("D"), "Intel PROSet Wireless",
                "Windows Update online · componenti software")],
            [Success("Windows Update online · componenti software"),
             Failure("Microsoft Update online · componenti software")]);

        Assert.False(report.IsPartial);
        Assert.True(report.SoftwareSearchIsPartial);
        var rows = ReferenceCoveragePresentation.Create([], report, true);
        var intel = Assert.Single(rows,
            row => row.Component == "Intel · PROSet / Wi-Fi extension");
        Assert.Contains("ricerca parziale", intel.SoftwareStatus);
        var gpu = Assert.Single(rows,
            row => row.Component == "NVIDIA · GPU");
        Assert.DoesNotContain("ricerca parziale", gpu.WindowsDriverStatus);
    }

    [Fact]
    public void MultipleWuaHardwareIdsMatchOnlyOneUpdateButTwoDevices()
    {
        var gpu = Device("PRIVATE_GPU", "Microsoft Basic Display Adapter",
            @"PCI\VEN_10DE&DEV_28A0", "Display");
        var audio = Device("PRIVATE_AUDIO", "High Definition Audio Device",
            @"HDAUDIO\FUNC_01&VEN_10DE&DEV_009B", "MEDIA");
        var report = new DriverSearchReport(
            [Wua(Guid.NewGuid().ToString("D"), gpu.HardwareIds[0],
                [audio.HardwareIds[0]])],
            [Success("Windows Update online")]);

        var rows = ReferenceCoveragePresentation.Create([gpu, audio], report, true);
        Assert.Contains("1 candidati", rows[5].WindowsDriverStatus);
        Assert.Contains("1 candidati", rows[6].WindowsDriverStatus);
        Assert.All(rows, row => Assert.False(row.IsInstallable));
    }

    [Fact]
    public void CopiedReportLabelsPartialDriverAndSoftwarePasses()
    {
        var device = Device("PRIVATE_DEVICE", "Microsoft Basic Display Adapter",
            @"PCI\VEN_10DE&DEV_28A0", "Display");
        var report = new DriverSearchReport(
            [], [Success("Windows configurato"), Failure("Windows Update online")],
            RelatedSoftwareSources:
            [Success("Windows Update online · componenti software"),
             Failure("Microsoft Update online · componenti software")]);

        var output = ScanCoverageText.Create(report, [], 1, 0, 0,
            [device], inventoryComplete: true);
        Assert.Contains("Type=Driver correlati per ID · ricerca parziale", output);
        Assert.Contains("Type=Software (senza matching PnP) · ricerca parziale", output);
        Assert.DoesNotContain("PRIVATE_DEVICE", output);
        Assert.DoesNotContain(@"PCI\VEN_10DE", output);
    }
}
