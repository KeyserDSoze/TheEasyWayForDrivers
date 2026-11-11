using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;
using TheEasyWayForDrivers.Core.Update;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

/// <summary>
/// The eight Driver Easy comparison rows must never manufacture WUA
/// Type=Driver candidates from IPC junk or unknown OEM/manual sources.
/// </summary>
public sealed class ReferenceVerifiedWuaGuidCoverageTests
{
    private const string Gpu = @"PCI\VEN_10DE&DEV_28A0";
    private const string Audio = @"HDAUDIO\FUNC_01&VEN_10DE&DEV_009B";
    private const string Killer =
        @"PCI\VEN_8086&DEV_51F1&SUBSYS_16721A56";
    private const string GuidOne =
        "00000000-0000-4000-8000-000000000345";
    private const string GuidTwo =
        "00000000-0000-4000-8000-000000000346";

    private static DriverInfo Device(string instance, string hardware,
        string deviceClass) =>
        new(instance, "Microsoft generic device", "Microsoft",
            deviceClass, "Microsoft", "1.0", null, "oem42.inf",
            true, true, 0, [hardware], []);

    private static DriverUpdateInfo Update(string id, string hardware,
        string source = "Windows Update online") =>
        new(id, "Windows driver", null, "Display", "Microsoft",
            null, null, false, "Microsoft", null, hardware, null,
            SearchSource: source);

    private static DriverSearchPassStatus Pass(string outcome) =>
        new("Windows Update online", outcome, 1,
            "PRIVATE_INTERNAL_SERVER_PATH");

    private static DriverSearchReport Report(
        IReadOnlyList<DriverUpdateInfo> updates,
        params DriverSearchPassStatus[] passes) =>
        new(updates, passes);

    [Fact]
    public void OnlyVerifiedWuaGuidsCountForEightCategoryMatrix()
    {
        var gpu = Device("PRIVATE_NVIDIA_SERIAL", Gpu, "Display");
        var updates = new[]
        {
            Update(GuidOne, Gpu),
            Update("NOT_A_WUA_GUID", Gpu),
            Update(GuidTwo, Gpu, "Unverified third-party repository"),
            Update(GuidTwo, Gpu,
                "Windows Update online · avanzato") // advanced flag absent
        };
        var rows = ReferenceDriverCoverage.Create(
            [gpu], Report(updates, Pass("Completed")));

        Assert.Equal(8, rows.Count);
        Assert.Equal(1, rows[5].Devices);
        Assert.Equal(1, rows[5].WindowsDriverCandidates);
        Assert.Equal(0, rows[5].WindowsModelReviewHints);
        var text = ScanCoverageText.Create(
            Report(updates, Pass("Completed")), [], 1, 0, 0,
            [gpu], inventoryComplete: true);
        Assert.Contains("NVIDIA · GPU: 1 dispositivi classificati; 1 candidati WUA Type=Driver", text);
        Assert.DoesNotContain("PRIVATE", text);
        Assert.DoesNotContain("NOT_A_WUA_GUID", text);
        Assert.DoesNotContain(GuidOne, text);
        Assert.DoesNotContain(GuidTwo, text);
        Assert.DoesNotContain(Gpu, text);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void UnknownSameGuidCannotInventAudioAssociation(
        bool unknownFirst)
    {
        var gpu = Device("PRIVATE_GPU", Gpu, "Display");
        var audio = Device("PRIVATE_AUDIO", Audio, "MEDIA");
        var official = Update(GuidOne, Gpu);
        var untrusted = Update(GuidOne.ToUpperInvariant(), Audio,
            "Unverified OEM mirror") with
        {
            AdditionalHardwareIds = [Audio],
            AdditionalHardwareIdsIncomplete = true,
            Title = "NVIDIA Firmware UEFI"
        };
        var report = Report(unknownFirst
                ? [untrusted, official] : [official, untrusted],
            Pass("Completed"));
        var rows = ReferenceDriverCoverage.Create([gpu, audio], report);
        Assert.Equal(1, rows[5].WindowsDriverCandidates);
        Assert.Equal(0, rows[6].WindowsDriverCandidates);
        Assert.Equal(1, rows[6].Devices);
        Assert.Equal(1, rows.Sum(row => row.WindowsDriverCandidates));
        Assert.All(rows, row => Assert.True(
            row.WindowsDriverCandidates >= 0));
    }

    [Fact]
    public void AllFailedDriverPassCannotConfirmStaleGuidFromIpc()
    {
        var gpu = Device("PRIVATE_GPU", Gpu, "Display");
        var report = Report([Update(GuidOne, Gpu)],
            Pass("Failed"), Pass("NetworkError"));
        var rows = ReferenceDriverCoverage.Create([gpu], report);
        Assert.Equal(1, rows[5].Devices);
        Assert.Equal(0, rows[5].WindowsDriverCandidates);
        Assert.Contains("ricerca WUA Type=Driver non verificata",
            rows[5].DisplayLine(true, !report.AllFailed, false));
        var presentation = ReferenceCoveragePresentation.Create(
            [gpu], report, true);
        Assert.Contains("non verificata",
            presentation[5].WindowsDriverStatus);
        Assert.DoesNotContain("0 candidati",
            presentation[5].WindowsDriverStatus);
    }

    [Fact]
    public void CompletedPassKeepsAdvancedReviewGuidWithoutInstallPermission()
    {
        var gpu = Device("PRIVATE_GPU", Gpu, "Display");
        var advanced = Update(GuidOne, Gpu,
            "Windows Update online · avanzato") with
            { IsAdvancedCandidate = true };
        var report = Report([advanced],
            new DriverSearchPassStatus("Windows Update online · avanzato",
                "Completed", 1, "PRIVATE_SOURCE"),
            Pass("Failed"));
        var rows = ReferenceDriverCoverage.Create([gpu], report);
        Assert.Equal(1, rows[5].WindowsDriverCandidates);
        Assert.True(report.IsPartial);
        Assert.False(QuickUpdatePolicy.IsEligible(advanced));
        Assert.Empty(QuickUpdatePolicy.AllOrdinaryIds([advanced]));
        Assert.Contains("ricerca parziale",
            rows[5].DisplayLine(true, true, false,
                driverPartial: report.IsPartial));
    }

    [Fact]
    public void SameGuidWithTwoOfficialPnpModelsCountsForTwoDevices()
    {
        var gpu = Device("PRIVATE_GPU", Gpu, "Display");
        var audio = Device("PRIVATE_AUDIO", Audio, "MEDIA");
        var official1 = Update(GuidOne, Gpu, "Windows configurato");
        var official2 = Update(GuidOne.ToUpperInvariant(), Audio,
            "Microsoft Update online");
        var report = Report([official1, official2],
            new DriverSearchPassStatus("Windows configurato",
                "Completed", 1, "PRIVATE_SOURCE"),
            new DriverSearchPassStatus("Microsoft Update online",
                "Completed", 1, "PRIVATE_SOURCE"));
        var rows = ReferenceDriverCoverage.Create([gpu, audio], report);

        Assert.Equal(1, rows[5].WindowsDriverCandidates);
        Assert.Equal(1, rows[6].WindowsDriverCandidates);
        Assert.Equal(2, rows.Sum(row => row.WindowsDriverCandidates));
        // One update GUID, two device categories; not two packages.
        Assert.Single(WuaDiagnosticAssociations.Create(
            [official1, official2]));
    }

    [Fact]
    public void WeakPciSubsysReviewRemainsReviewOnlyAndDoesNotAddDriver()
    {
        var gpu = Device("PRIVATE_GPU",
            Gpu + "&SUBSYS_16A217AA&REV_A1", "Display");
        var review = Update(GuidOne, Gpu);
        var report = Report([review], Pass("Completed"));
        var rows = ReferenceDriverCoverage.Create([gpu], report);
        Assert.Equal(1, rows[5].Devices);
        Assert.Equal(0, rows[5].WindowsDriverCandidates);
        Assert.Equal(1, rows[5].WindowsModelReviewHints);
        Assert.False(QuickUpdatePolicy.IsEligible(
            review with { IsAdvancedCandidate = true }));
    }

    [Fact]
    public void SoftwareHintsRemainSeparateAndDoNotCreateDriverGuids()
    {
        var report = new DriverSearchReport(
            [Update(GuidOne, Gpu, "External OEM")], [Pass("Failed")],
            [new WuaDriverRelatedSoftware(
                GuidTwo, "Killer Networking Software",
                "Windows Update online · componenti software")],
            [new DriverSearchPassStatus(
                "Windows Update online · componenti software",
                "Completed", 1, "OK")]);
        var rows = ReferenceDriverCoverage.Create([], report);
        Assert.Equal(0, rows.Sum(row => row.WindowsDriverCandidates));
        Assert.Equal(1, rows[3].SoftwareTitleHints);
        Assert.True(rows[3].SoftwareOnly);
        Assert.Contains("non sono driver confermati",
            rows[3].DisplayLine(true, !report.AllFailed, true));
    }

    [Fact]
    public void MissingHardwareIdsDoesNotBecomeKillerUpdateByTitle()
    {
        var wifi = Device("PRIVATE_KILLER", Killer, "Net");
        var report = Report([Update(GuidOne,
            @"PCI\VEN_8086&DEV_1111")],
            Pass("Completed"));
        var rows = ReferenceDriverCoverage.Create([wifi], report);
        Assert.Equal(1, rows[4].Devices);
        Assert.Equal(0, rows[4].WindowsDriverCandidates);
    }
}
