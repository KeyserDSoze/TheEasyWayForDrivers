using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

/// <summary>
/// A validated USB/HID VID+PID may guide a user to an official vendor support
/// page, never to a synthetic OEM update or WUA installation candidate.
/// </summary>
public sealed class HidUsbPeripheralOemHandoffTests
{
    private const string LogitechHid = @"HID\VID_046D&PID_C534";
    private const string RazerHid = @"HID\VID_1532&PID_0100";
    private const string LogitechUsb = @"USB\VID_046D&PID_C534";
    private const string RazerUsb = @"USB\VID_1532&PID_0100";

    private static DriverInfo Device(string hardware,
        string name = "HID-compliant mouse", string deviceClass = "HIDClass",
        IReadOnlyList<string>? compatibles = null) =>
        new("PRIVATE_DEVICE_INSTANCE_AND_SERIAL", name, "Microsoft",
            deviceClass, "Microsoft", "10.0.0.1", null, "oem12.inf",
            true, true, 0, [hardware], compatibles ?? []);

    [Theory]
    [InlineData(LogitechHid, "Logitech",
        VendorSourceDiscovery.LogitechSupportUrl,
        PnpKnownVendorIdentity.Vendor.Logitech)]
    [InlineData(@"hid\vid_046d&pid_c534&rev_2900&mi_00&col01", "Logitech",
        VendorSourceDiscovery.LogitechSupportUrl,
        PnpKnownVendorIdentity.Vendor.Logitech)]
    [InlineData(LogitechUsb, "Logitech",
        VendorSourceDiscovery.LogitechSupportUrl,
        PnpKnownVendorIdentity.Vendor.Logitech)]
    [InlineData(@"USB\VID_046D&PID_C534&REV_2900&MI_00", "Logitech",
        VendorSourceDiscovery.LogitechSupportUrl,
        PnpKnownVendorIdentity.Vendor.Logitech)]
    [InlineData(RazerHid, "Razer",
        VendorSourceDiscovery.RazerSupportUrl,
        PnpKnownVendorIdentity.Vendor.Razer)]
    [InlineData(@"HID\VID_1532&PID_0100&IG_01&COL01", "Razer",
        VendorSourceDiscovery.RazerSupportUrl,
        PnpKnownVendorIdentity.Vendor.Razer)]
    [InlineData(RazerUsb, "Razer",
        VendorSourceDiscovery.RazerSupportUrl,
        PnpKnownVendorIdentity.Vendor.Razer)]
    [InlineData(@"USB\VID_1532&PID_0100&MI_01", "Razer",
        VendorSourceDiscovery.RazerSupportUrl,
        PnpKnownVendorIdentity.Vendor.Razer)]
    public void StrictHardwareModelRoutesGenericWindowsPeripheralToOfficialSupport(
        string id, string expected, string url,
        PnpKnownVendorIdentity.Vendor vendor)
    {
        var device = Device(id, "Generic Microsoft HID Device");
        Assert.Equal(vendor, PnpKnownVendorIdentity.FromId(id));
        Assert.Equal(vendor, PnpKnownVendorIdentity.FromDriver(device));

        var handoff = Assert.Single(VendorSourceDiscovery.Find([device], []));
        Assert.Equal(expected, handoff.Vendor);
        Assert.Equal(url, handoff.OfficialUrl);
        Assert.True(VendorSourceDiscovery.IsTrustedOfficialUrl(url));
        Assert.False(handoff.IsVendorApplicable);
        Assert.False(handoff.IsCatalogMetadata);
        Assert.Equal(0, handoff.WuaMatches);
        Assert.Contains("nessun aggiornamento OEM confermato",
            handoff.StatusText);
        Assert.DoesNotContain("PRIVATE", handoff.Detail);
        Assert.DoesNotContain("PRIVATE", handoff.StatusText);
        Assert.Equal(expected,
            VendorSourceDiscovery.GetRecognizedSupportVendor(device));
        Assert.Equal(expected, DriverSourceAdvisor.GetSource(device, []));
        var documented = OemCatalogRegistry.Find(expected);
        Assert.NotNull(documented);
        Assert.Equal(url, documented.OfficialDocumentation);
        Assert.Equal(OemCatalogAccess.OfficialSupportOnly, documented.Access);
    }

    [Fact]
    public void VendorHardwareIdentityWinsOverWrongWindowsBrandOrProvider()
    {
        var logitech = Device(LogitechHid, "Razer Mouse") with
        {
            Manufacturer = "Razer",
            DriverProvider = "NVIDIA"
        };
        var razer = Device(RazerHid, "Logitech keyboard") with
        {
            DeviceId = "PRIVATE_DIFFERENT_RAZER_INSTANCE",
            Manufacturer = "Logitech",
            DriverProvider = "Intel"
        };
        var checks = VendorSourceDiscovery.Find([logitech, razer], []);

        Assert.Equal(2, checks.Count);
        Assert.Contains(checks, check => check.Vendor == "Logitech" &&
            check.OfficialUrl == VendorSourceDiscovery.LogitechSupportUrl);
        Assert.Contains(checks, check => check.Vendor == "Razer" &&
            check.OfficialUrl == VendorSourceDiscovery.RazerSupportUrl);
        Assert.All(checks, check =>
            Assert.Contains("nessun aggiornamento OEM confermato",
                check.StatusText));
    }

    [Theory]
    [InlineData(@"HID\VID_046D")]
    [InlineData(@"HID\VID_046D&UP:0001_U:0006")]
    [InlineData(@"HID\HID_DEVICE_SYSTEM_MOUSE")]
    [InlineData(@"HID\HID_DEVICE_SYSTEM_KEYBOARD")]
    [InlineData(@"HID\VID_046D&PID_C53")]
    [InlineData(@"HID\VID_046D&PID_C5340")]
    [InlineData(@"HID\VID_046D&PID_C534&FAKE_01")]
    [InlineData(@"HID\VID_046D&PID_C534\PRIVATE_SERIAL")]
    [InlineData(@"HID\VID_046D0&PID_C534")]
    [InlineData(@"USB\VID_046D")]
    [InlineData(@"USB\VID_046D&PID_C5340")]
    [InlineData(@"USB\VID_046D&PID_C534&FAKE_01")]
    [InlineData(@"USB\VID_046D&PID_C534\PRIVATE_SERIAL")]
    [InlineData(@"USB\VID_046D0&PID_C534")]
    [InlineData(@"USB\VID_15320&PID_0100")]
    [InlineData(@"PCI\VEN_046D&DEV_C534")]
    [InlineData(@"PCI\VEN_1532&DEV_0100")]
    public void GenericUsageMalformedAndWrongBusNeverIdentifyPeripheralVendor(
        string id)
    {
        Assert.Null(PnpKnownVendorIdentity.FromId(id));
        var device = Device(id, "Logitech Razer HID gaming mouse");
        Assert.Null(VendorSourceDiscovery.GetRecognizedSupportVendor(device));
        Assert.Empty(VendorSourceDiscovery.Find([device], []));
    }

    [Fact]
    public void CompatibleIdCanGuideOnlyAnActualUsbHidPeripheral()
    {
        var mouse = Device(@"ROOT\GENERIC_MOUSE", "Generic mouse", "Mouse",
            [LogitechHid]);
        Assert.Equal(PnpKnownVendorIdentity.Vendor.Logitech,
            PnpKnownVendorIdentity.FromDriver(mouse));
        Assert.Equal("Logitech", Assert.Single(
            VendorSourceDiscovery.Find([mouse], [])).Vendor);

        var rootSystem = mouse with { DeviceClass = "System" };
        Assert.Null(VendorSourceDiscovery.GetRecognizedSupportVendor(
            rootSystem));
        Assert.Empty(VendorSourceDiscovery.Find([rootSystem], []));

        // A foreign PCI model cannot borrow peripheral identity through
        // a misleading USB/HID compatible ID, even if its VEN is unknown.
        var pci = Device(@"PCI\VEN_9999&DEV_0001",
            "Generic controller", "Mouse", [RazerHid]);
        Assert.Null(VendorSourceDiscovery.GetRecognizedSupportVendor(pci));
        Assert.Empty(VendorSourceDiscovery.Find([pci], []));

        var acpi = pci with { HardwareIds = [@"ACPI\PNP0C14"] };
        Assert.Empty(VendorSourceDiscovery.Find([acpi], []));
    }

    [Fact]
    public void ValidPrimaryVendorOutranksConflictingCompatibleVendor()
    {
        var logitech = Device(LogitechUsb, compatibles: [RazerHid]);
        Assert.Equal(PnpKnownVendorIdentity.Vendor.Logitech,
            PnpKnownVendorIdentity.FromDriver(logitech));
        Assert.Equal("Logitech",
            VendorSourceDiscovery.GetRecognizedSupportVendor(logitech));

        var razer = Device(RazerHid, compatibles: [LogitechUsb]);
        Assert.Equal("Razer",
            VendorSourceDiscovery.GetRecognizedSupportVendor(razer));
    }

    [Fact]
    public void ConflictingHardwareVendorsFailClosedAndDoNotPickArbitrarySupport()
    {
        var device = Device(LogitechHid) with
        {
            HardwareIds = [LogitechHid, RazerHid],
            CompatibleIds = [LogitechUsb]
        };
        Assert.Equal(PnpKnownVendorIdentity.Vendor.Conflicting,
            PnpKnownVendorIdentity.FromDriver(device));
        Assert.Null(VendorSourceDiscovery.GetRecognizedSupportVendor(device));
        Assert.Empty(VendorSourceDiscovery.Find([device], []));
        Assert.Equal("Windows / OEM", DriverSourceAdvisor.GetSource(device, []));
    }

    [Theory]
    [InlineData("https://support.logi.com/")]
    [InlineData("https://mysupport.razer.com/")]
    public void OnlyDocumentedHttpsHostsAreAllowlisted(string url) =>
        Assert.True(VendorSourceDiscovery.IsTrustedOfficialUrl(url));

    [Theory]
    [InlineData("http://support.logi.com/")]
    [InlineData("https://support.logi.com.evil.example/")]
    [InlineData("https://mysupport.razer.com.evil.example/")]
    [InlineData("https://login:pass@support.logi.com/")]
    [InlineData("https://mysupport.razer.com:8443/")]
    [InlineData("file:///C:/private/driver.inf")]
    public void LookalikeAndNonHttpsVendorDestinationsAreRejected(string url) =>
        Assert.False(VendorSourceDiscovery.IsTrustedOfficialUrl(url));

    [Fact]
    public void LargeIntelInventoryDoesNotHidePeripheralOfficialHandoffs()
    {
        var intel = Enumerable.Range(1, 180).Select(i =>
            Device($@"PCI\VEN_8086&DEV_{i:X4}",
                "Network Controller", "Net") with
            {
                DeviceId = $"PRIVATE_INTEL_INSTANCE_{i}"
            });
        var nvidia = Device(@"PCI\VEN_10DE&DEV_28A0",
            "Generic graphics adapter", "Display") with
        {
            DeviceId = "PRIVATE_NVIDIA_INSTANCE"
        };
        var logitech = Device(LogitechHid) with
        {
            DeviceId = "PRIVATE_LOGITECH_INSTANCE"
        };
        var razer = Device(RazerUsb, "Generic USB device", "USB") with
        {
            DeviceId = "PRIVATE_RAZER_INSTANCE"
        };
        var scan = intel.Concat([nvidia, logitech, razer]).ToArray();
        var limited = VendorSourceDiscovery.Find(scan, [], maxResults: 4);

        Assert.Equal(4, limited.Count);
        Assert.Contains(limited, row => row.Vendor == "NVIDIA");
        Assert.Contains(limited, row => row.Vendor == "Intel");
        Assert.Contains(limited, row => row.Vendor == "Logitech");
        Assert.Contains(limited, row => row.Vendor == "Razer");
        Assert.All(limited, row =>
            Assert.Equal(0, row.WuaMatches));
    }

    [Fact]
    public void VendorInventoryCountsDevicesButCannotInventWuaUpdates()
    {
        var devices = new[]
        {
            Device(LogitechHid) with { DeviceId = "PRIVATE_LOGITECH" },
            Device(RazerUsb, "USB device", "USB") with
                { DeviceId = "PRIVATE_RAZER" }
        };
        var coverage = VendorInventoryCoverage.Create(devices, []);
        Assert.True(coverage.InventoryComplete);
        Assert.True(coverage.CorrelationsComplete);
        Assert.Equal(2, coverage.Rows.Sum(row => row.Devices));
        Assert.All(coverage.Rows, row =>
        {
            Assert.Equal(0, row.DevicesWithWuaMatches);
            Assert.Equal(0, row.WuaCorrelations);
            Assert.DoesNotContain("PRIVATE", row.DisplayLine(true));
        });

        var copy = ScanCoverageText.Create(null, coverage.Rows, 2, 0, 0,
            devices, true, vendorCoverage: coverage);
        Assert.DoesNotContain("PRIVATE", copy);
        Assert.DoesNotContain(LogitechHid, copy);
        Assert.DoesNotContain(RazerUsb, copy);
    }

    [Fact]
    public void OfficialHandoffRemainsSeparateFromActualWuaGuid()
    {
        var device = Device(LogitechHid);
        var wua = new DriverUpdateInfo(
            "00000000-0000-4000-8000-000000000348",
            "Windows driver", null, "HIDClass", "Microsoft",
            null, null, false, "Microsoft", null, LogitechHid, null,
            SearchSource: "Windows Update online");
        var handoff = Assert.Single(VendorSourceDiscovery.Find(
            [device], [wua]));
        Assert.Equal("Logitech", handoff.Vendor);
        Assert.Equal(1, handoff.WuaMatches);
        Assert.False(handoff.IsVendorApplicable);
        Assert.False(handoff.IsCatalogMetadata);
        Assert.Contains("candidati Windows Update per ID",
            handoff.StatusText);
        Assert.True(VendorSourceDiscovery.IsTrustedOfficialUrl(
            handoff.OfficialUrl));
    }
}
