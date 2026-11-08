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

    [Theory]
    [InlineData("High Definition Audio Device", @"HDAUDIO\FUNC_01&VEN_10DE&DEV_009B")]
    [InlineData("System controller", @"ACPI\NVDA0820")]
    [InlineData("Microsoft Basic Display Adapter", @"PCI\VEN_10DE&DEV_28A0")]
    public void GenericWindowsNamesKeepNvidiaHandoffVisible(string name, string hardwareId)
    {
        var row = Assert.Single(VendorSourceDiscovery.Find([
            Device(name, "Microsoft", hardwareId)
        ], []));
        Assert.Equal("NVIDIA", row.Vendor);
        Assert.Equal(VendorSourceDiscovery.NvidiaDriversUrl, row.OfficialUrl);
        Assert.Equal(0, row.WuaMatches);
    }

    [Fact]
    public void DetectsMajorNetworkAndAudioVendorsByHardwareId()
    {
        var devices = new[]
        {
            Device("Ethernet Controller", "Microsoft", @"PCI\VEN_10EC&DEV_8125"),
            Device("Network Controller", "Microsoft", @"PCI\VEN_14E4&DEV_43A0"),
            Device("Wireless Network Adapter", "Microsoft", @"PCI\VEN_168C&DEV_003E"),
            Device("Network Controller", "Microsoft", @"PCI\VEN_14C3&DEV_0608"),
            Device("High Definition Audio Device", "Microsoft", @"HDAUDIO\FUNC_01&VEN_10EC&DEV_0295")
        };

        var checks = VendorSourceDiscovery.Find(devices, []);
        Assert.Equal(5, checks.Count);
        Assert.Contains(checks, c => c.Vendor == "Realtek" &&
            c.OfficialUrl == VendorSourceDiscovery.RealtekSupportUrl);
        Assert.Contains(checks, c => c.Vendor == "Broadcom" &&
            c.OfficialUrl == VendorSourceDiscovery.BroadcomSupportUrl);
        Assert.Contains(checks, c => c.Vendor == "Qualcomm / Atheros" &&
            c.OfficialUrl == VendorSourceDiscovery.QualcommSupportUrl);
        Assert.Contains(checks, c => c.Vendor == "MediaTek" &&
            c.OfficialUrl == VendorSourceDiscovery.MediaTekSupportUrl);
        Assert.All(checks, c =>
        {
            Assert.Equal(0, c.WuaMatches);
            Assert.Contains("nessun aggiornamento OEM confermato", c.StatusText);
            Assert.True(VendorSourceDiscovery.IsTrustedOfficialUrl(c.OfficialUrl));
        });
    }

    [Fact]
    public void SkipsNonNetworkCameraAndNearMissVendorIds()
    {
        var camera = Device("Camera", "Microsoft", @"USB\VID_0BDA&PID_1234") with
        {
            DeviceClass = "Camera"
        };
        var nearMiss = Device("Network Controller", "Microsoft",
            @"PCI\VEN_10ECF&DEV_0001");
        Assert.Empty(VendorSourceDiscovery.Find([camera, nearMiss], []));
    }

    [Fact]
    public void SupportsMoreThanThirtyRecognizedDevices()
    {
        var devices = Enumerable.Range(0, 75).Select(i =>
            Device("Network Controller", "Microsoft",
                $@"PCI\VEN_14E4&DEV_{i:X4}")).ToArray();
        Assert.Equal(75, VendorSourceDiscovery.Find(devices, []).Count);
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
    public void VendorHandoffKeepsLateHardwareIdsFromSameWuaGuid()
    {
        const string guid = "00000000-0000-4000-8000-000000000271";
        var gpu = @"PCI\VEN_10DE&DEV_28A0";
        var audio = @"INTELAUDIO\FUNC_01&VEN_10EC&DEV_0295";
        var first = new DriverUpdateInfo(guid, "Synthetic driver", null,
            "Display", "Microsoft", null, null, false, null, null,
            @"PCI\VEN_9999&DEV_0001", null);
        var second = first with
        {
            Id = guid.ToUpperInvariant(),
            HardwareId = gpu,
            AdditionalHardwareIds = [audio]
        };
        var devices = new[]
        {
            Device("Microsoft Basic Display Adapter", "Microsoft", gpu)
                with { DeviceClass = "Display" },
            Device("Unknown codec", "Microsoft", audio)
                with { DeviceClass = "MEDIA" }
        };
        var rows = VendorSourceDiscovery.Find(devices, [first, second]);
        Assert.Equal(2, rows.Count);
        Assert.Contains(rows, row => row.Vendor == "NVIDIA" &&
            row.WuaMatches == 1);
        Assert.Contains(rows, row => row.Vendor == "Realtek" &&
            row.WuaMatches == 1);
        Assert.All(rows, row => Assert.False(row.IsVendorApplicable));
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

    [Fact]
    public void LargeIntelInventoryDoesNotHideNvidiaRealtekOrQualcomm()
    {
        // Regression #151: vendor alphabetical ordering followed by Take(100)
        // hid every NVIDIA device on a large Intel-dominated machine.
        var intel = Enumerable.Range(1, 180)
            .Select(i => Device("Network Controller", "Intel",
                $@"PCI\VEN_8086&DEV_{i:X4}"));
        var nvidiaGpu = Device("Microsoft Basic Display Adapter",
            "Microsoft", @"PCI\VEN_10DE&DEV_28A0") with
        { DeviceClass = "Display" };
        var nvidiaAudio = Device("High Definition Audio Device",
            "Microsoft", @"HDAUDIO\FUNC_01&VEN_10DE&DEV_009B") with
        { DeviceClass = "MEDIA" };
        var nvidiaPlatform = Device("System Controller",
            "Microsoft", @"ACPI\NVDA0820") with
        { DeviceClass = "System" };
        var realtek = Device("Unknown codec", "Microsoft",
            @"INTELAUDIO\FUNC_01&VEN_10EC&DEV_0295") with
        { DeviceClass = null };
        var qualcomm = Device("Generic Bluetooth Radio", "Microsoft",
            @"USB\VID_0CF3&PID_E300") with
        { DeviceClass = "Bluetooth" };
        var inventory = intel.Concat(
            [nvidiaGpu, nvidiaAudio, nvidiaPlatform, realtek, qualcomm]);

        var checks = VendorSourceDiscovery.Find(inventory, []);
        Assert.Equal(100, checks.Count);
        Assert.Contains(checks, row => row.Vendor == "NVIDIA" &&
            row.DeviceName == "Microsoft Basic Display Adapter");
        Assert.Contains(checks, row => row.Vendor == "NVIDIA" &&
            row.DeviceName == "High Definition Audio Device");
        Assert.Contains(checks, row => row.Vendor == "NVIDIA" &&
            row.DeviceName == "System Controller");
        Assert.Contains(checks, row => row.Vendor == "Realtek" &&
            row.OfficialUrl == VendorSourceDiscovery.RealtekSupportUrl);
        Assert.Contains(checks, row => row.Vendor == "Qualcomm / Atheros" &&
            row.OfficialUrl == VendorSourceDiscovery.QualcommSupportUrl);
        Assert.All(checks, row =>
        {
            Assert.False(row.IsVendorApplicable);
            Assert.Equal(0, row.WuaMatches);
            Assert.Contains("nessun aggiornamento OEM confermato", row.StatusText);
        });
        Assert.Equal(100, VendorCoverageMatrix.Create(checks)
            .Sum(group => group.Devices));
    }

    [Fact]
    public void BudgetPrioritizesActualWuaCorrelationsAcrossVendors()
    {
        var intel = Enumerable.Range(0, 120)
            .Select(i => Device("Network Controller", "Intel",
                $@"PCI\VEN_8086&DEV_{i:X4}"));
        var nvidia = Device("Generic Display Device", "Microsoft",
            @"PCI\VEN_10DE&DEV_28A0") with { DeviceClass = "Display" };
        var updateId = Guid.NewGuid().ToString("D");
        var wua = new DriverUpdateInfo(updateId, "GPU", null, "Display",
            "Microsoft", null, null, false, "Microsoft", null,
            nvidia.HardwareIds[0], null);
        var candidates = new[] { wua, wua with { Title = "Duplicate" } };

        var onlyOne = Assert.Single(VendorSourceDiscovery.Find(
            intel.Append(nvidia), candidates, maxResults: 1));
        Assert.Equal("NVIDIA", onlyOne.Vendor);
        Assert.Equal(1, onlyOne.WuaMatches); // Same WUA update ID, not two.
        Assert.False(onlyOne.IsVendorApplicable);

        var two = VendorSourceDiscovery.Find(intel.Append(nvidia),
            candidates, maxResults: 2);
        Assert.Equal(2, two.Count);
        Assert.Contains(two, row => row.Vendor == "NVIDIA");
        Assert.Contains(two, row => row.Vendor == "Intel");
        Assert.Empty(VendorSourceDiscovery.Find(intel.Append(nvidia),
            candidates, maxResults: 0));
    }

    [Theory]
    [InlineData(@"INTELAUDIO\FUNC_01&VEN_10EC&DEV_0295", null)]
    [InlineData(@"intelaudio\func_01&ven_10ec&dev_0295", "Unknown")]
    [InlineData(@"HDAUDIO\FUNC_01&VEN_10EC&DEV_0285", null)]
    public void GenericUnconfiguredRealtekAudioCodecHasOfficialHandoff(
        string hardwareId, string? deviceClass)
    {
        var device = Device("Unknown multimedia device", "Microsoft",
            hardwareId) with { DeviceClass = deviceClass };

        var row = Assert.Single(VendorSourceDiscovery.Find([device], []));
        Assert.Equal("Realtek", row.Vendor);
        Assert.Equal(0, row.WuaMatches);
        Assert.Contains("compatibilità OEM non verificata", row.Evidence);
    }

    [Theory]
    [InlineData(@"USB\VID_0CF3&PID_E300", "Bluetooth")]
    [InlineData(@"usb\vid_0cf3&pid_e360", "Net")]
    public void GenericQualcommAtherosBluetoothHasOfficialSource(
        string id, string deviceClass)
    {
        var radio = Device("Generic radio device", "Microsoft", id) with
        { DeviceClass = deviceClass };
        var row = Assert.Single(VendorSourceDiscovery.Find([radio], []));
        Assert.Equal("Qualcomm / Atheros", row.Vendor);
        Assert.Equal(0, row.WuaMatches);
        Assert.False(row.IsVendorApplicable);
    }

    [Fact]
    public void CompatibleIdsAlsoRecognizeRealtekAndQualcomm()
    {
        var realtek = Device("Unknown codec", "Microsoft",
            @"ACPI\PNP0C09") with
        {
            DeviceClass = null,
            CompatibleIds = [@"INTELAUDIO\FUNC_01&VEN_10EC&DEV_0295"]
        };
        var qualcomm = Device("Generic radio device", "Microsoft",
            @"USB\VID_9999&PID_0001") with
        {
            DeviceClass = "Bluetooth",
            CompatibleIds = [@"USB\VID_0CF3&PID_E300"]
        };
        var rows = VendorSourceDiscovery.Find([realtek, qualcomm], []);
        Assert.Equal(2, rows.Count);
        Assert.Contains(rows, x => x.Vendor == "Realtek");
        Assert.Contains(rows, x => x.Vendor == "Qualcomm / Atheros");
    }

    [Fact]
    public void SimilarButNotExactIdsAndUnrelatedUsbCameraDoNotMatch()
    {
        var realtekNearMiss = Device("Unknown codec", "Microsoft",
            @"INTELAUDIO\FUNC_01&VEN_10EC0&DEV_0295") with
        { DeviceClass = null };
        var qualcommNearMiss = Device("Generic radio device", "Microsoft",
            @"USB\VID_0CF30&PID_E300") with
        { DeviceClass = "Bluetooth" };
        var camera = Device("USB camera", "Microsoft",
            @"USB\VID_0CF3&PID_E300") with
        { DeviceClass = "Camera" };
        Assert.Empty(VendorSourceDiscovery.Find(
            [realtekNearMiss, qualcommNearMiss, camera], []));
    }
}
