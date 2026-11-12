using System.IO.Compression;
using System.Text;
using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

/// <summary>
/// A duplicate Win32_PnPEntity row with only a generic vendor or class
/// hint must not hide a complete NVIDIA/Killer/Realtek model on that SAME
/// instance. Cross-vendor, cross-bus and disagreeing exact models fail closed.
/// </summary>
public sealed class PnpDuplicateGenericFirstRecoveryTests
{
    private const string Nvidia = @"PCI\VEN_10DE&DEV_28A0&SUBSYS_16A217AA";
    private const string Killer = @"PCI\VEN_8086&DEV_51F1&SUBSYS_16721A56";
    private const string Audio = @"HDAUDIO\FUNC_01&VEN_10DE&DEV_009B";
    private const string Swc = @"SWC\VEN_10DE&DEV_28A0";
    private const string Realtek = @"SNDW\CLASS_00&MAN_025D&PART_0711";
    private const string Logitech = @"HID\VID_046D&PID_C534";
    private const string Usb = @"USB\VID_0955&PID_7019";

    private static DriverInfo Device(string id, string model,
        string name = "Windows generic device") =>
        new(id, name, "Microsoft", "Unknown", "Microsoft", "31.0.0.1",
            null, "PRIVATE_OEM42.inf", true, true, 0, [model], []);

    private static DriverUpdateInfo Update(string model) =>
        new("00000000-0000-4000-8000-000000000386",
            "Windows driver candidate", null, "Unknown", "Microsoft",
            null, null, false, "Microsoft", null, model, null,
            SearchSource: "Windows Update online");

    private static DriverSearchReport Report(string model) =>
        new([Update(model)], [
            new DriverSearchPassStatus(
                "Windows Update online", "Completed", 1, "verified")
        ]);

    [Theory]
    [InlineData(@"PCI\VEN_10DE&CC_0300", Nvidia)]
    [InlineData(@"PCI\CC_0300", Nvidia)]
    [InlineData(@"PCI\VEN_8086&CC_0280", Killer)]
    [InlineData(@"HDAUDIO\FUNC_01&VEN_10DE", Audio)]
    [InlineData(@"SWC\VEN_10DE", Swc)]
    [InlineData(@"SNDW\CLASS_00&MAN_025D", Realtek)]
    [InlineData(@"HID\VID_046D", Logitech)]
    [InlineData(@"USB\VID_0955", Usb)]
    public void VendorBoundOrClassOnlyFirstRowCanRevealSameInstanceModel(
        string generic, string full)
    {
        Assert.False(DriverUpdateMatcher.IsCorrelatableHardwareId(generic));
        Assert.True(DriverUpdateMatcher.IsCorrelatableHardwareId(full));

        var weak = Device("PRIVATE_SAME_INSTANCE", generic);
        var complete = Device("private_same_instance", full);
        var selected = Assert.Single(
            PnpDistinctTrustedDevices.Create([weak, complete]));
        Assert.Same(complete, selected);
        Assert.Equal(2, DriverUpdateMatcher.MatchStrength(
            selected, Update(full)));
        Assert.NotNull(PnpKnownVendorIdentity.FromDriver(selected));
        Assert.Equal(1, WuaPnpAssociationAudit.Create(
            [weak, complete], Report(full), true).ExactHardware);

        var reversed = Assert.Single(
            PnpDistinctTrustedDevices.Create([complete, weak]));
        Assert.Same(complete, reversed); // a complete first row always wins
    }

    [Theory]
    [InlineData(@"PCI\VEN_8086&CC_0280", Nvidia)]
    [InlineData(@"PCI\VEN_10DE&CC_0300", Killer)]
    [InlineData(@"HDAUDIO\FUNC_01&VEN_10EC", Audio)]
    [InlineData(@"USB\VID_0CF3", Usb)]
    [InlineData(@"HID\VID_1532", Logitech)]
    [InlineData(@"SWC\VEN_8086", Swc)]
    [InlineData(@"PCI\DEV_28A0", Nvidia)]
    [InlineData(@"PCI\VEN_10DE&FAKE_0300", Nvidia)]
    [InlineData(@"HDAUDIO\FUNC_01&VEN_10DE", Nvidia)]
    [InlineData(@"PCI\VEN_10DE&CC_0300\PRIVATE", Nvidia)]
    public void ContradictoryOrMalformedHintsNeverPromoteForeignModel(
        string hint, string full)
    {
        var first = Device("PRIVATE_PNP", hint);
        var later = Device("private_pnp", full);
        var retained = Assert.Single(
            PnpDistinctTrustedDevices.Create([first, later]));
        Assert.Same(first, retained);
        Assert.Equal(0, DriverUpdateMatcher.MatchStrength(
            retained, Update(full)));
    }

    [Fact]
    public void UnknownSpecificPrimaryOnFirstRowIsAuthoritative()
    {
        var unknown = Device("PRIVATE_CARD", @"PCI\VEN_9999&DEV_0001");
        var nvidia = Device("private_card", Nvidia);
        var selected = Assert.Single(
            PnpDistinctTrustedDevices.Create([unknown, nvidia]));
        Assert.Same(unknown, selected);
        Assert.Equal(0, DriverUpdateMatcher.MatchStrength(
            selected, Update(Nvidia)));
    }

    [Fact]
    public void SpecificCompatibleIdOnFirstRowIsNeverUpgradedToHardware()
    {
        var first = Device("PRIVATE_GPU", @"PCI\CC_0300") with
        {
            CompatibleIds = [Nvidia]
        };
        var second = Device("private_gpu", Nvidia);
        var selected = Assert.Single(
            PnpDistinctTrustedDevices.Create([first, second]));
        Assert.Same(first, selected);
        Assert.Equal(1, DriverUpdateMatcher.MatchStrength(
            selected, Update(Nvidia)));
    }

    [Fact]
    public void TwoDifferentCompleteModelsCannotBeChosenFromGenericFirst()
    {
        var generic = Device("PRIVATE_GPU", @"PCI\VEN_10DE&CC_0300");
        var gpuA = Device("private_gpu", Nvidia);
        var gpuB = Device("PRIVATE_GPU", @"PCI\VEN_10DE&DEV_1F15");
        var selected = Assert.Single(
            PnpDistinctTrustedDevices.Create([generic, gpuA, gpuB]));
        Assert.Same(generic, selected);
        Assert.Equal(0, DriverUpdateMatcher.MatchStrength(
            selected, Update(Nvidia)));
    }

    [Fact]
    public void EmptyFirstIdListMayRecoverOnlyOneVerifiedKnownModel()
    {
        var empty = Device("PRIVATE_GPU", @"PCI\VEN_10DE&CC_0300")
            with { HardwareIds = [] };
        var recognized = Device("private_gpu", Nvidia);
        Assert.Same(recognized, Assert.Single(
            PnpDistinctTrustedDevices.Create([empty, recognized])));

        var unrecognized = Device("PRIVATE_OTHER", @"PCI\VEN_9999&DEV_1111");
        var generic = Device("private_other", @"PCI\CC_0300");
        Assert.Same(generic, Assert.Single(
            PnpDistinctTrustedDevices.Create([generic, unrecognized])));
    }

    [Fact]
    public void SameVendorExtraExactRowsMustShareARealCompleteModel()
    {
        var generic = Device("PRIVATE_GPU", @"PCI\VEN_10DE&CC_0300");
        var complete = Device("private_gpu", Nvidia);
        var repeat = complete with
        {
            DeviceId = "PRIVATE_GPU",
            DriverProvider = "NVIDIA"
        };
        Assert.Same(complete, Assert.Single(
            PnpDistinctTrustedDevices.Create([generic, complete, repeat])));
    }

    [Fact]
    public void RecoveryFlowsThroughHomeVendorWuaAuditAndOemInfZip()
    {
        var generic = Device("PRIVATE_RECOIL_GPU", @"PCI\VEN_10DE&CC_0300");
        var complete = Device("private_recoil_gpu", Nvidia, "Generic Display");
        var devices = new[] { generic, complete };
        var report = Report(Nvidia);

        var home = HomeVendorCoverageSummary.Create(devices, report, true);
        Assert.Contains("1 dispositivo PnP", home[0].DeviceStatus);
        Assert.Contains("1 candidato WUA per ID", home[0].WindowsStatus);
        Assert.Equal(complete,
            HomeVendorCoverageSummary.ChooseLocalDriverStoreDevice(
                devices, "NVIDIA", true));

        var vendors = VendorInventoryCoverage.Create(
            devices, report.Updates);
        Assert.Single(vendors.Rows);
        Assert.Equal(1, vendors.Rows.Sum(x => x.Devices));
        Assert.Equal(1, vendors.Rows.Sum(x => x.DevicesWithWuaMatches));
        var handoff = Assert.Single(
            VendorSourceDiscovery.Find(devices, report.Updates));
        Assert.Equal("NVIDIA", handoff.Vendor);
        Assert.Equal(1, handoff.WuaMatches);

        var audit = WuaPnpAssociationAudit.Create(devices, report, true);
        Assert.Equal(0, audit.ExcludedAmbiguousPnpInstances);
        Assert.Equal(1, audit.ExactHardware);
        Assert.False(audit.IsInstallable);

        using var archive = new MemoryStream();
        using (var zip = new ZipArchive(
            archive, ZipArchiveMode.Create, leaveOpen: true))
        {
            using var writer = new StreamWriter(
                zip.CreateEntry("drivers/display.inf").Open(),
                new UTF8Encoding(false));
            writer.Write("[Version]\nSignature=\"$Windows NT$\"\n" +
                "DriverVer=10/04/2026,32.0.15.7000\n" +
                "[Manufacturer]\n%GPU%=Models,NTamd64.10.0...22000\n" +
                "[Models.NTamd64.10.0...22000]\n" +
                "%GPU%=Install," + Nvidia + "\n");
        }
        archive.Position = 0;
        var oem = LocalOemDriverComparison.Compare(
            archive, devices, "x64", 10, 0, 26100);
        Assert.Single(oem.Findings);
        Assert.Equal([0], oem.MatchedDeviceOrdinals);
        Assert.Equal([0], oem.PnpMatchedDeviceOrdinals);
        Assert.True(oem.Findings[0].HasExactHardwareId);
        Assert.False(oem.IsInstallable);
        Assert.Equal(0, oem.ExcludedAmbiguousPnpInstances);

        var copy = ScanCoverageText.Create(
            report, vendors.Rows, 2, 0, 0, devices, true,
            vendorCoverage: vendors);
        Assert.DoesNotContain("PRIVATE", copy);
        Assert.DoesNotContain(Nvidia, copy);
        Assert.DoesNotContain("00000000-0000", copy);
    }
}
