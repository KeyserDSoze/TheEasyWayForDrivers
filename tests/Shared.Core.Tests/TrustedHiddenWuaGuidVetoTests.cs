using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;
using TheEasyWayForDrivers.Core.Update;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

/// <summary>
/// #517: trusted hidden WUA metadata is a same-GUID cross-pass veto,
/// never an ordinary update even when another pass recommends it.
/// </summary>
public sealed class TrustedHiddenWuaGuidVetoTests
{
    private const string Nvidia =
        @"PCI\VEN_10DE&DEV_28A0&SUBSYS_16A217AA";
    private const string Killer =
        @"PCI\VEN_8086&DEV_51F1&SUBSYS_16721A56";

    private static DriverUpdateInfo Update(
        string guid, string hardware = Nvidia,
        string source = "Windows Update online",
        bool hidden = false, bool advanced = false,
        bool optional = false, string title = "NVIDIA Display") =>
        new(guid, title, null, "Display", "Microsoft", null, null,
            false, "Microsoft", null, hardware, null,
            IsHidden: hidden, IsOptional: optional,
            IsAdvancedCandidate: advanced, SearchSource: source);

    private static DriverInfo Device(string hardware) =>
        new("PRIVATE_INSTANCE", "Generic Windows device", "Microsoft",
            "Display", "Microsoft", "1.0", null, "oem1.inf",
            true, true, 0, [hardware], []);

    [Theory]
    [InlineData(true, "B")]
    [InlineData(false, "P")]
    [InlineData(true, "N")]
    [InlineData(false, "D")]
    public void TrustedHiddenCrossPassBlocksUpdateAllAndFingerprint(
        bool hiddenFirst, string hiddenGuidFormat)
    {
        var id = Guid.NewGuid();
        var ordinary = Update(id.ToString("D"));
        var hidden = Update(id.ToString(hiddenGuidFormat),
            source: "Microsoft Update online", hidden: true,
            title: "NVIDIA hidden display");
        var independent = Update(Guid.NewGuid().ToString("D"),
            hardware: Killer);
        var rows = hiddenFirst
            ? new[] { hidden, ordinary, independent }
            : [independent, ordinary, hidden];

        Assert.Equal([independent.Id], QuickUpdatePolicy.AllOrdinaryIds(rows));
        Assert.Empty(CompactInstallActionPolicy.SelectIds(
            rows, [ordinary]));
        Assert.Equal([independent.Id], CompactInstallActionPolicy.SelectIds(
            rows, [independent, ordinary]));
        Assert.Equal(independent.Id, DriverUpdateFingerprint.Create(rows));

        var merged = WindowsDriverCandidateMerge.ConfirmedDistinctForReport(
            new DriverSearchReport(rows, [
                new("Windows Update online", "Completed", 2, "success"),
                new("Microsoft Update online", "Completed", 1, "success")
            ]));
        var vetoed = Assert.Single(merged, x => Guid.Parse(x.Id) == id);
        Assert.True(vetoed.IsHidden);
        Assert.False(vetoed.IsOptional);
        Assert.Equal("NVIDIA Display", vetoed.Title);
        Assert.False(QuickUpdatePolicy.IsEligible(vetoed));
        Assert.DoesNotContain(id.ToString("D"),
            QuickUpdatePolicy.AllOrdinaryIds(merged));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void HiddenFlagSurvivesWuaMergeWithoutLosingPnpEvidence(
        bool hiddenFirst)
    {
        var id = Guid.NewGuid();
        var normal = Update(id.ToString("D"));
        var hidden = Update(id.ToString("B"), hardware: Killer,
            source: "Windows configurato", hidden: true);
        var dict = new Dictionary<string, DriverUpdateInfo>(
            StringComparer.OrdinalIgnoreCase);
        var inputs = hiddenFirst
            ? new[] { hidden, normal } : [normal, hidden];

        Assert.Equal(1, WindowsDriverCandidateMerge.Merge(dict, inputs));
        var selected = Assert.Single(dict).Value;
        Assert.True(selected.IsHidden);
        Assert.Equal(Nvidia, selected.HardwareId);
        Assert.Contains(Killer, selected.AdditionalHardwareIds!);
        Assert.False(QuickUpdatePolicy.IsEligible(selected));
        Assert.Empty(QuickUpdatePolicy.AllOrdinaryIds(dict.Values));

        var gpu = DeviceWuaMatchGroups.Create(Device(Nvidia), inputs);
        Assert.Empty(gpu.Ordinary);
        Assert.True(Assert.Single(gpu.ReviewOnly).IsHidden);
        Assert.Empty(gpu.ModelOnlyReview ?? []);
    }

    [Fact]
    public void PerDeviceGatePrefersExplicitHiddenReviewOverOrdinaryTitle()
    {
        var id = Guid.NewGuid();
        var ordinary = Update(id.ToString("N"));
        var hidden = Update(id.ToString("P"),
            source: "Microsoft Update online", hidden: true,
            title: "NVIDIA hidden WUA");
        var approved = QuickUpdatePolicy.AllOrdinaryIds([ordinary, hidden])
            .Select(Guid.Parse).ToHashSet();

        Assert.Empty(approved);
        var perDevice = DeviceWuaPresentationGate.Filter(Device(Nvidia),
            [ordinary, hidden], approvedSourceGuids: approved);
        Assert.Empty(perDevice.Ordinary);
        var review = Assert.Single(perDevice.ReviewOnly);
        Assert.True(review.IsHidden);
        Assert.Equal("NVIDIA hidden WUA", review.Title);
        Assert.Empty(perDevice.ModelOnlyReview);
    }

    [Fact]
    public void HiddenRecordFromUnknownOemCannotVetoValidWuaGuid()
    {
        var id = Guid.NewGuid();
        var normal = Update(id.ToString("D"));
        var foreign = Update(id.ToString("B"),
            source: "Unknown OEM support portal",
            hidden: true, title: "Unverified hidden firmware");
        foreach (var rows in new[]
        {
            new[] { foreign, normal },
            new[] { normal, foreign }
        })
        {
            Assert.Equal([id.ToString("D")],
                QuickUpdatePolicy.AllOrdinaryIds(rows));
            var dict = new Dictionary<string, DriverUpdateInfo>(
                StringComparer.OrdinalIgnoreCase);
            WindowsDriverCandidateMerge.Merge(dict, rows);
            Assert.False(Assert.Single(dict).Value.IsHidden);
            Assert.True(QuickUpdatePolicy.IsEligible(
                Assert.Single(dict).Value));
            Assert.Equal([id.ToString("D")],
                CompactInstallActionPolicy.SelectIds(rows, [normal]));
            Assert.Equal(id.ToString("D"),
                DriverUpdateFingerprint.Create(rows));
        }
    }

    [Fact]
    public void AdvancedOnlyRepresentationWithoutHiddenFlagDoesNotVeto()
    {
        var id = Guid.NewGuid();
        var normal = Update(id.ToString("D"));
        var advanced = Update(id.ToString("B"),
            source: "Microsoft Update online · avanzato",
            advanced: true);
        Assert.Equal([normal.Id],
            QuickUpdatePolicy.AllOrdinaryIds([advanced, normal]));
        var dict = new Dictionary<string, DriverUpdateInfo>(
            StringComparer.OrdinalIgnoreCase);
        WindowsDriverCandidateMerge.Merge(dict, [advanced, normal]);
        Assert.True(QuickUpdatePolicy.IsEligible(
            Assert.Single(dict).Value));
        Assert.False(Assert.Single(dict).Value.IsHidden);
    }

    [Fact]
    public void HiddenAdvancedRepresentationStillVetoesOrdinary()
    {
        var id = Guid.NewGuid();
        var normal = Update(id.ToString("D"));
        var hiddenAdvanced = Update(id.ToString("N"),
            source: "Microsoft Update online · avanzato",
            advanced: true, hidden: true);
        Assert.Empty(QuickUpdatePolicy.AllOrdinaryIds(
            [normal, hiddenAdvanced]));
        var dict = new Dictionary<string, DriverUpdateInfo>(
            StringComparer.OrdinalIgnoreCase);
        WindowsDriverCandidateMerge.Merge(dict,
            [hiddenAdvanced, normal]);
        Assert.True(Assert.Single(dict).Value.IsHidden);
        Assert.False(QuickUpdatePolicy.IsEligible(
            Assert.Single(dict).Value));
    }
}
