using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

/// <summary>
/// The OEM support handoff, Home installed-version evidence and device
/// source suggestion must agree about non-Intel/NVIDIA audio/network chips.
/// These are verified PnP classifications, NOT verified driver updates.
/// </summary>
public sealed class MajorVendorInstalledEvidenceTests
{
    private static DriverInfo Device(string id, string name, string deviceClass,
        string manufacturer = "Microsoft", string? deviceId = null) =>
        new(deviceId ?? "PRIVATE_INSTANCE", name, manufacturer, deviceClass,
            manufacturer, "31.0.1.200", null, "oem42.inf", true, true, 0,
            [id], []);

    [Theory]
    [InlineData(@"PCI\VEN_10EC&DEV_8125", "Ethernet Controller", "Net", "Realtek")]
    [InlineData(@"INTELAUDIO\FUNC_01&VEN_10EC&DEV_0295", "Unknown codec", "MEDIA", "Realtek")]
    [InlineData(@"HDAUDIO\FUNC_01&VEN_10EC&DEV_0295", "High Definition Audio Device", "MEDIA", "Realtek")]
    [InlineData(@"PCI\VEN_14E4&DEV_43A0", "Network Controller", "Net", "Broadcom")]
    [InlineData(@"USB\VID_0A5C&PID_21E8", "Generic radio device", "Bluetooth", "Broadcom")]
    [InlineData(@"PCI\VEN_168C&DEV_003E", "Network Controller", "Net", "Qualcomm / Atheros")]
    [InlineData(@"USB\VID_0CF3&PID_E300", "Generic radio device", "Bluetooth", "Qualcomm / Atheros")]
    [InlineData(@"PCI\VEN_14C3&DEV_0608", "Network Controller", "Net", "MediaTek")]
    [InlineData(@"USB\VID_0E8D&PID_7961", "Generic radio device", "Bluetooth", "MediaTek")]
    public void HomeAndDeviceSourceRecognizeExistingOfficialHandoffVendor(
        string id, string name, string deviceClass, string expectedVendor)
    {
        var device = Device(id, name, deviceClass);

        var handoff = Assert.Single(VendorSourceDiscovery.Find([device], []));
        var evidence = Assert.Single(VendorDriverEvidenceFinder.Find([device], []));

        Assert.Equal(expectedVendor,
            VendorSourceDiscovery.GetRecognizedSupportVendor(device));
        Assert.Equal(handoff.Vendor, evidence.Vendor);
        Assert.Equal(expectedVendor, evidence.Vendor);
        Assert.Equal(expectedVendor, DriverSourceAdvisor.GetSource(device, []));
        Assert.Equal(0, evidence.CorrelatedWuaCandidates);
        Assert.Contains("stato OEM non verificato", evidence.DisplayLine);
        Assert.DoesNotContain("PRIVATE_INSTANCE", evidence.DisplayLine);
        Assert.DoesNotContain(@"PCI\", evidence.DisplayLine);
    }

    [Fact]
    public void RealtekCodecBehindIntelSstUsesChipVendorAndLaptopOem()
    {
        var codec = Device(@"INTELAUDIO\FUNC_01&VEN_10EC&DEV_0295",
            "Unknown codec", "MEDIA", "Intel Smart Sound Technology");
        Assert.Equal("Realtek",
            VendorSourceDiscovery.GetRecognizedSupportVendor(codec));

        var evidence = Assert.Single(VendorDriverEvidenceFinder.Find([codec], []));
        Assert.Equal("Realtek", evidence.Vendor);
        Assert.Equal("Realtek · PCSpecialist OEM",
            DriverSourceAdvisor.GetSource(codec, [], "PCSpecialist"));
        Assert.Equal("Realtek · Dell OEM", DriverSourceAdvisor.GetSource(
            codec with { HardwareIds = [@"HDAUDIO\FUNC_01&VEN_10EC&DEV_0295"] },
            [], "Dell"));
    }

    [Fact]
    public void CompatibleIdsAlsoSupportRealtekAndQualcommWithoutGuessingNames()
    {
        var realtek = Device(@"ROOT\MEDIA", "Unknown codec", "Unknown") with
        {
            CompatibleIds = [@"INTELAUDIO\FUNC_01&VEN_10EC&DEV_0295"]
        };
        // A class-only Windows primary identity may borrow a specific USB
        // Compatible ID. An explicit foreign VID+PID must NOT do so.
        var radio = Device(@"USB\Class_E0&SubClass_01&Prot_01", "Generic radio device",
            "Bluetooth", deviceId: "PRIVATE_RADIO") with
        {
            CompatibleIds = [@"USB\VID_0CF3&PID_E300"]
        };
        var rows = VendorDriverEvidenceFinder.Find([realtek, radio], []);
        Assert.Equal(2, rows.Count);
        Assert.Contains(rows, row => row.Vendor == "Realtek");
        Assert.Contains(rows, row => row.Vendor == "Qualcomm / Atheros");
        Assert.All(rows, row => Assert.Equal(0, row.CorrelatedWuaCandidates));
    }

    [Theory]
    [InlineData(@"PCI\VEN_14E40&DEV_43A0", "Network Controller", "Net")]
    [InlineData(@"USB\VID_0CF30&PID_E300", "Generic radio device", "Bluetooth")]
    [InlineData(@"PCI\VEN_14C30&DEV_0608", "Network Controller", "Net")]
    [InlineData(@"INTELAUDIO\FUNC_01&VEN_10EC0&DEV_0295", "Unknown codec", "MEDIA")]
    [InlineData(@"USB\VID_0BDA&PID_1234", "USB Camera", "Camera")]
    [InlineData(@"PCI\VEN_14E4&DEV_1234", "PCI Root Port", "System")]
    public void NearMissCameraAndInfrastructureAreNotClassifiedAsVendors(
        string id, string name, string deviceClass)
    {
        var device = Device(id, name, deviceClass);
        Assert.Null(VendorSourceDiscovery.GetRecognizedSupportVendor(device));
        Assert.Empty(VendorDriverEvidenceFinder.Find([device], []));
        Assert.Equal("Windows / OEM",
            DriverSourceAdvisor.GetSource(device, []));
    }

    [Fact]
    public void WuaCorrelationCountsDistinctUpdateIdsAndNeverClaimsVendorOffer()
    {
        const string id = @"PCI\VEN_14E4&DEV_43A0";
        var device = Device(id, "Network Controller", "Net");
        var guid = Guid.NewGuid().ToString("D");
        var matched = new DriverUpdateInfo(guid, "Driver candidate", null, "Net",
            "Broadcom", null, null, false, "Broadcom", null, id, null);
        var unrelated = matched with
        {
            Id = Guid.NewGuid().ToString("D"),
            HardwareId = @"PCI\VEN_14E40&DEV_43A0"
        };
        var rows = VendorDriverEvidenceFinder.Find(
            [device], [matched, matched with { Title = "Duplicate WUA pass" }, unrelated]);

        var evidence = Assert.Single(rows);
        Assert.Equal("Broadcom", evidence.Vendor);
        Assert.Equal(1, evidence.CorrelatedWuaCandidates);
        Assert.Contains("compatibilità da verificare", evidence.DisplayLine);
        Assert.Equal("Windows Update", DriverSourceAdvisor.GetSource(device, [matched]));
    }

    [Fact]
    public void LargeIntelInventoryCannotHideNewVendorRowsWithinBudget()
    {
        var intel = Enumerable.Range(0, 160).Select(index =>
            Device($@"PCI\VEN_8086&DEV_{index:X4}",
                "Network Controller", "Net", deviceId: "INTEL_" + index));
        var peripherals = new[]
        {
            Device(@"PCI\VEN_10EC&DEV_8125", "Ethernet Controller", "Net", deviceId: "REALTEK"),
            Device(@"PCI\VEN_14E4&DEV_43A0", "Network Controller", "Net", deviceId: "BROADCOM"),
            Device(@"PCI\VEN_168C&DEV_003E", "Network Controller", "Net", deviceId: "QUALCOMM"),
            Device(@"PCI\VEN_14C3&DEV_0608", "Network Controller", "Net", deviceId: "MEDIATEK")
        };

        var rows = VendorDriverEvidenceFinder.Find(
            intel.Concat(peripherals), [], maximum: 15);
        Assert.Equal(15, rows.Count);
        Assert.Contains(rows, item => item.Vendor == "Realtek");
        Assert.Contains(rows, item => item.Vendor == "Broadcom");
        Assert.Contains(rows, item => item.Vendor == "Qualcomm / Atheros");
        Assert.Contains(rows, item => item.Vendor == "MediaTek");
        Assert.All(rows, item => Assert.Equal(0, item.CorrelatedWuaCandidates));
    }

    [Fact]
    public void FailedWuaScanRemainsUnverifiedForNewVendorRows()
    {
        var device = Device(@"PCI\VEN_14C3&DEV_0608",
            "Network Controller", "Net");
        var result = Assert.Single(VendorDriverEvidenceFinder.Find(
            [device], [], wuaWasQueried: false));
        Assert.Contains("ricerca Windows Update non verificata",
            result.DisplayLine);
        Assert.DoesNotContain("nessun candidato", result.DisplayLine);
    }

    [Theory]
    [InlineData(@"PCI\VEN_10EC&DEV_8125", @"PCI\CC_020000", "Realtek")]
    [InlineData(@"PCI\VEN_14E4&DEV_43A0", @"PCI\CC_028000", "Broadcom")]
    [InlineData(@"PCI\VEN_168C&DEV_003E", @"PCI\CC_0280", "Qualcomm / Atheros")]
    [InlineData(@"PCI\VEN_17CB&DEV_1234", @"PCI\CC_028000", "Qualcomm / Atheros")]
    [InlineData(@"PCI\VEN_14C3&DEV_0608", @"PCI\CC_0280", "MediaTek")]
    public void GenericPciDeviceGetsReadOnlyHandoffFromRealVendorAndNetworkClass(
        string id, string pciClass, string vendor)
    {
        var generic = Device(id, "PCI Device", "Unknown") with
        {
            CompatibleIds = [pciClass]
        };
        var handoff = Assert.Single(VendorSourceDiscovery.Find([generic], []));
        var installed = Assert.Single(VendorDriverEvidenceFinder.Find([generic], []));
        Assert.Equal(vendor, handoff.Vendor);
        Assert.Equal(vendor, installed.Vendor);
        Assert.Equal(0, handoff.WuaMatches);
        Assert.False(handoff.IsVendorApplicable);
        Assert.False(handoff.IsCatalogMetadata);
        Assert.True(VendorSourceDiscovery.IsTrustedOfficialUrl(handoff.OfficialUrl));
        Assert.Contains("nessun aggiornamento OEM confermato", handoff.StatusText);
        Assert.DoesNotContain("PRIVATE_INSTANCE", installed.DisplayLine);
    }



    [Theory]
    [InlineData(@"PCI\VEN_10EC&DEV_8125", "Net", "Realtek")]
    [InlineData(@"PCI\VEN_168C&DEV_003E", "Net", "Qualcomm / Atheros")]
    [InlineData(@"PCI\VEN_14E4&DEV_43A0", "Net", "Broadcom")]
    [InlineData(@"PCI\VEN_10DE&DEV_28A0", "Display", "NVIDIA")]
    public void ConflictingKillerMarketingNameCannotOverridePciVendor(
        string hardwareId, string deviceClass, string expected)
    {
        var device = Device(hardwareId, "Killer Wireless Adapter",
            deviceClass, "Killer Driver Provider");
        var source = Assert.Single(VendorSourceDiscovery.Find([device], []));
        var installed = Assert.Single(VendorDriverEvidenceFinder.Find(
            [device], []));
        Assert.Equal(expected, source.Vendor);
        Assert.Equal(expected, installed.Vendor);
        Assert.Equal(expected, DriverSourceAdvisor.GetSource(device, []));
        Assert.Equal(0, installed.CorrelatedWuaCandidates);
        Assert.Contains("stato OEM non verificato", installed.DisplayLine);
        Assert.False(source.IsVendorApplicable);
        Assert.DoesNotContain("PRIVATE_INSTANCE", installed.DisplayLine);
    }

    [Fact]
    public void GenuineIntelKillerNameAndHardwareStillRouteToIntelWifi()
    {
        var device = Device(
            @"PCI\VEN_8086&DEV_51F1&SUBSYS_16721A56",
            "Killer Wi-Fi 6E AX1675i", "Net", "Intel");
        var source = Assert.Single(VendorSourceDiscovery.Find([device], []));
        Assert.Equal("Killer / Intel", source.Vendor);
        Assert.Equal(VendorSourceDiscovery.IntelWifiUrl, source.OfficialUrl);
        Assert.Equal("Killer / Intel",
            Assert.Single(VendorDriverEvidenceFinder.Find([device], [])).Vendor);
    }

    [Theory]
    [InlineData(@"PCI\VEN_10DEC&DEV_8125")]
    [InlineData(@"PCI\VEN_10DE&DEV_28A00")]
    [InlineData(@"PCI\VEN_10DE&DEV_28AZ")]
    [InlineData(@"PCI\VEN_10DE&DEV_28A0\PRIVATE_INSTANCE")]
    public void InvalidPciIdentityCannotTakePrecedenceOverKillerName(
        string badId)
    {
        var device = Device(badId, "Killer Wi-Fi Device", "Net", "Microsoft");
        // Malformed PnP vendor evidence cannot classify the vendor or
        // pretend that the referenced update is verified.
        var source = VendorSourceDiscovery.GetRecognizedSupportVendor(device);
        Assert.NotEqual("NVIDIA", source);
        Assert.NotEqual("Realtek", source);
        Assert.NotEqual("Qualcomm / Atheros", source);
        Assert.DoesNotContain("PRIVATE_INSTANCE",
            string.Join(" ", VendorDriverEvidenceFinder.Find([device], [])
                .Select(x => x.DisplayLine)));
    }


}
