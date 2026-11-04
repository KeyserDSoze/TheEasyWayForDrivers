using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

public sealed class VendorCoverageMatrixTests
{
    private static DriverInfo Device(
        string name, string hardwareId, string deviceClass, string maker = "Microsoft") =>
        new(
            hardwareId + "\\CURRENT",
            name, maker, deviceClass, maker, "31.0.0.1000",
            null, "test.inf", true, true, 0, [hardwareId], []);

    private static DriverUpdateInfo Wua(string id, string hardwareId) =>
        new(id, "WUA published driver", null, "Net", "Microsoft", null,
            null, false, "Microsoft", null, hardwareId, null);

    [Fact]
    public void EightDeviceFixtureWithTwoWuaMatchesIsNotEightConfirmedUpdates()
    {
        // Regression fixture models categories from #91; this is synthetic,
        // not data read from the user's RTX 4060 machine.
        var drivers = new[]
        {
            Device("Microsoft Basic Display Adapter", @"PCI\VEN_10DE&DEV_28A0", "Display"),
            Device("High Definition Audio Device", @"HDAUDIO\FUNC_01&VEN_10DE&DEV_009B", "MEDIA"),
            Device("System controller", @"ACPI\NVDA0820", "System"),
            Device("Intel UHD Graphics", @"PCI\VEN_8086&DEV_46A6", "Display"),
            Device("Killer Wi-Fi 6E AX1675i", @"PCI\VEN_8086&DEV_51F1", "Net", "Intel"),
            Device("Intel Wireless Bluetooth", @"USB\VID_8087&PID_0033", "Bluetooth"),
            Device("Realtek Ethernet Controller", @"PCI\VEN_10EC&DEV_8125", "Net"),
            Device("MediaTek Wi-Fi Adapter", @"PCI\VEN_14C3&DEV_0608", "Net")
        };

        var matches = new[]
        {
            Wua("wua-killer", @"PCI\VEN_8086&DEV_51F1"),
            Wua("wua-intel-bt", @"USB\VID_8087&PID_0033")
        };

        var checks = VendorSourceDiscovery.Find(drivers, matches);
        Assert.Equal(8, checks.Count);
        Assert.Equal(2, checks.Count(x => x.WuaMatches > 0));
        Assert.Equal(0, checks.Where(x => x.Vendor == "NVIDIA")
            .Sum(x => x.WuaMatches));

        var matrix = VendorCoverageMatrix.Create(checks);
        Assert.Equal(8, matrix.Sum(group => group.Devices));
        Assert.Equal(2, matrix.Sum(group => group.DevicesWithWuaMatches));
        var nvidia = Assert.Single(matrix, x => x.Vendor == "NVIDIA");
        Assert.Equal(3, nvidia.Devices);
        Assert.Equal(0, nvidia.DevicesWithWuaMatches);
        Assert.Contains("disponibilità dei driver OEM non verificata",
            nvidia.DisplayLine(wuaWasQueried: true));
    }

    [Fact]
    public void MissingWuaQueryIsNeverInterpretedAsZeroAvailableDrivers()
    {
        var row = new VendorCoverageRow("NVIDIA", 3, 0, 0, 0);
        var text = row.DisplayLine(wuaWasQueried: false);
        Assert.Contains("non verificata", text);
        Assert.DoesNotContain("0 con candidati", text);
        Assert.DoesNotContain("aggiornato", text);
    }

    [Fact]
    public void OemMetadataIsNotCountedAsAnotherInstalledDevice()
    {
        var checks = new[]
        {
            new VendorSourceCheck("NVIDIA RTX", "NVIDIA", "31.0",
                VendorSourceDiscovery.NvidiaDriversUrl, "NVIDIA", "hardware ID", 1),
            new VendorSourceCheck("HPIA softpaq", "HP", "N/D",
                "https://support.hp.com/", "HP HPIA", "needed according to HP",
                0, IsCatalogMetadata: true, PublishedVersion: "32.0",
                IsVendorApplicable: true),
            new VendorSourceCheck("Lenovo package", "Lenovo", "N/D",
                "https://support.lenovo.com/", "Lenovo catalog", "package only",
                0, IsCatalogMetadata: true)
        };
        var row = Assert.Single(VendorCoverageMatrix.Create(checks));
        Assert.Equal("NVIDIA", row.Vendor);
        Assert.Equal(1, row.Devices);
        Assert.Equal(1, row.DevicesWithWuaMatches);
    }

    [Fact]
    public void UnknownInstalledVersionIsCountedButNeverAnUpdateClaim()
    {
        var row = Assert.Single(VendorCoverageMatrix.Create(
        [
            new VendorSourceCheck("GPU", "NVIDIA", "non disponibile",
                VendorSourceDiscovery.NvidiaDriversUrl, "NVIDIA", "id", 0)
        ]));
        Assert.Equal(1, row.DevicesWithoutInstalledVersion);
        Assert.Contains("1 senza versione installata", row.DisplayLine(true));
    }
}
