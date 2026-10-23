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
    public void IrrelevantDevicesAreNotShownAsDriverUpdates()
    {
        var drivers = new[]
        {
            Driver("Intel Host Bridge", "2", "Intel", @"PCI\VEN_8086&DEV_0001"),
            Driver("Unknown webcam", "1", "Other", @"USB\VID_0001")
        };
        Assert.Empty(VendorDriverEvidenceFinder.Find(drivers, []));
    }
}
