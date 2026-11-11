using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;
using TheEasyWayForDrivers.Core.Update;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

/// <summary>WUA GUID and source provenance must agree in every scan view.</summary>
public sealed class WuaSourceProvenanceConsistencyTests
{
    private const string Gpu = @"PCI\VEN_10DE&DEV_28A0";
    private const string Audio = @"HDAUDIO\FUNC_01&VEN_10DE&DEV_009B";
    private const string Killer = @"PCI\VEN_8086&DEV_51F1";
    private const string GuidOne = "00000000-0000-4000-8000-000000000346";
    private const string GuidTwo = "00000000-0000-4000-8000-000000000347";

    private static DriverInfo Device(string serial, string hardware,
        string type = "Display") =>
        new(serial, "Generic Windows device", "Microsoft", type,
            "Microsoft", "1.0.0.0", null, "oem10.inf",
            true, true, 0, [hardware], []);

    private static DriverUpdateInfo Update(string guid, string hardware,
        string source = "Windows Update online") =>
        new(guid, "Windows driver", null, "Display", "Microsoft",
            null, null, false, "Microsoft", null, hardware, null,
            SearchSource: source);

    private static DriverSearchReport Report(
        IReadOnlyList<DriverUpdateInfo> offers, bool completed = true) =>
        new(offers, [new DriverSearchPassStatus(
            "Windows Update online", completed ? "Completed" : "Failed",
            completed ? offers.Count : 0, "PRIVATE_PROXY_PATH")]);

    [Theory]
    [InlineData("Windows Update online", false, true)]
    [InlineData("Windows configurato", false, true)]
    [InlineData("Microsoft Update online", false, true)]
    [InlineData("Windows Update", false, true)]
    [InlineData("Windows Update online · avanzato", true, true)]
    [InlineData("Microsoft Update online · avanzato", true, true)]
    [InlineData("Windows Update online · avanzato", false, false)]
    [InlineData("Microsoft Update online · software", true, false)]
    [InlineData("Third-party driver catalog", false, false)]
    [InlineData("Windows Update online · advanced", true, false)]
    public void KnownSourceAndValidGuidAreBothRequired(
        string source, bool advanced, bool expected)
    {
        var item = Update(GuidOne, Gpu, source) with
        {
            IsAdvancedCandidate = advanced
        };
        Assert.Equal(expected, WindowsDriverCandidateMerge.IsKnownWuaGuid(item));
        Assert.False(WindowsDriverCandidateMerge.IsKnownWuaGuid(
            item with { Id = "PRIVATE_NOT_GUID" }));
    }

    [Fact]
    public void UnknownOnlyGuidCannotAppearAsWuaOnAnyDiagnosticSurface()
    {
        var gpu = Device("PRIVATE_SERIAL_GPU", Gpu);
        var unknown = Update(GuidOne, Gpu, "Unverified OEM mirror") with
        {
            AdditionalHardwareIdsIncomplete = true
        };
        var report = Report([unknown]);

        Assert.Empty(WuaDiagnosticAssociations.Create([unknown]));
        var prepared = DeviceWuaMatchGroups.Prepare([unknown]);
        Assert.Equal(0, prepared.UniqueGuidCount);
        Assert.Equal(0, prepared.ForDevice(gpu).TotalCorrelated);
        var home = HomeVendorCoverageSummary.Create([gpu], report, true);
        Assert.Contains("0 candidati WUA", home[0].WindowsStatus);
        Assert.DoesNotContain("ID WUA incompleti", home[0].WindowsStatus);

        var inventory = VendorInventoryCoverage.Create([gpu], [unknown]);
        var nvidia = Assert.Single(inventory.Rows, x => x.Vendor == "NVIDIA");
        Assert.Equal(0, nvidia.DevicesWithWuaMatches);
        Assert.Equal(0, nvidia.WuaCorrelations);
        var eight = ReferenceDriverCoverage.Create([gpu], report);
        Assert.Equal(0, eight[5].WindowsDriverCandidates);

        var audit = WuaPnpAssociationAudit.Create([gpu], report, true);
        Assert.Equal(0, audit.ConfirmedGuids);
        Assert.Equal(0, audit.ExactHardware);
        Assert.Equal(0, audit.IncompleteWuaIds);
        Assert.False(audit.IsInstallable);
        var copy = ScanCoverageText.Create(report, inventory.Rows,
            1, 0, 0, [gpu], true, vendorCoverage: inventory);
        Assert.DoesNotContain("PRIVATE", copy);
        Assert.DoesNotContain(GuidOne, copy);
        Assert.DoesNotContain(Gpu, copy);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void UnknownGuidCollisionCannotAddUntrustedAudioId(
        bool untrustedFirst)
    {
        var gpu = Device("PRIVATE_GPU", Gpu);
        var audio = Device("PRIVATE_AUDIO", Audio, "MEDIA");
        var official = Update(GuidOne, Gpu);
        var unknown = Update(GuidOne, Audio, "External driver mirror") with
        {
            AdditionalHardwareIds = [Audio],
            AdditionalHardwareIdsIncomplete = true,
            Title = "NVIDIA firmware UEFI"
        };
        var rows = untrustedFirst
            ? new[] { unknown, official } : [official, unknown];
        var report = Report(rows);

        var grouped = WuaDiagnosticAssociations.Create(rows);
        Assert.Single(grouped);
        Assert.True(grouped[0].Matches(gpu));
        Assert.False(grouped[0].Matches(audio));
        Assert.Single(DeviceWuaMatchGroups.Create(gpu, rows).Ordinary);
        Assert.Equal(0, DeviceWuaMatchGroups.Create(audio, rows)
            .TotalCorrelated);
        var home = HomeVendorCoverageSummary.Create([gpu, audio], report, true);
        Assert.Contains("1 candidato WUA", home[0].WindowsStatus);
        Assert.DoesNotContain("ID WUA incompleti", home[0].WindowsStatus);
        var audit = WuaPnpAssociationAudit.Create([gpu, audio], report, true);
        Assert.Equal(1, audit.ConfirmedGuids);
        Assert.Equal(1, audit.ExactHardware);
        Assert.Equal(0, audit.IncompleteWuaIds);
        Assert.DoesNotContain("PRIVATE", audit.Text);
    }

    [Fact]
    public void TwoRealWuaPassesCanDescribeTwoPnpModelsOfOneGuid()
    {
        var gpu = Device("PRIVATE_GPU", Gpu);
        var audio = Device("PRIVATE_AUDIO", Audio, "MEDIA");
        var first = Update(GuidOne, Gpu, "Windows configurato");
        var second = Update(GuidOne, Audio, "Microsoft Update online") with
        {
            AdditionalHardwareIdsIncomplete = true
        };
        var grouped = WuaDiagnosticAssociations.Create([first, second]);
        Assert.Single(grouped);
        Assert.True(grouped[0].Matches(gpu));
        Assert.True(grouped[0].Matches(audio));
        Assert.Single(DeviceWuaMatchGroups.Create(gpu, [first, second])
            .Ordinary);
        Assert.Single(DeviceWuaMatchGroups.Create(audio, [first, second])
            .Ordinary);
        var audit = WuaPnpAssociationAudit.Create([gpu, audio],
            Report([first, second]), true);
        Assert.Equal(1, audit.ConfirmedGuids);
        Assert.Equal(1, audit.IncompleteWuaIds);
        Assert.Equal(1, audit.ExactHardware);
        Assert.Equal(0, audit.Unattributed);
        Assert.False(audit.IsInstallable);
    }

    [Fact]
    public void LegitimateAdvancedKillerGuidStaysReviewOnly()
    {
        var killer = Device("PRIVATE_KILLER", Killer, "Net");
        var advanced = Update(GuidTwo, Killer,
            "Windows Update online · avanzato") with
        {
            IsAdvancedCandidate = true
        };
        var unknown = Update(GuidOne, Killer, "Unknown seller");
        var result = DeviceWuaMatchGroups.Create(killer, [advanced, unknown]);
        Assert.Empty(result.Ordinary);
        Assert.Equal(GuidTwo, Assert.Single(result.ReviewOnly).Id);
        Assert.Empty(result.ModelOnlyReview!);
        Assert.False(QuickUpdatePolicy.IsEligible(advanced));
        Assert.Single(WuaDiagnosticAssociations.Create([advanced, unknown]));
    }

    [Fact]
    public void FailedComSearchDoesNotValidateEvenKnownSourceNames()
    {
        var gpu = Device("PRIVATE_GPU", Gpu);
        var report = Report([Update(GuidOne, Gpu)], completed: false);
        var home = HomeVendorCoverageSummary.Create([gpu], report, true);
        Assert.Contains("non verificata", home[0].WindowsStatus);
        Assert.DoesNotContain("1 candidato", home[0].WindowsStatus);
        var audit = WuaPnpAssociationAudit.Create([gpu], report, true);
        Assert.False(audit.SearchVerified);
        Assert.Equal(0, audit.ConfirmedGuids);
        Assert.Equal(0, ReferenceDriverCoverage.Create([gpu], report)[5]
            .WindowsDriverCandidates);
    }

    [Fact]
    public void UnknownMissingIdsAndBadGuidCannotInflateAudit()
    {
        var gpu = Device("PRIVATE_GPU", Gpu);
        var unknown = Update(GuidOne, "", "Random catalog") with
        {
            AdditionalHardwareIdsIncomplete = true
        };
        var invalid = Update("INVALID_GUID", Gpu);
        var genuine = Update(GuidTwo, "") with
        {
            AdditionalHardwareIdsIncomplete = true
        };
        var audit = WuaPnpAssociationAudit.Create([gpu],
            Report([unknown, invalid, genuine]), true);
        Assert.Equal(1, audit.ConfirmedGuids);
        Assert.Equal(1, audit.MissingWuaIds);
        Assert.Equal(1, audit.IncompleteWuaIds);
        Assert.Equal(1, audit.Unattributed);
    }
}
