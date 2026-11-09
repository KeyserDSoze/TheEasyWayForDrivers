using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;
using TheEasyWayForDrivers.Core.Update;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

/// <summary>
/// Windows PnP can expose only VEN+DEV+SUBSYS+REV, while the same
/// authenticated WUA Type=Driver pass declares VEN+DEV+SUBSYS. This is
/// diagnostic only: no Windows Compatible ID, rank or safe update is
/// implied by dropping the terminal two-hex REV.
/// </summary>
public sealed class WuaSubsysRevisionReviewTests
{
    private const string Gpu = @"PCI\VEN_10DE&DEV_28A0&SUBSYS_16A217AA";
    private const string Killer = @"PCI\VEN_8086&DEV_51F1&SUBSYS_16721A56";
    private const string NvidiaAudio =
        @"HDAUDIO\FUNC_01&VEN_10DE&DEV_009B&SUBSYS_12345678";
    private const string IntelAudio =
        @"INTELAUDIO\FUNC_01&VEN_10EC&DEV_0295&SUBSYS_000010EC";
    private const string NvidiaSwc =
        @"SWC\VEN_10DE&DEV_28A0&SUBSYS_16A217AA";

    private static DriverInfo Device(string hardware, string deviceClass = "Unknown",
        IReadOnlyList<string>? compatible = null) =>
        new("PRIVATE_PNP_INSTANCE_SERIAL", "Generic Windows device", "Microsoft",
            deviceClass, "Microsoft", "31.0.0.0", null, "oem42.inf",
            true, true, 0, [hardware], compatible ?? []);

    private static DriverUpdateInfo Wua(string hardware,
        int number = 1, string source = "Windows Update online",
        string title = "Windows driver", string driverClass = "Display",
        IReadOnlyList<string>? extra = null, bool advanced = false) =>
        new($"00000000-0000-4000-8000-{number:D12}", title,
            null, driverClass, "Microsoft", null, 25000, false,
            "Microsoft", null, hardware, null,
            IsAdvancedCandidate: advanced, SearchSource: source,
            AdditionalHardwareIds: extra);

    private static DriverSearchReport Report(params DriverUpdateInfo[] updates) =>
        new(updates, [
            new DriverSearchPassStatus("Windows Update online",
                "Completed", updates.Length, "PRIVATE_SERVER_ADDRESS")
        ]);

    [Theory]
    [InlineData(Gpu, Gpu + "&REV_A1", "Display")]
    [InlineData(Killer, Killer + "&REV_02", "Net")]
    [InlineData(NvidiaAudio, NvidiaAudio + "&REV_01", "MEDIA")]
    [InlineData(IntelAudio, IntelAudio + "&REV_0A", "MEDIA")]
    [InlineData(NvidiaSwc, NvidiaSwc + "&REV_0B", "SoftwareComponent")]
    public void SameOemSubsysWithoutRevIsVisibleOnlyAsManualWuaHint(
        string declared, string specific, string deviceClass)
    {
        var pc = Device(specific, deviceClass);
        var offer = Wua(declared);

        Assert.Equal(0, DriverUpdateMatcher.MatchStrength(pc, offer));
        Assert.Contains(declared, InfModelEvidence.RevisionlessSubsysReviewKeys(pc));
        Assert.Contains(declared, WuaGenericModelCorrelation.ReviewModelKeys(pc));
        Assert.DoesNotContain(declared,
            WuaGenericModelCorrelation.PublicReviewModelKeys(pc));
        Assert.True(WuaGenericModelCorrelation.IsWeakModelOnlyMatch(pc, offer));

        var prepared = DeviceWuaMatchGroups.Prepare([offer]);
        Assert.True(prepared.UsesPnpIndex);
        var found = prepared.ForDevice(pc);
        Assert.Empty(found.Ordinary);
        Assert.Empty(found.ReviewOnly);
        Assert.Equal(offer.Id, Assert.Single(found.ModelOnlyReview!).Id);
        Assert.Equal(1, found.TotalCorrelated);
        Assert.Empty(QuickUpdatePolicy.AllOrdinaryIds(found.Ordinary));

        var audit = WuaPnpAssociationAudit.Create(
            [pc], Report(offer), inventoryComplete: true);
        Assert.True(audit.SearchVerified);
        Assert.Equal(1, audit.ConfirmedGuids);
        Assert.Equal(0, audit.ExactHardware);
        Assert.Equal(0, audit.CompatibleOnly);
        Assert.Equal(1, audit.GenericModelOnly);
        Assert.Equal(0, audit.Unattributed);
        Assert.False(audit.IsInstallable);
        Assert.Contains("SUBSYS senza REV", audit.Text);
        Assert.DoesNotContain("PRIVATE_", audit.Text);
        Assert.DoesNotContain("16A217AA", audit.Text);
        Assert.DoesNotContain(specific, audit.Text);

        // Even if a WUA GUID is otherwise ordinarily eligible, the
        // per-device update button never gains authority from this hint.
        Assert.True(QuickUpdatePolicy.IsEligible(offer));
        Assert.False(found.Ordinary.Any());
    }

    [Fact]
    public void EightCategoryMatrixAndCoverageTextCountReviewButNotDriverOffer()
    {
        var gpu = Device(Gpu + "&REV_A1", "Display");
        var killer = Device(Killer + "&REV_02", "Net");
        var audio = Device(NvidiaAudio + "&REV_01", "MEDIA");
        var swc = Device(NvidiaSwc + "&REV_0B", "SoftwareComponent");

        var report = Report(Wua(Gpu, 1), Wua(Killer, 2),
            Wua(NvidiaAudio, 3), Wua(NvidiaSwc, 4));
        var rows = ReferenceDriverCoverage.Create(
            [gpu, killer, audio, swc], report);

        Assert.Equal(8, rows.Count);
        Assert.Equal(4, rows.Sum(row => row.Devices));
        Assert.Equal(0, rows.Sum(row => row.WindowsDriverCandidates));
        Assert.Equal(4, rows.Sum(row => row.WindowsModelReviewHints));
        Assert.Equal(1, rows[5].WindowsModelReviewHints); // NVIDIA GPU
        Assert.Equal(1, rows[4].WindowsModelReviewHints); // Killer Wi-Fi
        Assert.Equal(1, rows[6].WindowsModelReviewHints); // NVIDIA HD Audio
        Assert.Equal(1, rows[7].WindowsModelReviewHints); // NVIDIA Platform

        var presentation = ReferenceCoveragePresentation.Create(
            [gpu, killer, audio, swc], report, inventoryComplete: true);
        Assert.Contains("SUBSYS senza REV", presentation[5].NextStep);
        Assert.False(presentation[5].IsInstallable);

        var qa = ScanCoverageText.Create(report, [], 4, 0, 0,
            [gpu, killer, audio, swc], inventoryComplete: true);
        Assert.Contains("SUBSYS senza REV", qa);
        Assert.Contains("solo modello generico", qa);
        Assert.DoesNotContain("PRIVATE_", qa);
        Assert.DoesNotContain(Gpu, qa);
        Assert.DoesNotContain(Killer, qa);
        Assert.DoesNotContain("00000000-0000-4000", qa);
    }

    [Theory]
    [InlineData(Gpu + "&REV_A1", @"PCI\VEN_10DE&DEV_28A0&SUBSYS_16A217AB")]
    [InlineData(Gpu + "&REV_A1", @"PCI\VEN_10DE&DEV_28A1&SUBSYS_16A217AA")]
    [InlineData(Gpu + "&REV_A1", @"PCI\VEN_10DE&DEV_28A0&SUBSYS_16A217AA&REV_A0")]
    [InlineData(Gpu + "&REV_A1", @"PCI\VEN_10DE&DEV_28A0&SUBSYS_16A217AA&UID_01")]
    [InlineData(Gpu + "&REV_A1", @"PCI\VEN_10DE")]
    [InlineData(Gpu + "&REV_A1", @"PCI\CC_030000")]
    [InlineData(Gpu + "&REV_A1", @"PCI\VEN_1002&DEV_28A0&SUBSYS_16A217AA")]
    [InlineData(Killer + "&REV_02", @"PCI\VEN_8086&DEV_51F1&SUBSYS_16721A57")]
    [InlineData(NvidiaAudio + "&REV_01",
        @"HDAUDIO\FUNC_02&VEN_10DE&DEV_009B&SUBSYS_12345678")]
    public void WrongSubsysOrModelOrFakeSuffixNeverCreatesWuaReview(
        string hardware, string declared)
    {
        var pc = Device(hardware);
        var offer = Wua(declared);
        Assert.False(WuaGenericModelCorrelation.IsWeakModelOnlyMatch(pc, offer));
        var result = DeviceWuaMatchGroups.Create(pc, [offer]);
        Assert.Empty(result.Ordinary);
        Assert.Empty(result.ReviewOnly);
        Assert.Empty(result.ModelOnlyReview!);
    }

    [Theory]
    [InlineData(Gpu + "&REV_A")]
    [InlineData(Gpu + "&REV_A12")]
    [InlineData(Gpu + "&REV_ZZ")]
    [InlineData(Gpu + "&REV_A1&UID_01")]
    [InlineData(Gpu + @"&REV_A1\PRIVATE_INSTANCE")]
    [InlineData(@"PCI\VEN_10DE&DEV_28A0&SUBSYS_16A217AA&CC_030000&REV_A1")]
    [InlineData(@"PCI\VEN_10DE&DEV_28A0&REV_A1")]
    public void MalformedOrNonterminalWindowsPnpIdYieldsNoSubsysKey(string id)
    {
        Assert.Empty(InfModelEvidence.RevisionlessSubsysReviewKeys(
            Device(id)));
        Assert.False(WuaGenericModelCorrelation.IsWeakModelOnlyMatch(
            Device(id), Wua(Gpu)));
    }

    [Fact]
    public void UntrustedSourceFirmwareAndInvalidGuidCannotBecomeReview()
    {
        var device = Device(Gpu + "&REV_A1");
        foreach (var offer in new[]
        {
            Wua(Gpu, source: "Unknown third party vendor"),
            Wua(Gpu, title: "NVIDIA Firmware", driverClass: "Firmware"),
            Wua(Gpu) with { Id = "not-a-guid" },
            Wua(Gpu, driverClass: "BIOS")
        })
        {
            Assert.False(WuaGenericModelCorrelation.IsWeakModelOnlyMatch(
                device, offer));
            var groups = DeviceWuaMatchGroups.Create(device, [offer]);
            Assert.Empty(groups.ModelOnlyReview!);
        }
    }

    [Fact]
    public void SecondaryWuaHardwareIdCanBeReviewHintButNotAnExtraGuid()
    {
        var pc = Device(Killer + "&REV_02", "Net");
        var update = Wua(@"PCI\VEN_9999&DEV_0001",
            extra: [Killer], advanced: true,
            source: "Windows Update online · avanzato");
        var found = DeviceWuaMatchGroups.Create(pc, [update]);

        Assert.Empty(found.Ordinary);
        Assert.Empty(found.ReviewOnly);
        Assert.Single(found.ModelOnlyReview!);
        Assert.Equal(1, found.TotalCorrelated);
        Assert.Empty(QuickUpdatePolicy.AllOrdinaryIds(found.Ordinary));
    }

    [Fact]
    public void RealCompatibleIdOrExactHardwareIdAlwaysOutranksWeakSubsys()
    {
        var specific = Gpu + "&REV_A1";
        var weak = Wua(Gpu, 100);
        var actual = Device(specific, "Display", [Gpu]);
        var strong = DeviceWuaMatchGroups.Create(actual, [weak]);
        Assert.Equal(1, DriverUpdateMatcher.MatchStrength(actual, weak));
        Assert.Equal(weak.Id, Assert.Single(strong.Ordinary).Id);
        Assert.Empty(strong.ModelOnlyReview!);

        var sameGuidStrong = Wua(specific, 100);
        var merged = DeviceWuaMatchGroups.Create(
            Device(specific, "Display"), [weak, sameGuidStrong]);
        Assert.Equal(sameGuidStrong.Id, Assert.Single(merged.Ordinary).Id);
        Assert.Empty(merged.ReviewOnly);
        Assert.Empty(merged.ModelOnlyReview!);
    }

    [Fact]
    public void CompatibleOnlyOrMarketingNameCannotCreateSubsysReview()
    {
        var device = Device(@"ROOT\GENERIC", "Display",
            [Gpu + "&REV_A1"]) with
        {
            Name = "NVIDIA RTX 4060 Laptop GPU",
            Manufacturer = "NVIDIA",
            DriverProvider = "NVIDIA"
        };
        Assert.Empty(InfModelEvidence.RevisionlessSubsysReviewKeys(device));
        Assert.Empty(WuaGenericModelCorrelation.ReviewModelKeys(device));
        Assert.False(WuaGenericModelCorrelation.IsWeakModelOnlyMatch(
            device, Wua(Gpu)));
        Assert.Empty(DeviceWuaMatchGroups.Create(device, [Wua(Gpu)])
            .ModelOnlyReview!);
    }

    [Fact]
    public void ExistingPublicModelReviewRemainsFunctionalAfterIndexExtension()
    {
        var pc = Device(Gpu + "&REV_A1");
        var generic = @"PCI\VEN_10DE&DEV_28A0";
        var wua = Wua(generic);

        Assert.Contains(generic,
            WuaGenericModelCorrelation.PublicReviewModelKeys(pc));
        Assert.True(WuaGenericModelCorrelation.IsWeakModelOnlyMatch(pc, wua));
        var report = DeviceWuaMatchGroups.Create(pc, [wua]);
        Assert.Equal(0, DriverUpdateMatcher.MatchStrength(pc, wua));
        Assert.Equal(wua.Id, Assert.Single(report.ModelOnlyReview!).Id);
        Assert.Empty(report.Ordinary);
    }
}
