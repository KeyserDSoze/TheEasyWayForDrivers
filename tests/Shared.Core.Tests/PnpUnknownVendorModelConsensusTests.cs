using System.IO.Compression;
using System.Text;
using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

/// <summary>
/// #455: strict primary models from unsupported manufacturers are still
/// independently valid PnP identity evidence; don't silently discard one
/// when it contradicts a supported NVIDIA/Killer/Realtek model.
/// Read-only diagnostics cannot authorize any driver installation.
/// </summary>
public sealed class PnpUnknownVendorModelConsensusTests
{
    private const string Nvidia = @"PCI\VEN_10DE&DEV_28A0";
    private const string UnknownPci = @"PCI\VEN_9999&DEV_0001";
    private const string Killer = @"PCI\VEN_8086&DEV_51F1";
    private const string GuidId =
        "00000000-0000-4000-8000-000000000455";

    private static DriverInfo Device(
        string instance, params string[] hardware) =>
        new(instance, "Windows generic device", "Microsoft", "Unknown",
            "Microsoft", "1.0.0.0", null, "PRIVATE_oem42.inf",
            true, true, 0, hardware, []);

    private static DriverUpdateInfo Update(string hardware) =>
        new(GuidId, "Windows Type Driver", null, "Unknown",
            "Microsoft", null, null, false, "Microsoft", null,
            hardware, null, SearchSource: "Windows Update online");

    private static DriverSearchReport Report(string hardware) =>
        new([Update(hardware)], [new DriverSearchPassStatus(
            "Windows Update online", "Completed", 1, "verified")]);

    [Theory]
    [InlineData(Nvidia, UnknownPci)]
    [InlineData(Killer, @"PCI\VEN_1234&DEV_0001")]
    [InlineData(@"PCI\VEN_10EC&DEV_8125", UnknownPci)]
    [InlineData(@"USB\VID_0955&PID_7019", @"USB\VID_9999&PID_0001")]
    [InlineData(@"HDAUDIO\FUNC_01&VEN_10DE&DEV_009B",
        @"HDAUDIO\FUNC_01&VEN_9999&DEV_0001")]
    [InlineData(@"SWC\VEN_8086&DEV_51F1", @"SWC\VEN_9999&DEV_0001")]
    [InlineData(@"HID\VID_046D&PID_C534", @"HID\VID_9999&PID_0001")]
    [InlineData(@"SNDW\CLASS_00&MAN_025D&PART_0711",
        @"SNDW\CLASS_00&MAN_0242&PART_0711")]
    public void UnknownButValidPrimaryModelVetoesSupportedVendor(
        string recognized, string foreign)
    {
        // The OEM vendor mapping does not know these manufacturers,
        // but both IDs pass strict public-model and PnP validation.
        Assert.NotNull(PnpKnownVendorIdentity.FromId(recognized));
        Assert.Null(PnpKnownVendorIdentity.FromId(foreign));
        Assert.True(DriverUpdateMatcher.IsCorrelatableHardwareId(foreign));
        Assert.NotNull(MicrosoftUpdateCatalogSearch.TryGetPublicModelKey(foreign));

        var first = Device("PRIVATE_SAME_DEVICE", recognized);
        var second = Device("private_same_device", foreign);
        var mixed = Device("PRIVATE_SAME_DEVICE", recognized, foreign)
            with { CompatibleIds = [recognized] };

        foreach (var rows in new[]
        {
            new[] { mixed },
            new[] { first, second },
            new[] { second, first }
        })
        {
            Assert.Empty(PnpDistinctTrustedDevices.Create(rows));
            Assert.Empty(VendorSourceDiscovery.Find(rows, [Update(recognized)]));
            var audit = WuaPnpAssociationAudit.Create(
                rows, Report(recognized), true);
            Assert.Equal(1, audit.ConfirmedGuids);
            Assert.Equal(0, audit.ExactHardware);
            Assert.Equal(0, audit.CompatibleOnly);
            Assert.Equal(1, audit.Unattributed);
            Assert.Equal(1, audit.ExcludedAmbiguousPnpInstances);
            Assert.False(audit.IsInstallable);
            Assert.DoesNotContain(GuidId, audit.Text);
            Assert.DoesNotContain(recognized, audit.Text);
            Assert.DoesNotContain(foreign, audit.Text);
            Assert.DoesNotContain("PRIVATE", audit.Text);
        }
    }

    [Fact]
    public void TwoDifferentUnknownModelsAlsoCannotPretendToBeOneDevice()
    {
        var first = Device("PRIVATE_UNKNOWN",
            @"PCI\VEN_1234&DEV_0001");
        var second = Device("private_unknown",
            @"PCI\VEN_1234&DEV_0002");
        Assert.Empty(PnpDistinctTrustedDevices.Create([first, second]));
        var audit = WuaPnpAssociationAudit.Create(
            [first, second], Report(@"PCI\VEN_1234&DEV_0001"), true);
        Assert.Equal(1, audit.ExcludedAmbiguousPnpInstances);
        Assert.Equal(0, audit.ExactHardware);
    }

    [Fact]
    public void FalseNvidiaPrimaryIsNotAHomeOrInfOffer()
    {
        var ambiguous = Device("PRIVATE_GPU", Nvidia, UnknownPci);
        var rows = new[] { ambiguous };
        var report = Report(Nvidia);
        Assert.All(HomeVendorCoverageSummary.Create(rows, report, true), row =>
        {
            Assert.Contains("0 dispositivi PnP", row.DeviceStatus);
            Assert.Contains("0 candidati WUA per ID", row.WindowsStatus);
            Assert.False(row.HasLocalDevice);
            Assert.False(row.IsInstallable);
        });
        Assert.Empty(VendorInventoryCoverage.Create(rows, report.Updates).Rows);
        Assert.All(ReferenceDriverCoverage.Create(rows, report), row =>
        {
            Assert.Equal(0, row.Devices);
            Assert.Equal(0, row.WindowsDriverCandidates);
        });

        using var archive = new MemoryStream();
        using (var zip = new ZipArchive(
            archive, ZipArchiveMode.Create, leaveOpen: true))
        {
            using var writer = new StreamWriter(
                zip.CreateEntry("display/driver.inf").Open(),
                new UTF8Encoding(false));
            writer.Write("[Version]\nSignature=\"$Windows NT$\"\n" +
                "DriverVer=10/04/2026,32.0.15.7000\n" +
                "[Manufacturer]\n%GPU%=Models,NTamd64.10.0...22000\n" +
                "[Models.NTamd64.10.0...22000]\n" +
                "%GPU%=Install," + Nvidia + "\n");
        }
        archive.Position = 0;
        var comparison = LocalOemDriverComparison.Compare(
            archive, rows, "x64", 10, 0, 26100);
        Assert.Empty(comparison.Findings);
        Assert.Empty(comparison.MatchedDeviceOrdinals ?? []);
        Assert.Equal(1, comparison.ExcludedAmbiguousPnpInstances);
        Assert.False(comparison.IsInstallable);
    }

    [Fact]
    public void MalformedAndCompatibleOnlyForeignAliasesCannotVetoActualHardware()
    {
        var valid = Device("PRIVATE_GOOD", Nvidia,
            @"PCI\VEN_9999&DEV_0001&PRIVATE_BROKEN")
            with { CompatibleIds = [UnknownPci] };
        Assert.Same(valid, Assert.Single(
            PnpDistinctTrustedDevices.Create([valid])));
        Assert.Equal(0, WuaPnpAssociationAudit.Create(
            [valid], Report(Nvidia), true).ExcludedAmbiguousPnpInstances);
    }

    [Fact]
    public void OneCoherentUnknownOemModelAndHealthyKillerArePreserved()
    {
        var unknown = Device("PRIVATE_UNKNOWN",
            @"PCI\VEN_1234&DEV_0001",
            @"PCI\VEN_1234&DEV_0001&SUBSYS_16A217AA",
            @"PCI\VEN_1234&DEV_0001&SUBSYS_16A217AA&REV_01");
        var killer = Device("PRIVATE_KILLER", Killer);
        var kept = PnpDistinctTrustedDevices.Create([unknown, killer]);
        Assert.Equal(2, kept.Count);
        Assert.Same(unknown, kept[0]);
        Assert.Same(killer, kept[1]);
        var audit = WuaPnpAssociationAudit.Create(
            [unknown, killer], Report(Killer), true);
        Assert.Equal(1, audit.ExactHardware);
        Assert.Equal(0, audit.ExcludedAmbiguousPnpInstances);
    }
}
