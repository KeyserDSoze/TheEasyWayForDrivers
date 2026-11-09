using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;
using TheEasyWayForDrivers.Core.Update;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

public sealed class ReferenceDriverCoverageTests
{
    private static DriverInfo Device(string instanceId, string title,
        string hardwareId, string deviceClass) =>
        new(instanceId, title, "Microsoft", deviceClass, "Microsoft",
            "31.0.0.1", null, "driver.inf", true, true, 0,
            [hardwareId], []);

    private static DriverUpdateInfo Wua(string hardwareId, string? id = null) =>
        new(id ?? Guid.NewGuid().ToString("D"), "Sample Driver", null, null,
            "Microsoft", null, null, false, "Microsoft", null,
            hardwareId, null);

    private static DriverSearchPassStatus Pass(string type) =>
        new(type, "Completed", 2, "queried");

    [Fact]
    public void EightReferenceRowsSeparatePnPDriverMatchesAndUnboundSoftwareHints()
    {
        var devices = new[]
        {
            Device("private-01", "Basic Display", @"PCI\VEN_8086&DEV_46A6", "Display"),
            Device("private-02", "Generic Bluetooth", @"USB\VID_8087&PID_0033", "Bluetooth"),
            Device("private-03", "Killer Wi-Fi 6E", @"PCI\VEN_8086&DEV_51F1", "Net"),
            Device("private-04", "Microsoft Basic Display Adapter",
                @"PCI\VEN_10DE&DEV_28A0", "Display"),
            Device("private-05", "High Definition Audio Device",
                @"HDAUDIO\FUNC_01&VEN_10DE&DEV_009B", "MEDIA"),
            Device("private-06", "System controller", @"ACPI\NVDA0820", "System")
        };
        var report = new DriverSearchReport(
            [Wua(@"PCI\VEN_8086&DEV_46A6"),
             Wua(@"PCI\VEN_8086&DEV_51F1")],
            [Pass("Windows Update online")],
            [
                new WuaDriverRelatedSoftware(Guid.NewGuid().ToString("D"),
                    "Intel PROSet/Wireless WiFi Software extension",
                    "Windows Update online · componenti software"),
                new WuaDriverRelatedSoftware(Guid.NewGuid().ToString("D"),
                    "Killer Networking Software",
                    "Windows Update online · componenti software"),
                new WuaDriverRelatedSoftware(Guid.NewGuid().ToString("D"),
                    "NVIDIA Platform Controller",
                    "Windows Update online · componenti software")
            ],
            [Pass("Windows Update online · componenti software")]);

        var rows = ReferenceDriverCoverage.Create(devices, report);

        Assert.Equal(8, rows.Count);
        Assert.Equal(6, rows.Sum(row => row.Devices));
        Assert.Equal(2, rows.Sum(row => row.WindowsDriverCandidates));
        Assert.Equal(3, rows.Sum(row => row.SoftwareTitleHints));
        Assert.Equal(1, rows[0].Devices);
        Assert.Equal(1, rows[0].WindowsDriverCandidates);
        Assert.Equal(1, rows[1].Devices);
        Assert.False(rows[2].SoftwareOnly);
        Assert.Equal(1, rows[2].SoftwareTitleHints);
        Assert.True(rows[3].SoftwareOnly);
        Assert.Equal(1, rows[3].SoftwareTitleHints);
        Assert.Equal(1, rows[4].Devices);
        Assert.Equal(1, rows[4].WindowsDriverCandidates);
        Assert.Equal(1, rows[5].Devices);
        Assert.Equal(1, rows[6].Devices);
        Assert.Equal(1, rows[7].Devices);
        Assert.Equal(1, rows[7].SoftwareTitleHints);

        var exported = ScanCoverageText.Create(report, [], 6, 0, 0,
            devices, inventoryComplete: true);
        Assert.Contains("Confronto categorie Intel/Killer/NVIDIA", exported);
        Assert.Contains("NVIDIA · Platform Controllers: 1 dispositivi", exported);
        Assert.Contains("Intel · PROSet / Wi-Fi extension: 0 dispositivi classificati; 0 candidati WUA Type=Driver correlati per ID; 1 titoli WUA Type=Software", exported);
        Assert.DoesNotContain("private-0", exported);
        Assert.DoesNotContain(@"PCI\VEN_", exported);
        Assert.DoesNotContain(@"HDAUDIO\", exported);
        Assert.DoesNotContain(@"ACPI\NVDA", exported);
        Assert.DoesNotContain("Killer Networking Software", exported);
        Assert.DoesNotContain("Intel PROSet/Wireless WiFi Software extension", exported);
        Assert.Contains("non entrano in Aggiorna tutto", exported);
    }

    [Fact]
    public void MicrosoftGenericKillerAx1675iAppearsInEightComponentMatrix()
    {
        const string id = @"PCI\VEN_8086&DEV_51F1&SUBSYS_16721A56";
        var adapter = Device("PRIVATE_KILLER", "Network Controller", id, "Net");
        var guid = Guid.NewGuid().ToString("D");
        var report = new DriverSearchReport([Wua(id, guid), Wua(id, guid)],
            [Pass("Windows Update online")]);
        var rows = ReferenceDriverCoverage.Create([adapter], report);
        Assert.Equal(1, rows[4].Devices);
        Assert.Equal(1, rows[4].WindowsDriverCandidates);
        Assert.Equal(0, rows[1].Devices);
        Assert.Equal(1, rows.Sum(row => row.Devices));

        var copy = ScanCoverageText.Create(report, [], 1, 0, 0,
            [adapter], inventoryComplete: true);
        Assert.Contains("Killer · scheda Wi-Fi: 1 dispositivi", copy);
        Assert.DoesNotContain("PRIVATE_KILLER", copy);
        Assert.DoesNotContain("SUBSYS_", copy);
        Assert.DoesNotContain(guid, copy);
    }

    [Fact]
    public void GenericNvidiaSwcComponentIsCountedAsPlatformWithExactWuaMatch()
    {
        const string swc = @"SWC\VEN_10DE&DEV_28A0";
        var component = Device("PRIVATE_DCH_001", "Software Component",
            swc, "SoftwareComponent");
        var updateGuid = Guid.NewGuid().ToString("D");
        var report = new DriverSearchReport(
            [Wua(swc, updateGuid), Wua(swc, updateGuid)],
            [Pass("Windows Update online")]);

        var rows = ReferenceDriverCoverage.Create(
            [component, component with { DeviceId = "private_dch_001" }],
            report);
        Assert.Equal(1, rows[7].Devices);
        Assert.Equal(1, rows[7].WindowsDriverCandidates);
        Assert.Equal(1, rows.Sum(row => row.Devices));
        Assert.Equal(1, rows.Sum(row => row.WindowsDriverCandidates));
        Assert.False(rows[7].SoftwareOnly);

        var exported = ScanCoverageText.Create(report, [], 1, 0, 0,
            [component], inventoryComplete: true);
        Assert.Contains("NVIDIA · Platform Controllers: 1 dispositivi", exported);
        Assert.DoesNotContain("PRIVATE_DCH", exported);
        Assert.DoesNotContain(swc, exported);
    }

    [Fact]
    public void NvidiaCompatibleSwcIsWeakMatchButStillRecognizedAsPlatform()
    {
        const string swc = @"SWC\VEN_10DE&DEV_28A0";
        var component = Device("PRIVATE_COMPONENT", "System Controller",
            @"ROOT\SOFTWARE", "System") with { CompatibleIds = [swc] };
        var report = new DriverSearchReport([Wua(swc)],
            [Pass("Windows Update online")]);

        var row = ReferenceDriverCoverage.Create([component], report)[7];
        Assert.Equal(1, row.Devices);
        Assert.Equal(1, row.WindowsDriverCandidates);
        Assert.False(NvidiaHardwareDetector.HasNvidiaPlatformId(component with
        {
            CompatibleIds = [@"SWC\VEN_10DEF&DEV_28A0"]
        }));
    }

    [Theory]
    [InlineData(@"SWC\VEN_10DEF&DEV_28A0")]
    [InlineData(@"SWC\VEN_10DE&DEV_28A0XYZ")]
    [InlineData(@"ACPI\NVDA0820FAKE")]
    [InlineData(@"ACPI\NVDAZZZZ")]
    public void NearMissNvidiaPlatformIdDoesNotBecomeAnEighthComponent(string id)
    {
        var device = Device("PRIVATE_BAD", "Software Component", id,
            "SoftwareComponent");
        var report = new DriverSearchReport([Wua(@"SWC\VEN_10DE&DEV_28A0")],
            [Pass("Windows Update online")]);
        var rows = ReferenceDriverCoverage.Create([device], report);

        Assert.Equal(0, rows.Sum(row => row.Devices));
        Assert.Equal(0, rows.Sum(row => row.WindowsDriverCandidates));
    }

    [Fact]
    public void SoftwareTitleNeverPretendsThereIsMatchingHardwareOrDriverUpdate()
    {
        var title = new WuaDriverRelatedSoftware(
            Guid.NewGuid().ToString("D"), "NVIDIA Platform Controller",
            "Windows Update online · componenti software");
        var report = new DriverSearchReport([], [Pass("Windows Update online")],
            [title], [Pass("Windows Update online · componenti software")]);
        var row = ReferenceDriverCoverage.Create([], report)[7];

        Assert.Equal(0, row.Devices);
        Assert.Equal(0, row.WindowsDriverCandidates);
        Assert.Equal(1, row.SoftwareTitleHints);
        Assert.Contains("0 dispositivi classificati",
            row.DisplayLine(true, true, true));
        Assert.Contains("senza matching PnP", row.DisplayLine(true, true, true));
    }

    [Fact]
    public void IntelProsetExtensionDriverAndSoftwareHintAreCountedSeparately()
    {
        const string extensionId = @"SWC\VEN_8086&DEV_51F1";
        var extension = Device("PRIVATE_PROSET_EXTENSION",
            "Intel(R) PROSet/Wireless WiFi Software Extension",
            extensionId, "SoftwareComponent") with
        {
            Manufacturer = "Intel", DriverProvider = "Intel"
        };
        var matchedGuid = Guid.NewGuid().ToString("D");
        var report = new DriverSearchReport(
            [Wua(extensionId, matchedGuid),
             Wua(extensionId + "0")],
            [Pass("Windows Update online")],
            [new WuaDriverRelatedSoftware(Guid.NewGuid().ToString("D"),
                "Intel PROSet/Wireless WiFi Software extension",
                "Windows Update online · componenti software")],
            [Pass("Windows Update online · componenti software")]);

        var rows = ReferenceDriverCoverage.Create([extension], report);
        Assert.Equal(1, rows[2].Devices);
        Assert.Equal(1, rows[2].WindowsDriverCandidates);
        Assert.Equal(1, rows[2].SoftwareTitleHints);
        Assert.False(rows[2].SoftwareOnly);
        Assert.Equal(0, rows[3].WindowsDriverCandidates);

        var copied = ScanCoverageText.Create(report, [], 1, 0, 0,
            [extension], inventoryComplete: true);
        Assert.Contains("Intel · PROSet / Wi-Fi extension: 1 dispositivi", copied);
        Assert.Contains("1 candidati WUA Type=Driver correlati per ID", copied);
        Assert.DoesNotContain("PRIVATE_PROSET_EXTENSION", copied);
        Assert.DoesNotContain(extensionId, copied);
        Assert.DoesNotContain(matchedGuid, copied);
        Assert.DoesNotContain("Intel PROSet/Wireless WiFi Software extension", copied);
    }

    [Theory]
    [InlineData("Intel Wireless Bluetooth Extension", @"USB\VID_8087&PID_0033", "Extension", "Microsoft")]
    [InlineData("Intel Wireless Network Adapter", @"PCI\VEN_8086&DEV_51F1", "Net", "Microsoft")]
    [InlineData("Killer Wi-Fi Extension", @"PCI\VEN_8086&DEV_51F1", "Extension", "Intel")]
    [InlineData("Intel Audio Extension", @"PCI\VEN_8086&DEV_1234", "Extension", "Microsoft")]
    [InlineData("Intel PROSet WiFi", @"SWC\VEN_8086&DEV_51F1", "SoftwareComponent", "Intel")]
    [InlineData("Intel Wi-Fi Extension", @"PCI\VEN_80860&DEV_51F1", "Extension", "Microsoft")]
    public void OtherIntelComponentsAndLookalikeIdsAreNotProsetExtensions(
        string name, string hardwareId, string deviceClass, string manufacturer)
    {
        var device = Device("PRIVATE_NOT_PROSET", name,
            hardwareId, deviceClass) with
        {
            Manufacturer = manufacturer, DriverProvider = manufacturer
        };
        var rows = ReferenceDriverCoverage.Create([device],
            new DriverSearchReport([Wua(hardwareId)],
                [Pass("Windows Update online")]));
        Assert.Equal(0, rows[2].Devices);
        Assert.Equal(0, rows[2].WindowsDriverCandidates);
    }

    [Fact]
    public void GenericIntelSwcIsNotAssumedToBeProsetButNamedWifiExtensionIs()
    {
        const string id = @"SWC\VEN_8086&DEV_51F1";
        var generic = Device("PRIVATE_DCH_GENERIC", "Software Component",
            id, "SoftwareComponent");
        var named = Device("PRIVATE_DCH_WIFI", "Intel Wi-Fi Extension",
            id, "SoftwareComponent");
        var report = new DriverSearchReport([Wua(id)],
            [Pass("Windows Update online")]);

        Assert.Equal(0, ReferenceDriverCoverage.Create([generic], report)[2].Devices);
        var row = ReferenceDriverCoverage.Create([named], report)[2];
        Assert.Equal(1, row.Devices);
        Assert.Equal(1, row.WindowsDriverCandidates);
    }

    [Fact]
    public void PartialOrMissingScanNeverClaimsZeroSources()
    {
        var devices = new[]
        {
            Device("private-07", "Basic Display",
                @"PCI\VEN_10DE&DEV_28A0", "Display")
        };
        var unverified = ScanCoverageText.Create(null, [], 0, 0, 0,
            devices, inventoryComplete: false);

        Assert.Contains("inventario PnP non verificato", unverified);
        Assert.Contains("ricerca WUA Type=Driver non verificata", unverified);
        Assert.Contains("ricerca WUA Type=Software non verificata", unverified);
        Assert.DoesNotContain("private-07", unverified);

        var noUpdate = new DriverSearchReport([],
            [Pass("Windows Update online")]);
        var afterVerifiedQuery = ReferenceDriverCoverage.Create(devices, noUpdate)[5];
        Assert.Equal(1, afterVerifiedQuery.Devices);
        Assert.Contains("0 candidati WUA Type=Driver",
            afterVerifiedQuery.DisplayLine(true, true, false));
    }

    [Fact]
    public void DeduplicatesInstancesAndUpdateIdsWithoutCountingBrandPrefixes()
    {
        var baseDevice = Device("PRIV-DEVICE", "Basic Display",
            @"PCI\VEN_8086&DEV_46A6", "Display");
        var lookalike = Device("OTHER", "Basic Display",
            @"PCI\VEN_80860&DEV_46A6", "Display");
        var updateId = Guid.NewGuid().ToString("D");
        var report = new DriverSearchReport(
            [Wua(@"PCI\VEN_8086&DEV_46A6", updateId),
             Wua(@"PCI\VEN_8086&DEV_46A6", updateId)],
            [Pass("Windows Update online")]);
        var rows = ReferenceDriverCoverage.Create(
            [baseDevice, baseDevice with { DeviceId = "priv-device" }, lookalike],
            report);

        Assert.Equal(1, rows[0].Devices);
        Assert.Equal(1, rows[0].WindowsDriverCandidates);
    }
}
