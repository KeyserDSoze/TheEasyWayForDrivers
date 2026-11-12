using System.IO.Compression;
using System.Text;
using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

public sealed class PnpSetupApiOemSubsystemEnrichmentTests
{
    private const string Gpu = @"PCI\VEN_10DE&DEV_28A0";
    private const string GpuOem = @"PCI\VEN_10DE&DEV_28A0&SUBSYS_16A217AA";
    private const string GpuOtherOem = @"PCI\VEN_10DE&DEV_28A0&SUBSYS_16A21043";
    private const string Killer = @"PCI\VEN_8086&DEV_51F1";
    private const string KillerOem = @"PCI\VEN_8086&DEV_51F1&SUBSYS_16721A56";
    private const string Realtek = @"PCI\VEN_10EC&DEV_8125";
    private const string RealtekOem = @"PCI\VEN_10EC&DEV_8125&SUBSYS_012310EC";
    private const string GuidValue = "00000000-0000-4000-8000-000000000388";

    private static DriverInfo Device(PnpSetupApiIdentity.Enrichment id, string deviceClass) =>
        new("PRIVATE_INSTANCE_SERIAL", "Generic Microsoft Device",
            "Microsoft", deviceClass, "Microsoft", "1.0", null,
            "PRIVATE_oem88.inf", true, true, 0, id.HardwareIds,
            id.CompatibleIds, PnpIdsRecoveredFromSetupApi: id.UsedSetupApi);

    private static DriverUpdateInfo Offer(string id) =>
        new(GuidValue, "Windows PnP driver", null, "Display", "Microsoft",
            null, null, false, "Microsoft", null, id, null,
            SearchSource: "Windows Update online");

    private static DriverSearchReport Report(string id) =>
        new([Offer(id)], [new DriverSearchPassStatus(
            "Windows Update online", "Completed", 1, "verified")]);

    private static MemoryStream Zip(string id)
    {
        var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            using var writer = new StreamWriter(
                zip.CreateEntry("oem/driver.inf").Open(), new UTF8Encoding(false));
            writer.Write("[Version]\nSignature=\"$Windows NT$\"\n" +
                "DriverVer=11/12/2026,32.0.15.7000\n" +
                "[Manufacturer]\n%Maker%=Models,NTamd64.10.0...22000\n" +
                "[Models.NTamd64.10.0...22000]\n%Model%=Install," + id + "\n");
        }
        ms.Position = 0;
        return ms;
    }

    [Theory]
    [InlineData(Gpu, GpuOem, "Display")]
    [InlineData(Killer, KillerOem, "Net")]
    [InlineData(Realtek, RealtekOem, "Net")]
    public void SameInstanceNativeQualifiedIdPreservesWmiAndEnablesExactOemMatch(
        string bare, string qualified, string deviceClass)
    {
        Assert.True(PnpSetupApiIdentity.NeedsPciOemSubsystemRecovery([bare]));
        var identity = PnpSetupApiIdentity.Merge(
            [bare], [], [qualified, bare], [], null, null);
        Assert.Equal([bare, qualified], identity.HardwareIds);
        Assert.True(identity.UsedSetupApi);
        Assert.False(identity.UsedSignedDriver);
        var driver = Device(identity, deviceClass);
        Assert.Equal(2, DriverUpdateMatcher.MatchStrength(driver, Offer(bare)));
        Assert.Equal(2, DriverUpdateMatcher.MatchStrength(driver, Offer(qualified)));
        Assert.Equal(1, WuaPnpAssociationAudit.Create(
            [driver], Report(qualified), true).ExactHardware);
        Assert.DoesNotContain("PRIVATE",
            WuaPnpAssociationAudit.Create([driver], Report(qualified), true).Text);

        using var zip = Zip(qualified);
        var oem = LocalOemDriverComparison.Compare(
            zip, [driver], "x64", 10, 0, 26100);
        Assert.Single(oem.Findings);
        Assert.Equal([0], oem.MatchedDeviceOrdinals);
        Assert.True(oem.Findings[0].HasExactHardwareId);
        Assert.False(oem.IsInstallable);
        Assert.False(oem.IsVerifiedCompatible);
    }

    [Fact]
    public void RecoveredNvidiaOemCanBeSeenByHomeEightRowsAndVendorAudit()
    {
        var id = PnpSetupApiIdentity.Merge(
            [Gpu], [], [GpuOem, Gpu], [], null, null);
        var gpu = Device(id, "Display");
        var search = Report(GpuOem);

        var home = HomeVendorCoverageSummary.Create([gpu], search, true);
        Assert.Contains("1 dispositivo PnP", home[0].DeviceStatus);
        Assert.Contains("1 candidato WUA per ID", home[0].WindowsStatus);
        Assert.Equal(gpu, HomeVendorCoverageSummary.ChooseLocalDriverStoreDevice(
            [gpu], "NVIDIA", true));

        var eight = ReferenceDriverCoverage.Create([gpu], search);
        Assert.Equal(8, eight.Count);
        Assert.Equal(1, eight[5].Devices);
        Assert.Equal(1, eight[5].WindowsDriverCandidates);
        var coverage = VendorInventoryCoverage.Create([gpu], search.Updates);
        Assert.Single(coverage.Rows);
        Assert.Equal(1, coverage.Rows[0].DevicesWithWuaMatches);
        var handoff = Assert.Single(VendorSourceDiscovery.Find(
            [gpu], search.Updates));
        Assert.Equal("NVIDIA", handoff.Vendor);
        Assert.Equal(1, handoff.WuaMatches);

        var anonymous = ScanCoverageText.Create(
            search, coverage.Rows, 1, 0, 0,
            [gpu], inventoryComplete: true, vendorCoverage: coverage);
        Assert.Contains("SetupAPI Windows locale: 1 dispositivi", anonymous);
        Assert.DoesNotContain("PRIVATE", anonymous);
        Assert.DoesNotContain(GpuOem, anonymous);
        Assert.DoesNotContain(GuidValue, anonymous);
    }

    [Fact]
    public void MultipleWmiVariantsAndNativeRevisionMustAgree()
    {
        var wmi = new[] { Gpu, Gpu + "&REV_A1", Gpu + "&CC_0300" };
        Assert.True(PnpSetupApiIdentity.NeedsPciOemSubsystemRecovery(wmi));
        var qualifiedRev = GpuOem + "&REV_A1";
        var id = PnpSetupApiIdentity.Merge(
            wmi, [], [qualifiedRev, GpuOem, Gpu], [], null, null);
        Assert.Equal(wmi.Concat([qualifiedRev, GpuOem]), id.HardwareIds);
        Assert.True(id.UsedSetupApi);
    }

    [Theory]
    [InlineData(@"PCI\VEN_10DE&DEV_1F15")]
    [InlineData(KillerOem)]
    [InlineData(@"USB\VID_0955&PID_7019")]
    [InlineData(@"PCI\VEN_10DE&DEV_28A0&SUBSYS_16A217AA\PRIVATE")]
    [InlineData(@"PCI\VEN_10DE&DEV_28A0&SUBSYS_INVALID")]
    [InlineData(@"PCI\VEN_10DE&DEV_28A0&FAKE_01")]
    public void OneForeignMalformedOrDifferentDeviceNativeEntryVetoesEntireRecovery(
        string foreign)
    {
        var id = PnpSetupApiIdentity.Merge(
            [Gpu], [], [GpuOem, foreign], [], null, null);
        Assert.Equal([Gpu], id.HardwareIds);
        Assert.False(id.UsedSetupApi);
        Assert.Equal(0, DriverUpdateMatcher.MatchStrength(
            Device(id, "Display"), Offer(GpuOem)));
    }

    [Fact]
    public void DifferentOemSubsystemsOrRevisionsNeverPickOne()
    {
        var differentSubsys = PnpSetupApiIdentity.Merge(
            [Gpu], [], [GpuOem, GpuOtherOem], [], null, null);
        Assert.Equal([Gpu], differentSubsys.HardwareIds);
        Assert.False(differentSubsys.UsedSetupApi);

        Assert.False(PnpSetupApiIdentity.NeedsPciOemSubsystemRecovery([
            Gpu + "&REV_A1", Gpu + "&REV_A2"
        ]));
        var mismatchedWmiRevision = PnpSetupApiIdentity.Merge(
            [Gpu + "&REV_A1"], [],
            [GpuOem + "&REV_A2", GpuOem], [], null, null);
        Assert.Equal([Gpu + "&REV_A1"], mismatchedWmiRevision.HardwareIds);
        Assert.False(mismatchedWmiRevision.UsedSetupApi);

        var mismatchedNativeRevisions = PnpSetupApiIdentity.Merge(
            [Gpu], [],
            [GpuOem + "&REV_A1", GpuOem + "&REV_A2"], [], null, null);
        Assert.Equal([Gpu], mismatchedNativeRevisions.HardwareIds);
        Assert.False(mismatchedNativeRevisions.UsedSetupApi);
    }

    [Theory]
    [InlineData(GpuOem)]
    [InlineData(@"PCI\VEN_9999&DEV_1234")]
    [InlineData(@"HDAUDIO\FUNC_01&VEN_10DE&DEV_009B")]
    [InlineData(@"PCI\VEN_10DE")]
    [InlineData(@"PCI\VEN_10DE&DEV_28A0\PRIVATE")]
    public void AlreadyQualifiedUnknownVendorOrIncompleteWmiNotInOemLookup(
        string original)
    {
        Assert.False(PnpSetupApiIdentity.NeedsPciOemSubsystemRecovery(
            [original]));
    }

    [Fact]
    public void ContradictoryWmiModelsCannotImportForeignOemSubsys()
    {
        var originals = new[] { Gpu, @"PCI\VEN_10DE&DEV_1F15" };
        Assert.False(PnpSetupApiIdentity.NeedsPciOemSubsystemRecovery(originals));
        var id = PnpSetupApiIdentity.Merge(
            originals, [], [GpuOem], [], null, null);
        Assert.Equal(originals, id.HardwareIds);
        Assert.False(id.UsedSetupApi);
    }

    [Fact]
    public void SignedDriverAloneCannotOverrideAuthoritativeWmiPciModel()
    {
        // SignedDriver HardwareID alone never overrides a complete WMI
        // HardwareID. A separate missing CompatibleID may legitimately
        // use SignedDriver fallback and is outside this assertion.
        var id = PnpSetupApiIdentity.Merge(
            [Gpu], [], [], [], GpuOem, null);
        Assert.Equal([Gpu], id.HardwareIds);
        Assert.False(id.UsedSignedDriver);
        Assert.False(id.UsedSetupApi);
    }

    [Fact]
    public void NativeCompatibleIdsAreScopedToExactlySameModelOrPciClass()
    {
        var id = PnpSetupApiIdentity.Merge(
            [Gpu], [], [GpuOem], [
                KillerOem, @"PCI\VEN_10DE&DEV_1F15", @"PCI\CC_0300", Gpu
            ], null, null);
        Assert.Equal([Gpu, GpuOem], id.HardwareIds);
        Assert.Equal([@"PCI\CC_0300", Gpu], id.CompatibleIds);
        Assert.True(id.UsedSetupApi);
        Assert.Equal(0, DriverUpdateMatcher.MatchStrength(
            Device(id, "Display"), Offer(KillerOem)));
    }

    [Fact]
    public void ExistingWmiSubsysStaysAuthoritativeOverNativeOrSigned()
    {
        var id = PnpSetupApiIdentity.Merge(
            [GpuOem], [], [GpuOtherOem], [], GpuOtherOem, null);
        Assert.Equal([GpuOem], id.HardwareIds);
        Assert.False(id.UsedSetupApi);
        Assert.False(id.UsedSignedDriver);
    }

    [Fact]
    public void OversizedNativePropertyCannotInventVerifiedOemIdentity()
    {
        var many = Enumerable.Repeat(
            GpuOem, PnpSetupApiIdentity.MaximumIds + 1).ToArray();
        var id = PnpSetupApiIdentity.Merge(
            [Gpu], [], many, [], null, null);
        Assert.Equal([Gpu], id.HardwareIds);
        Assert.False(id.UsedSetupApi);
    }
}
