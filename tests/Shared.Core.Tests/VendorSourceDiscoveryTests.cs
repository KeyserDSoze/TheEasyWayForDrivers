using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

public sealed class VendorSourceDiscoveryTests
{
    private static DriverInfo Device(string name, string maker, string id,
        string version = "31.0.0.1000") =>
        new(id, name, maker, "Net", maker, version, null, null,
            true, true, 0, [id], []);

    [Fact]
    public void RecognizesReferenceHardwareButDoesNotInventAvailableDriver()
    {
        var results = VendorSourceDiscovery.Find([
            Device("Intel(R) UHD Graphics", "Intel", @"PCI\VEN_8086&DEV_1234"),
            Device("Intel(R) Wireless Bluetooth(R)", "Intel", @"USB\VID_8087&PID_0033"),
            Device("Killer(R) Wi-Fi 6E AX1675i 160MHz Wireless Network Adapter (211NGW)",
                "Intel", @"PCI\VEN_8086&DEV_51F1"),
            Device("NVIDIA GeForce RTX 4060 Laptop GPU", "NVIDIA",
                @"PCI\VEN_10DE&DEV_28A0"),
            Device("NVIDIA High Definition Audio", "NVIDIA",
                @"HDAUDIO\FUNC_01&VEN_10DE"),
            Device("NVIDIA Platform Controllers and Framework", "NVIDIA",
                @"ACPI\NVDA0820")
        ], []);

        Assert.Equal(6, results.Count);
        Assert.All(results, item => Assert.Equal(0, item.WuaMatches));
        Assert.All(results, item => Assert.Contains(
            "nessun aggiornamento OEM confermato", item.StatusText));
        Assert.Contains(results, item =>
            item.DeviceName.Contains("Killer") &&
            item.OfficialUrl == VendorSourceDiscovery.IntelWifiUrl);
        Assert.Contains(results, item =>
            item.DeviceName.Contains("Bluetooth") &&
            item.OfficialUrl == VendorSourceDiscovery.IntelBluetoothUrl);
        Assert.Contains(results, item =>
            item.DeviceName.Contains("RTX 4060") &&
            item.OfficialUrl == VendorSourceDiscovery.NvidiaDriversUrl);
    }

    [Fact]
    public void WuaMatchDoesNotPretendVendorCatalogWasQueried()
    {
        var id = @"PCI\VEN_8086&DEV_51F1";
        var update = new DriverUpdateInfo(Guid.NewGuid().ToString("D"),
            "Intel Network Driver", null, null, "Intel", null, null,
            false, "Intel", null, id, null);
        var row = Assert.Single(VendorSourceDiscovery.Find([
            Device("Killer Wi-Fi AX1675i", "Intel", id)], [update]));
        Assert.Equal(1, row.WuaMatches);
        Assert.Contains("Windows Update", row.StatusText);
        Assert.Contains("Installato:", row.Detail);
    }

    [Fact]
    public void NoMatchingHardwareNoFalseCandidate()
    {
        var results = VendorSourceDiscovery.Find([
            Device("USB Camera", "Other", @"USB\VID_9876&PID_1234"),
            Device("PCI Express Root Port", "Intel", @"PCI\VEN_8086&DEV_4567")
        ], []);
        Assert.Empty(results);
    }

    [Fact]
    public void RepeatedDeviceIdentityIsNotCountedTwice()
    {
        var device = Device("Intel UHD Graphics", "Intel",
            @"PCI\VEN_8086&DEV_1234");
        Assert.Single(VendorSourceDiscovery.Find([device, device], []));
    }

    [Theory]
    [InlineData("https://www.intel.com/content/www/us/en/download/19351/hello")]
    [InlineData("https://www.nvidia.com/Download/Find.aspx")]
    [InlineData("https://www.amd.com/en/support/download/drivers.html")]
    public void TrustedDestinationsAreHttpsAllowlisted(string url) =>
        Assert.True(VendorSourceDiscovery.IsTrustedOfficialUrl(url));

    [Theory]
    [InlineData("http://www.intel.com/download")]
    [InlineData("https://www.intel.com.evil.example/path")]
    [InlineData("https://user:pass@www.nvidia.com/path")]
    [InlineData("https://www.amd.com:8443/path")]
    [InlineData("file:///c:/temp")]
    public void UntrustedDestinationsAreRejected(string url) =>
        Assert.False(VendorSourceDiscovery.IsTrustedOfficialUrl(url));
}
