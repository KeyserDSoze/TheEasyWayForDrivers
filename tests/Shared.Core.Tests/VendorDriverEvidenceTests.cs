using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

public sealed class VendorDriverEvidenceTests
{
    private static DriverInfo Driver(
        string name, string version, string manufacturer, string hardwareId) =>
        new(hardwareId, name, manufacturer, "Display", manufacturer,
            version, null, null, true, true, 0, [hardwareId], []);

    [Fact]
    public void NvidiaLaptopGpuWithoutWindowsUpdateCandidateIsNotDeclaredCurrent()
    {
        var results = VendorDriverEvidenceFinder.Find(
            [Driver("NVIDIA GeForce RTX 4060 Laptop GPU", "31.0.15.5000",
                "NVIDIA", @"PCI\VEN_10DE&DEV_28A0")], []);

        var result = Assert.Single(results);
        Assert.Equal("NVIDIA", result.Vendor);
        Assert.Contains("31.0.15.5000", result.DisplayLine);
        Assert.Contains("stato OEM non verificato", result.DisplayLine);
        Assert.DoesNotContain("aggiornato", result.DisplayLine);
    }

    [Fact]
    public void IdenticalHardwareIdIsOnlyACorrelatedCandidate_NotInstallApproval()
    {
        var id = @"PCI\VEN_8086&DEV_1234";
        var driver = Driver("Intel(R) UHD Graphics", "31.0.100", "Intel", id);
        var update = new DriverUpdateInfo("guid", "Intel Graphics", null, null, "Intel",
            null, null, false, "Intel", null, id, null);

        var result = Assert.Single(VendorDriverEvidenceFinder.Find([driver], [update]));
        Assert.Equal(1, result.CorrelatedWuaCandidates);
        Assert.Contains("compatibilità da verificare", result.DisplayLine);
    }

    [Fact]
    public void KillerAdapterAndBluetoothAreTrackedWithoutBeingApproved()
    {
        var drivers = new[]
        {
            Driver("Killer(R) Wi-Fi 6E AX1675i", "23.2", "Intel", @"PCI\VEN_8086&DEV_51F1"),
            Driver("Intel(R) Wireless Bluetooth(R)", "23.1", "Intel", @"USB\VID_8087&PID_0033")
        };

        var results = VendorDriverEvidenceFinder.Find(drivers, []);
        Assert.Equal(2, results.Count);
        Assert.Contains(results, d => d.Vendor == "Killer / Intel");
        Assert.Contains(results, d => d.Vendor == "Intel");
    }

    [Fact]
    public void KillerWithGenericMicrosoftNameHasInstalledVersionEvidence()
    {
        const string id = @"PCI\VEN_8086&DEV_51F1&SUBSYS_16721A56";
        var card = Driver("Network Controller", "24.0.0.1",
            "Microsoft", id) with { DeviceClass = "Net" };
        var row = Assert.Single(VendorDriverEvidenceFinder.Find([card], []));
        Assert.Equal("Killer / Intel", row.Vendor);
        Assert.Equal(0, row.CorrelatedWuaCandidates);
        Assert.Contains("stato OEM non verificato", row.DisplayLine);
        Assert.DoesNotContain(id, row.DisplayLine);
    }

    [Fact]
    public void WuaFailureDoesNotPretendToHaveZeroCorrelatedUpdates()
    {
        var driver = Driver("NVIDIA GeForce RTX 4060 Laptop GPU", "31.0.15.5000",
            "NVIDIA", @"PCI\VEN_10DE&DEV_28A0");

        var result = Assert.Single(VendorDriverEvidenceFinder.Find(
            [driver], [], wuaWasQueried: false));

        Assert.Contains("non verificata", result.DisplayLine);
        Assert.DoesNotContain("nessun candidato", result.DisplayLine);
    }

    [Fact]
    public void GenericNvidiaAudioAndPlatformControllersAreVisibleInInventoryEvidence()
    {
        var audio = Driver("High Definition Audio Device", "1.0.0.1",
            "Microsoft", @"HDAUDIO\FUNC_01&VEN_10DE&DEV_009B") with
        {
            DeviceClass = "MEDIA"
        };
        var controller = Driver("System controller", "2.0.0.1",
            "Microsoft", @"ACPI\NVDA0820") with
        {
            DeviceClass = "System"
        };

        var results = VendorDriverEvidenceFinder.Find([audio, controller], []);
        Assert.Equal(2, results.Count);
        Assert.All(results, r => Assert.Equal("NVIDIA", r.Vendor));
        Assert.All(results, r => Assert.Contains("stato OEM non verificato", r.DisplayLine));
    }

    [Fact]
    public void AmdGraphicsHasInstalledEvidenceWithoutInventingVendorUpdate()
    {
        var device = Driver("Microsoft Basic Display Adapter", "31.0.0.1",
            "Microsoft", @"PCI\VEN_1002&DEV_744C");
        var row = Assert.Single(VendorDriverEvidenceFinder.Find([device], []));
        Assert.Equal("AMD", row.Vendor);
        Assert.Equal(0, row.CorrelatedWuaCandidates);
    }

    [Fact]
    public void IrrelevantDevicesAreNotShownAsDriverUpdates()
    {
        var drivers = new[]
        {
            Driver("Intel Host Bridge", "2", "Intel", @"PCI\VEN_8086&DEV_0001"),
            Driver("Unknown webcam", "1", "Other", @"USB\VID_0001")
        };
        Assert.Empty(VendorDriverEvidenceFinder.Find(drivers, []));
    }

    [Fact]
    public void GenericIntelNetworkAndBluetoothAppearByHardwareVendorAndClass()
    {
        var network = Driver("Network Controller", "23.2.1", "Microsoft",
            @"PCI\VEN_8086&DEV_51F1") with { DeviceClass = "Net" };
        var bluetooth = Driver("Generic radio device", "2.0.1", "Microsoft",
            @"USB\VID_8087&PID_0033") with { DeviceClass = "Bluetooth" };

        var update = new DriverUpdateInfo(
            Guid.NewGuid().ToString("D"), "Windows wireless driver",
            null, "Net", null, "24.1.0", null, false,
            null, null, @"PCI\VEN_8086&DEV_51F1", null);

        var rows = VendorDriverEvidenceFinder.Find(
            [network, bluetooth], [update]);

        Assert.Equal(2, rows.Count);
        Assert.All(rows, row => Assert.Equal("Intel", row.Vendor));
        var networkRow = Assert.Single(rows, row => row.Device == "Network Controller");
        Assert.Equal(1, networkRow.CorrelatedWuaCandidates);
        Assert.Contains("23.2.1", networkRow.DisplayLine);
        Assert.Contains("stato OEM non verificato",
            Assert.Single(rows, row => row.Device == "Generic radio device").DisplayLine);
    }

    [Fact]
    public void CompatibleIdIntelNetworkGetsReadOnlyEvidenceWithoutInventingUpdate()
    {
        var row = Driver("Network Controller", "non disponibile", "Microsoft",
            @"PCI\VEN_9999&DEV_51F1") with
        {
            DeviceClass = "Net",
            CompatibleIds = [@"PCI\VEN_8086&DEV_51F1"]
        };

        var found = Assert.Single(VendorDriverEvidenceFinder.Find([row], []));
        Assert.Equal("Intel", found.Vendor);
        Assert.Equal(0, found.CorrelatedWuaCandidates);
        Assert.Contains("stato OEM non verificato", found.DisplayLine);
    }

    [Fact]
    public void GenericIntelHostBridgeAndSimilarVendorPrefixDoNotLookActionable()
    {
        var bridge = Driver("Generic PCI bridge", "1.0", "Microsoft",
            @"PCI\VEN_8086&DEV_1234") with { DeviceClass = "System" };
        var fake = Driver("Network Controller", "1.0", "Microsoft",
            @"PCI\VEN_80860&DEV_1234") with { DeviceClass = "Net" };

        Assert.Empty(VendorDriverEvidenceFinder.Find([bridge, fake], []));
    }
}
