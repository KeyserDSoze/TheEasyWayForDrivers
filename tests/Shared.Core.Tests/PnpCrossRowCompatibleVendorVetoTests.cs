using System.IO.Compression;
using System.Text;
using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

/// <summary>
/// #456: primary PnP identity evidence from one WMI row always outranks
/// a known-vendor CompatibleId alias on another row of the SAME DeviceId.
/// Read-only matching does not grant installation eligibility.
/// </summary>
public sealed class PnpCrossRowCompatibleVendorVetoTests
{
    private const string Nvidia = @"PCI\VEN_10DE&DEV_28A0";
    private const string UnknownPci = @"PCI\VEN_9999&DEV_0001";
    private const string Killer = @"PCI\VEN_8086&DEV_51F1";
    private const string UpdateGuid =
        "00000000-0000-4000-8000-000000000456";

    private static DriverInfo Device(string instance, string hardware) =>
        new(instance, "Generic Windows device", "Microsoft", "Unknown",
            "Microsoft", "1.0.0.0", null, "PRIVATE_oem42.inf",
            true, true, 0, [hardware], []);

    private static DriverUpdateInfo Wua(string hardware) =>
        new(UpdateGuid, "Windows Type Driver", null, "Unknown",
            "Microsoft", null, null, false, "Microsoft", null,
            hardware, null, SearchSource: "Windows Update online");

    private static DriverSearchReport Report(string hardware) =>
        new([Wua(hardware)], [new DriverSearchPassStatus(
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
    public void CrossRowKnownCompatibleCannotOverrideUnknownPrimary(
        string knownCompatible, string unknownPrimary)
    {
        Assert.NotNull(PnpKnownVendorIdentity.FromId(knownCompatible));
        Assert.Null(PnpKnownVendorIdentity.FromId(unknownPrimary));
        Assert.True(DriverUpdateMatcher.IsCorrelatableHardwareId(unknownPrimary));
        Assert.NotNull(MicrosoftUpdateCatalogSearch.TryGetPublicModelKey(unknownPrimary));

        var generic = Device("PRIVATE_SAME_DEVICE", @"ROOT\GENERIC")
            with { CompatibleIds = [knownCompatible] };
        var concrete = Device("private_same_device", unknownPrimary);
        var rows = new[] { generic, concrete };

        // Individually the legacy generic row would correlate only by
        // Compatible ID, even though another snapshot contains a real,
        // foreign primary identity for this same instance.
        Assert.Equal(1, DriverUpdateMatcher.MatchStrength(
            generic, Wua(knownCompatible)));
        foreach (var ordered in new[]
        {
            rows,
            new[] { concrete, generic }
        })
        {
            Assert.Empty(PnpDistinctTrustedDevices.Create(ordered));
            Assert.Empty(VendorSourceDiscovery.Find(
                ordered, [Wua(knownCompatible)]));
            var audit = WuaPnpAssociationAudit.Create(
                ordered, Report(knownCompatible), true);
            Assert.Equal(1, audit.ConfirmedGuids);
            Assert.Equal(0, audit.ExactHardware);
            Assert.Equal(0, audit.CompatibleOnly);
            Assert.Equal(1, audit.Unattributed);
            Assert.Equal(1, audit.ExcludedAmbiguousPnpInstances);
            Assert.False(audit.IsInstallable);
            Assert.DoesNotContain(UpdateGuid, audit.Text);
            Assert.DoesNotContain(unknownPrimary, audit.Text);
            Assert.DoesNotContain(knownCompatible, audit.Text);
            Assert.DoesNotContain("PRIVATE", audit.Text);
        }
    }

    [Fact]
    public void WeakKnownAliasCannotInflateHomeBenchmarkOrLocalInf()
    {
        var generic = Device("PRIVATE_GPU", @"PCI\CC_0300")
            with { CompatibleIds = [Nvidia] };
        var unknown = Device("private_gpu", UnknownPci);
        var rows = new[] { generic, unknown };
        var report = Report(Nvidia);

        Assert.All(HomeVendorCoverageSummary.Create(
            rows, report, inventoryComplete: true), item =>
        {
            Assert.Contains("0 dispositivi PnP", item.DeviceStatus);
            Assert.Contains("0 candidati WUA per ID", item.WindowsStatus);
            Assert.False(item.HasLocalDevice);
            Assert.False(item.IsInstallable);
        });
        Assert.Empty(VendorInventoryCoverage.Create(
            rows, report.Updates).Rows);
        Assert.All(ReferenceDriverCoverage.Create(rows, report), item =>
        {
            Assert.Equal(0, item.Devices);
            Assert.Equal(0, item.WindowsDriverCandidates);
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
    public void CoherentUnknownAndKnownPrimaryModelsRemainTrustedIndependently()
    {
        var generic = Device("PRIVATE_SAME", @"ROOT\GENERIC");
        var unknown = Device("private_same", UnknownPci);
        var known = Device("PRIVATE_KILLER", Killer)
            with { CompatibleIds = [UnknownPci] };

        // Unknown hardware is not automatically disqualified by existence
        // of a separate, healthy, known hardware PnP instance.
        var kept = PnpDistinctTrustedDevices.Create([unknown, known]);
        Assert.Equal(2, kept.Count);
        Assert.Same(unknown, kept[0]);
        Assert.Same(known, kept[1]);

        var genericWithUnknownAlias = generic with
        {
            CompatibleIds = [UnknownPci]
        };
        Assert.Equal(1, PnpDistinctTrustedDevices.Create(
            [genericWithUnknownAlias, unknown]).Count);
        var audit = WuaPnpAssociationAudit.Create(
            [unknown, known], Report(Killer), true);
        Assert.Equal(1, audit.ExactHardware);
        Assert.Equal(0, audit.ExcludedAmbiguousPnpInstances);
    }

    [Fact]
    public void MalformedUnknownHardwareCannotVetoValidKnownCompatible()
    {
        var generic = Device("PRIVATE_CARD", @"ROOT\GENERIC")
            with { CompatibleIds = [Nvidia] };
        var malformed = Device("private_card",
            @"PCI\VEN_9999&DEV_0001&NOTREAL_1234");

        // A malformed string cannot provide vendor/model evidence.
        Assert.Same(generic, Assert.Single(
            PnpDistinctTrustedDevices.Create([generic, malformed])));
        Assert.Equal(0, WuaPnpAssociationAudit.Create(
            [generic, malformed], Report(Nvidia), true)
            .ExcludedAmbiguousPnpInstances);
    }
}
