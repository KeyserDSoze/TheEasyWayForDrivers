using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;
using TheEasyWayForDrivers.Core.Update;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

/// <summary>
/// #541: Intel VEN_1A56 is already recognized by PnpKnownVendorIdentity
/// and OEM companion counts. Validate the same PnP evidence for Home,
/// manual handoff, installed-driver preview and exact WUA association.
/// </summary>
public sealed class IntelSecondaryPciVendorCoverageTests
{
    private const string Gpu = @"PCI\VEN_1A56&DEV_0042";
    private const string Net = @"PCI\VEN_1A56&DEV_1001";
    private const string GpuClass = @"PCI\CC_0300";
    private const string NetClass = @"PCI\CC_0280";
    private const string GpuGuid = "00000000-0000-4000-8000-000000000541";
    private const string NetGuid = "00000000-0000-4000-8000-000000000542";

    private static DriverInfo Device(string instance, string model,
        string pciClass) =>
        new(instance, "PCI Device", "Microsoft", "Unknown", "Microsoft",
            "31.0.0.1", null, "oem42.inf", true, true, 0,
            [model], [pciClass]);

    private static readonly DriverInfo Graphics =
        Device("PRIVATE_GPU_INSTANCE", Gpu, GpuClass);
    private static readonly DriverInfo Network =
        Device("PRIVATE_NET_INSTANCE", Net, NetClass);

    private static DriverUpdateInfo Wua(string guid, string hardware) =>
        new(guid, "Windows driver", null, "Display", "Microsoft",
            "31.0.0.2", null, false, "Microsoft", null,
            hardware, null, SearchSource: "Windows Update online");

    private static DriverSearchReport Report(params DriverUpdateInfo[] offers) =>
        new(offers, [new DriverSearchPassStatus("Windows Update online",
            "Completed", offers.Length, "PRIVATE_WSUS_URL")]);

    [Fact]
    public void GenericPciDevicesAreIntelInAllLocalDiscoverySurfaces()
    {
        Assert.Equal(PnpKnownVendorIdentity.Vendor.Intel,
            PnpKnownVendorIdentity.FromDriver(Graphics));
        Assert.Equal(PnpKnownVendorIdentity.Vendor.Intel,
            PnpKnownVendorIdentity.FromDriver(Network));
        Assert.True(IntelHardwareDetector.IsIntelDevice(Graphics));
        Assert.True(IntelHardwareDetector.IsIntelDevice(Network));
        Assert.True(IntelHardwareDetector.HasIntelDisplayClass(Graphics));
        Assert.True(IntelHardwareDetector.HasIntelNetworkClass(Network));
        Assert.False(IntelHardwareDetector.HasIntelNetworkClass(Graphics));
        Assert.False(IntelHardwareDetector.HasIntelDisplayClass(Network));
        Assert.Equal(2, IntelHardwareDetector.CountIntelDevices(
            [Graphics, Network]));

        var rows = VendorSourceDiscovery.Find([Graphics, Network], []);
        Assert.Equal(2, rows.Count);
        Assert.All(rows, row =>
        {
            Assert.Equal("Intel", row.Vendor);
            Assert.Equal(VendorSourceDiscovery.IntelDsaUrl, row.OfficialUrl);
            Assert.Equal(0, row.WuaMatches);
            Assert.False(row.IsInstallable);
            Assert.False(row.IsVendorApplicable);
            Assert.DoesNotContain("PRIVATE_", row.Detail);
        });
        Assert.Equal("Intel", DriverSourceAdvisor.GetSource(Graphics, []));
        Assert.Equal("Intel", DriverSourceAdvisor.GetSource(Network, []));
    }

    [Fact]
    public void HomeAndInstalledEvidenceCountBothWithoutOfferingOemInstall()
    {
        var home = HomeVendorCoverageSummary.Create(
            [Graphics, Network], Report(), inventoryComplete: true);
        var intel = Assert.Single(home, row => row.Group == "Intel / Killer");
        Assert.Contains("2 dispositivi PnP classificati", intel.DeviceStatus);
        Assert.Contains("0 candidati WUA", intel.WindowsStatus);
        Assert.True(intel.CanInspectLocalDrivers);

        var chosen = HomeVendorCoverageSummary.ChooseLocalDriverStoreDevice(
            [Graphics, Network], "Intel / Killer", inventoryComplete: true);
        Assert.NotNull(chosen);
        Assert.Equal(Network.DeviceId, chosen.DeviceId);

        var evidence = VendorDriverEvidenceFinder.Find([Graphics, Network], []);
        Assert.Equal(2, evidence.Count);
        Assert.All(evidence, row =>
        {
            Assert.Equal("Intel", row.Vendor);
            Assert.Equal(0, row.CorrelatedWuaCandidates);
            Assert.Contains("stato OEM non verificato", row.DisplayLine);
            Assert.DoesNotContain("PRIVATE_", row.DisplayLine);
        });
    }

    [Fact]
    public void ExactWuaMatchAndEightRowBenchmarkDoNotInventKillerDrivers()
    {
        var report = Report(Wua(GpuGuid, Gpu), Wua(NetGuid, Net));
        Assert.False(report.IsPartial);
        var home = HomeVendorCoverageSummary.Create(
            [Graphics, Network], report, inventoryComplete: true);
        Assert.Contains("2 candidati WUA per ID",
            Assert.Single(home, row => row.Group == "Intel / Killer").WindowsStatus);

        var rows = ReferenceDriverCoverage.Create([Graphics, Network], report);
        Assert.Equal(1, rows[0].Devices);
        Assert.Equal(1, rows[0].WindowsDriverCandidates);
        Assert.Equal(0, rows[4].Devices); // No AX1675i SUBSYS evidence.
        Assert.Equal(0, rows[4].WindowsDriverCandidates);

        var evidence = VendorDriverEvidenceFinder.Find(
            [Graphics, Network],
            WindowsDriverCandidateMerge.ConfirmedForReport(report));
        Assert.Equal(2, evidence.Count);
        Assert.All(evidence, row =>
            Assert.Equal(1, row.CorrelatedWuaCandidates));

        var audit = WuaPnpAssociationAudit.Create(
            [Graphics, Network], report, inventoryComplete: true);
        Assert.Equal(2, audit.ConfirmedGuids);
        Assert.Equal(2, audit.ExactHardware);
        var copied = ScanCoverageText.Create(
            report, [], 2, 0, 0, [Graphics, Network], true);
        Assert.Contains("Intel · grafica: 1 dispositivi classificati", copied);
        Assert.DoesNotContain("PRIVATE_", copied);
        Assert.DoesNotContain(GpuGuid, copied);
        Assert.DoesNotContain(NetGuid, copied);
        Assert.DoesNotContain(Gpu, copied);
    }

    [Fact]
    public void ForeignPrimaryCannotBorrowIntelCompatibleModel()
    {
        var foreign = Graphics with
        {
            HardwareIds = [@"PCI\VEN_9999&DEV_0042"],
            CompatibleIds = [Gpu, GpuClass],
            Manufacturer = "Intel",
            DriverProvider = "Intel"
        };
        Assert.Equal(PnpKnownVendorIdentity.Vendor.Conflicting,
            PnpKnownVendorIdentity.FromDriver(foreign));
        Assert.Null(VendorSourceDiscovery.GetRecognizedSupportVendor(foreign));
        Assert.Empty(VendorSourceDiscovery.Find([foreign], []));
        Assert.False(IntelHardwareDetector.HasIntelDisplayClass(foreign));
        Assert.Empty(VendorDriverEvidenceFinder.Find([foreign], []));
        Assert.Equal(0, ReferenceDriverCoverage.Create([foreign], Report())[0].Devices);
        Assert.Equal("Windows / OEM", DriverSourceAdvisor.GetSource(foreign, []));
    }

    [Theory]
    [InlineData(@"PCI\VEN_1A56")]
    [InlineData(@"PCI\VEN_1A56&DEV_")]
    [InlineData(@"PCI\VEN_1A560&DEV_0042")]
    [InlineData(@"PCI\VEN_1A56&DEV_0042&FAKE_01")]
    [InlineData(@"PCI\VEN_1A56&DEV_0042\PRIVATE_INSTANCE")]
    public void VendorOnlyOrMalformedIdNeverPromotesAnIntelDevice(string id)
    {
        var bad = Graphics with { HardwareIds = [id] };
        Assert.False(IntelHardwareDetector.IsIntelDevice(bad));
        Assert.Null(VendorSourceDiscovery.GetRecognizedSupportVendor(bad));
        Assert.Empty(VendorSourceDiscovery.Find([bad], []));
        Assert.Equal(0, ReferenceDriverCoverage.Create([bad], Report())[0].Devices);
    }

    [Fact]
    public void ExplicitWindowsClassCannotBeReplacedByGenericPciHint()
    {
        var camera = Graphics with { DeviceClass = "Camera" };
        Assert.True(IntelHardwareDetector.IsIntelDevice(camera));
        Assert.False(IntelHardwareDetector.HasIntelDisplayClass(camera));
        Assert.False(IntelHardwareDetector.HasIntelNetworkClass(camera));
        Assert.Equal(0, ReferenceDriverCoverage.Create([camera], Report())[0].Devices);
    }
}
