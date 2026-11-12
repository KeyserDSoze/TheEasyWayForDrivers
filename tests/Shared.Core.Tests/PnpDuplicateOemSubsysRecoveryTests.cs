using System.IO.Compression;
using System.Text;
using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

/// <summary>
/// One Windows PnP instance may carry both VEN+DEV and VEN+DEV+SUBSYS
/// hardware models in duplicated rows. Preserve the exact OEM ID only
/// when every row independently agrees on the same verified PCI model.
/// </summary>
public sealed class PnpDuplicateOemSubsysRecoveryTests
{
    private const string NvidiaBase = @"PCI\VEN_10DE&DEV_28A0";
    private const string NvidiaOem =
        @"PCI\VEN_10DE&DEV_28A0&SUBSYS_16A217AA";
    private const string NvidiaOtherOem =
        @"PCI\VEN_10DE&DEV_28A0&SUBSYS_16A21043";
    private const string NvidiaOtherDevice = @"PCI\VEN_10DE&DEV_1F15";
    private const string KillerBase = @"PCI\VEN_8086&DEV_51F1";
    private const string KillerOem =
        @"PCI\VEN_8086&DEV_51F1&SUBSYS_16721A56";
    private const string RealtekBase = @"PCI\VEN_10EC&DEV_8125";
    private const string RealtekOem =
        @"PCI\VEN_10EC&DEV_8125&SUBSYS_012310EC";
    private const string WuaGuid = "00000000-0000-4000-8000-000000000387";

    private static DriverInfo Device(string instance, string hardware,
        string name = "Windows Generic Adapter") =>
        new(instance, name, "Microsoft", "Unknown", "Microsoft",
            "31.0.0.1", null, "PRIVATE_OEM42.inf", true, true, 0,
            [hardware], []);

    private static DriverUpdateInfo Update(string id) =>
        new(WuaGuid, "Driver available for review", null, "Display",
            "Microsoft", null, null, false, "Microsoft", null,
            id, null, SearchSource: "Windows Update online");

    private static DriverSearchReport Report(string id) =>
        new([Update(id)], [
            new DriverSearchPassStatus(
                "Windows Update online", "Completed", 1, "verified")
        ]);

    private static MemoryStream Zip(string model)
    {
        var data = new MemoryStream();
        using (var zip = new ZipArchive(
            data, ZipArchiveMode.Create, leaveOpen: true))
        {
            using var writer = new StreamWriter(
                zip.CreateEntry("driver/oem.inf").Open(),
                new UTF8Encoding(false));
            writer.Write("[Version]\nSignature=\"$Windows NT$\"\n" +
                "Class=Display\nDriverVer=10/04/2026,32.0.15.7000\n" +
                "[Manufacturer]\n%Device%=Models,NTamd64.10.0...22000\n" +
                "[Models.NTamd64.10.0...22000]\n" +
                "%Device%=Install," + model + "\n");
        }
        data.Position = 0;
        return data;
    }

    [Theory]
    [InlineData(NvidiaBase, NvidiaOem)]
    [InlineData(KillerBase, KillerOem)]
    [InlineData(RealtekBase, RealtekOem)]
    public void SameInstanceSpecificOemModelReplacesOnlyBareModel(
        string bare, string qualified)
    {
        var first = Device("PRIVATE_SAME_PNP", bare);
        var second = Device("private_same_pnp", qualified,
            "Official Device Identity");
        Assert.NotNull(PnpKnownVendorIdentity.FromId(bare));
        Assert.Equal(
            MicrosoftUpdateCatalogSearch.TryGetPublicModelKey(bare),
            MicrosoftUpdateCatalogSearch.TryGetPublicModelKey(qualified));

        var chosen = Assert.Single(
            PnpDistinctTrustedDevices.Create([first, second]));
        Assert.Same(second, chosen);
        Assert.Same(second, Assert.Single(
            PnpDistinctTrustedDevices.Create([second, first])));
        Assert.Equal(0, DriverUpdateMatcher.MatchStrength(
            first, Update(qualified)));
        Assert.Equal(2, DriverUpdateMatcher.MatchStrength(
            chosen, Update(qualified)));

        var audit = WuaPnpAssociationAudit.Create(
            [first, second], Report(qualified), true);
        Assert.Equal(1, audit.ExactHardware);
        Assert.Equal(0, audit.Unattributed);
        Assert.Equal(0, audit.ExcludedAmbiguousPnpInstances);
        Assert.False(audit.IsInstallable);
        Assert.DoesNotContain("PRIVATE", audit.Text);
        Assert.DoesNotContain(qualified, audit.Text);
    }

    [Fact]
    public void NvidiaOemSubsysSurvivesHomeEightRowsAndOemInf()
    {
        var bare = Device("PRIVATE_GPU", NvidiaBase,
            "Microsoft Basic Display Adapter");
        var precise = Device("private_gpu", NvidiaOem,
            "Generic Laptop Display");
        var devices = new[] { bare, precise };
        var report = Report(NvidiaOem);

        var home = HomeVendorCoverageSummary.Create(devices, report, true);
        Assert.Contains("1 dispositivo PnP", home[0].DeviceStatus);
        Assert.Contains("1 candidato WUA per ID", home[0].WindowsStatus);
        Assert.Equal(precise,
            HomeVendorCoverageSummary.ChooseLocalDriverStoreDevice(
                devices, "NVIDIA", true));

        var benchmark = ReferenceDriverCoverage.Create(devices, report);
        Assert.Equal(8, benchmark.Count);
        Assert.Equal(1, benchmark[5].Devices);
        Assert.Equal(1, benchmark[5].WindowsDriverCandidates);

        var vendor = VendorInventoryCoverage.Create(
            devices, report.Updates);
        Assert.Single(vendor.Rows);
        Assert.Equal(1, vendor.Rows.Sum(row => row.DevicesWithWuaMatches));
        var handoff = Assert.Single(
            VendorSourceDiscovery.Find(devices, report.Updates));
        Assert.Equal("NVIDIA", handoff.Vendor);
        Assert.Equal(1, handoff.WuaMatches);

        using var archive = Zip(NvidiaOem);
        var matched = LocalOemDriverComparison.Compare(
            archive, devices, "x64", 10, 0, 26100);
        var finding = Assert.Single(matched.Findings);
        Assert.True(finding.HasExactHardwareId);
        Assert.Equal(0, finding.DeviceOrdinal);
        Assert.Equal([0], matched.MatchedDeviceOrdinals);
        Assert.Equal([0], matched.PnpMatchedDeviceOrdinals);
        Assert.Equal(0, matched.ExcludedAmbiguousPnpInstances);
        Assert.False(matched.IsInstallable);
        Assert.False(matched.IsVerifiedCompatible);
        Assert.DoesNotContain("PRIVATE", matched.Summary);

        using var archiveWithoutSubsys = Zip(NvidiaOem);
        var weakOnly = LocalOemDriverComparison.Compare(
            archiveWithoutSubsys, [bare],
            "x64", 10, 0, 26100);
        Assert.Empty(weakOnly.Findings);
        Assert.False(weakOnly.IsInstallable);

        var summary = ScanCoverageText.Create(
            report, vendor.Rows, devices.Length, 0, 0,
            devices, true, vendorCoverage: vendor);
        Assert.DoesNotContain("PRIVATE", summary);
        Assert.DoesNotContain(NvidiaOem, summary);
        Assert.DoesNotContain(WuaGuid, summary);
    }

    [Fact]
    public void MatchingAliasesAndClassTokensKeepOriginalPnpOrdinal()
    {
        var first = Device("PRIVATE_GPU", NvidiaBase) with
        {
            CompatibleIds = [@"PCI\VEN_10DE&CC_0300", @"PCI\CC_0300"]
        };
        var second = Device("private_gpu", NvidiaOem) with
        {
            CompatibleIds = [NvidiaBase, @"PCI\CC_0300"]
        };
        var unrelated = Device("PRIVATE_KILLER", KillerOem, "Killer Wi-Fi");
        var chosen = PnpDistinctTrustedDevices.Create(
            [unrelated, first, second]);

        Assert.Equal(2, chosen.Count);
        Assert.Same(unrelated, chosen[0]);
        Assert.Same(second, chosen[1]);

        using var zip = Zip(NvidiaOem);
        var oem = LocalOemDriverComparison.Compare(zip,
            [unrelated, first, second], "x64", 10, 0, 26100);
        Assert.Single(oem.Findings);
        Assert.Equal([1], oem.MatchedDeviceOrdinals);
        Assert.Equal(1, oem.Findings[0].DeviceOrdinal);
        Assert.False(oem.IsInstallable);
    }

    [Theory]
    [InlineData(NvidiaOtherDevice)]
    [InlineData(KillerOem)]
    [InlineData(@"HDAUDIO\FUNC_01&VEN_10DE&DEV_009B")]
    [InlineData(@"PCI\VEN_10DE&DEV_28A0&SUBSYS_16A217AA\PRIVATE")]
    [InlineData(@"PCI\VEN_10DE&DEV_28A0&SUBSYS_FAKE")]
    public void ForeignModelOrMalformedIdCannotSupersedeBareModel(
        string foreign)
    {
        var first = Device("PRIVATE_GPU", NvidiaBase);
        var second = Device("private_gpu", foreign);
        var chosen = PnpDistinctTrustedDevices.Create([first, second]);
        // A different known vendor may fail closed even before
        // the promotion check; either way no OEM-specific match exists.
        Assert.DoesNotContain(chosen,
            driver => driver.HardwareIds.Contains(foreign) &&
                driver != first);
        Assert.All(chosen, driver =>
            Assert.NotEqual(2, DriverUpdateMatcher.MatchStrength(
                driver, Update(NvidiaOem))));
    }

    [Fact]
    public void TwoConflictingSubsysVariantsNeverGuessOneOemPackage()
    {
        var first = Device("PRIVATE_GPU", NvidiaBase);
        var nvidia = Device("private_gpu", NvidiaOem);
        var alternative = Device("PRIVATE_GPU", NvidiaOtherOem);
        var chosen = Assert.Single(
            PnpDistinctTrustedDevices.Create([first, nvidia, alternative]));
        Assert.Same(first, chosen);
        Assert.Equal(0, DriverUpdateMatcher.MatchStrength(
            chosen, Update(NvidiaOem)));
        Assert.Equal(0, DriverUpdateMatcher.MatchStrength(
            chosen, Update(NvidiaOtherOem)));
    }

    [Fact]
    public void QualifiedOnlyInCompatibleIdsIsNotPrimaryEvidence()
    {
        var first = Device("PRIVATE_GPU", NvidiaBase);
        var second = Device("private_gpu", NvidiaBase) with
        {
            CompatibleIds = [NvidiaOem]
        };
        Assert.Same(first, Assert.Single(
            PnpDistinctTrustedDevices.Create([first, second])));
        Assert.Equal(0, DriverUpdateMatcher.MatchStrength(
            first, Update(NvidiaOem)));
    }

    [Fact]
    public void UnknownVendorDoesNotGetPromotedToMoreExactOem()
    {
        var first = Device("PRIVATE_OTHER", @"PCI\VEN_9999&DEV_1234");
        var second = Device("private_other",
            @"PCI\VEN_9999&DEV_1234&SUBSYS_12345678");
        Assert.Same(first, Assert.Single(
            PnpDistinctTrustedDevices.Create([first, second])));
    }

    [Fact]
    public void AlreadyQualifiedFirstRowStaysAuthoritative()
    {
        var strong = Device("PRIVATE_GPU", NvidiaOem);
        var bare = Device("private_gpu", NvidiaBase);
        Assert.Same(strong, Assert.Single(
            PnpDistinctTrustedDevices.Create([strong, bare])));
        Assert.Equal(2, DriverUpdateMatcher.MatchStrength(
            strong, Update(NvidiaOem)));
    }

    [Fact]
    public void GenericFirstNvidiaRecoveryFromPreviousIssueStillWorks()
    {
        var first = Device("PRIVATE_GPU", @"PCI\VEN_10DE&CC_0300");
        var second = Device("private_gpu", NvidiaOem);
        Assert.Same(second, Assert.Single(
            PnpDistinctTrustedDevices.Create([first, second])));
    }
}
