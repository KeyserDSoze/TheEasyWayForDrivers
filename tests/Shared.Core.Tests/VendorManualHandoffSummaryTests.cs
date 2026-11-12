using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

/// <summary>
/// The Desktop OEM panel shows only one MANUAL link per trusted vendor
/// destination, even if many generic WMI PnP devices share that link.
/// Exact WUA device associations remain diagnostic and manufacturer
/// assessed packages/news remain individual non-installable records.
/// </summary>
public sealed class VendorManualHandoffSummaryTests
{
    private static DriverInfo Device(string name, string id, string cls = "Net") =>
        new("PRIVATE_SERIAL_" + id, name, "Microsoft", cls, "Microsoft",
            "1.0", null, "PRIVATE_OEM42.inf", true, true, 0, [id], []);

    private static VendorSourceCheck Manual(string name, string vendor,
        string url, int wua = 0) =>
        new(name, vendor, "PRIVATE_VERSION_123",
            url, "PRIVATE_SOURCE_TITLE", "PRIVATE_PNP_ID",
            WuaMatches: wua);

    [Fact]
    public void HundredPcDevicesProduceFewSafeOfficialSourcesNotHundredHandoffs()
    {
        var intelFlood = Enumerable.Range(1, 140).Select(i =>
            Device("Network Controller", $@"PCI\VEN_8086&DEV_{i:X4}"));
        var gpu = Device("Microsoft Basic Display Adapter",
            @"PCI\VEN_10DE&DEV_28A0", "Display");
        var audio = Device("High Definition Audio Device",
            @"HDAUDIO\FUNC_01&VEN_10DE&DEV_009B", "MEDIA");
        var realtek = Device("Generic Ethernet Controller",
            @"PCI\VEN_10EC&DEV_8125");
        var killer = Device("Network Controller",
            @"PCI\VEN_8086&DEV_51F1&SUBSYS_16721A56");

        var raw = VendorSourceDiscovery.Find(
            intelFlood.Concat([gpu, audio, realtek, killer]), []);
        Assert.Equal(100, raw.Count);
        Assert.True(raw.Count(item => item.Vendor == "Intel") > 80);
        Assert.Contains(raw, item => item.Vendor == "NVIDIA");
        Assert.Contains(raw, item => item.Vendor == "Realtek");
        Assert.Contains(raw, item => item.Vendor == "Killer / Intel");

        var grouped = VendorManualHandoffSummary.Create(raw);
        Assert.InRange(grouped.Count, 4, 6);
        Assert.Equal(raw.Count, grouped.Sum(item =>
            item.GroupedManualDevices));
        Assert.Contains(grouped, item =>
            item.Vendor == "NVIDIA" &&
            item.GroupedManualDevices == 2 &&
            item.OfficialUrl == VendorSourceDiscovery.NvidiaDriversUrl);
        Assert.Contains(grouped, item =>
            item.Vendor == "Killer / Intel" &&
            item.OfficialUrl == VendorSourceDiscovery.IntelWifiUrl);
        Assert.All(grouped, item =>
        {
            Assert.True(item.GroupedManualDevices >= 1);
            Assert.False(item.IsVendorApplicable);
            Assert.False(item.IsInstallable);
            Assert.Equal(0, item.WuaMatches);
            Assert.True(VendorSourceDiscovery.IsTrustedOfficialUrl(item.OfficialUrl));
            Assert.Contains("NON aggiornamenti verificati", item.StatusText);
            Assert.DoesNotContain("PRIVATE", item.Title);
            Assert.DoesNotContain("PRIVATE", item.StatusText);
            Assert.DoesNotContain("PRIVATE", item.Detail);
            Assert.DoesNotContain("PRIVATE", item.Evidence);
        });
        // Source discovery retains individual evidence for the device
        // panels. UI aggregation does not change the PnP data/QA result.
        Assert.Equal(100, VendorCoverageMatrix.Create(raw)
            .Sum(row => row.Devices));
    }

    [Fact]
    public void IntelWirelessBluetoothAndDsaRemainThreeDifferentActions()
    {
        var rows = new[] {
            Manual("PRIVATE_WIFI", "Intel", VendorSourceDiscovery.IntelWifiUrl),
            Manual("PRIVATE_BLUETOOTH", "Intel", VendorSourceDiscovery.IntelBluetoothUrl),
            Manual("PRIVATE_GRAPHICS", "Intel", VendorSourceDiscovery.IntelDsaUrl),
            Manual("PRIVATE_KILLER", "Killer / Intel", VendorSourceDiscovery.IntelWifiUrl)
        };
        var grouped = VendorManualHandoffSummary.Create(rows);
        Assert.Equal(4, grouped.Count); // Vendor+URL, never collapse distinct links.
        Assert.Contains(grouped, x => x.SourceName == "Intel Bluetooth");
        Assert.Contains(grouped, x => x.SourceName == "Intel Driver & Support Assistant");
        Assert.Contains(grouped, x => x.SourceName == "Intel Wi-Fi / Killer" &&
            x.Vendor == "Intel");
        Assert.Contains(grouped, x => x.SourceName == "Intel Wi-Fi / Killer" &&
            x.Vendor == "Killer / Intel");
        Assert.Equal(4, grouped.Sum(x => x.GroupedManualDevices));
    }

    [Fact]
    public void WuaMatchesCountMatchedDevicesWithoutInventingOemUpdates()
    {
        var rows = new[] {
            Manual("PRIVATE_GPU1", "NVIDIA", VendorSourceDiscovery.NvidiaDriversUrl, 3),
            Manual("PRIVATE_GPU2", "NVIDIA", VendorSourceDiscovery.NvidiaDriversUrl, 1),
            Manual("PRIVATE_AUDIO", "NVIDIA", VendorSourceDiscovery.NvidiaDriversUrl),
            Manual("PRIVATE_RTK", "Realtek", VendorSourceDiscovery.RealtekSupportUrl)
        };
        var report = VendorManualHandoffSummary.Create(rows);
        var nvidia = Assert.Single(report, row => row.Vendor == "NVIDIA");
        Assert.Equal(3, nvidia.GroupedManualDevices);
        Assert.Equal(2, nvidia.GroupedManualDevicesWithWuaIds);
        Assert.Equal(0, nvidia.WuaMatches);
        Assert.Contains("2 con candidato WUA per ID", nvidia.StatusText);
        Assert.Contains("NON aggiornamenti verificati", nvidia.StatusText);
        Assert.False(nvidia.IsInstallable);
        Assert.False(nvidia.IsVendorApplicable);
        Assert.Single(report, row => row.Vendor == "Realtek");
    }

    [Fact]
    public void NewsAssessedOemMetadataAndUntrustedSitesAreNotGrouped()
    {
        var rows = new[] {
            Manual("PRIVATE_GPU", "NVIDIA", VendorSourceDiscovery.NvidiaDriversUrl),
            Manual("PRIVATE_OTHER", "NVIDIA",
                VendorSourceDiscovery.NvidiaDriversUrl + "?order=PRIVATE"),
            Manual("PRIVATE_INJECTED_VENDOR", "PRIVATE_VENDOR",
                VendorSourceDiscovery.IntelWifiUrl),
            Manual("PRIVATE_CROSS_VENDOR", "NVIDIA",
                VendorSourceDiscovery.IntelDsaUrl),
            Manual("PRIVATE_TYPO", "Intel",
                "https://www.intel.com.evil.invalid/download"),
            Manual("PRIVATE_ASSESSED", "Intel", VendorSourceDiscovery.IntelDsaUrl)
                with { IsVendorApplicable = true },
            Manual("PRIVATE_NEWS", "NVIDIA", VendorSourceDiscovery.NvidiaDriversUrl)
                with { IsReleaseAnnouncement = true, IsCatalogMetadata = true },
            Manual("PRIVATE_CATALOG", "Realtek", VendorSourceDiscovery.RealtekSupportUrl)
                with { IsCatalogMetadata = true }
        };
        var grouped = VendorManualHandoffSummary.Create(rows);
        var one = Assert.Single(grouped);
        Assert.Equal("NVIDIA", one.Vendor);
        Assert.Equal(1, one.GroupedManualDevices);
        Assert.DoesNotContain("PRIVATE", one.Title);
        Assert.DoesNotContain("PRIVATE", one.StatusText);
        Assert.DoesNotContain("PRIVATE", one.Detail);
        Assert.DoesNotContain("PRIVATE", one.SourceName);
        Assert.DoesNotContain("PRIVATE", one.OfficialUrl);
        Assert.All(rows, original =>
            Assert.Equal(0, original.GroupedManualDevices));
    }

    [Fact]
    public void CaseAndArrivalOrderDoNotInflateOrReorderSourceSummary()
    {
        var rows = new[] {
            Manual("PRIVATE_A", "NVIDIA", VendorSourceDiscovery.NvidiaDriversUrl, 1),
            Manual("PRIVATE_B", "nvidia", VendorSourceDiscovery.NvidiaDriversUrl),
            Manual("PRIVATE_C", "Realtek", VendorSourceDiscovery.RealtekSupportUrl),
            Manual("PRIVATE_D", "Intel", VendorSourceDiscovery.IntelBluetoothUrl)
        };
        var first = VendorManualHandoffSummary.Create(rows);
        var second = VendorManualHandoffSummary.Create(rows.Reverse());
        Assert.Equal(first.Select(x => x.SourceName),
            second.Select(x => x.SourceName));
        Assert.Equal(first.Select(x => x.GroupedManualDevices),
            second.Select(x => x.GroupedManualDevices));
        Assert.Equal(2, Assert.Single(first, row =>
            row.OfficialUrl == VendorSourceDiscovery.NvidiaDriversUrl)
            .GroupedManualDevices);
        Assert.Equal(1, Assert.Single(first, row =>
            row.OfficialUrl == VendorSourceDiscovery.NvidiaDriversUrl)
            .GroupedManualDevicesWithWuaIds);
    }

    [Fact]
    public void InputIsBoundedAndOriginalManualHandoffsRemainUntouched()
    {
        var baseRow = Manual("PRIVATE_GPU", "NVIDIA",
            VendorSourceDiscovery.NvidiaDriversUrl);
        var raw = Enumerable.Repeat(baseRow,
            VendorManualHandoffSummary.MaximumInputRows).ToArray();
        Assert.Single(VendorManualHandoffSummary.Create(raw));
        Assert.Throws<InvalidDataException>(() =>
            VendorManualHandoffSummary.Create(
                raw.Append(baseRow)));
        Assert.Empty(VendorManualHandoffSummary.Create([]));
        Assert.Throws<ArgumentNullException>(() =>
            VendorManualHandoffSummary.Create(null!));
        Assert.Equal("PRIVATE_GPU", baseRow.DeviceName);
        Assert.Equal("PRIVATE_VERSION_123", baseRow.InstalledVersion);
        Assert.Equal(0, baseRow.GroupedManualDevices);
    }
}
