using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;
using TheEasyWayForDrivers.Core.Update;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

/// <summary>
/// #492: Type=Software remains non-installable, and old IPC records may
/// not bypass source/title verification through public merge or fairness.
/// </summary>
public sealed class WuaSoftwareIpcBoundaryTests
{
    private const string Configured = "Windows configurato · componenti software";
    private const string Online = "Windows Update online · componenti software";
    private const string Microsoft = "Microsoft Update online · componenti software";
    private const string IdA = "00000000-0000-4000-8000-000000000492";
    private const string IdB = "00000000-0000-4000-8000-000000000493";

    private static WuaDriverRelatedSoftware Hint(
        string id = IdA,
        string title = "Intel PROSet/Wireless WiFi Software extension",
        string source = Online) => new(id, title, source);

    [Theory]
    [InlineData("Intel Wireless Driver SECRET_LOGIN_123")]
    [InlineData("NVIDIA Platform Controller ORDER_29341")]
    [InlineData("Intel Wireless Extension SERIAL_98765")]
    [InlineData("Realtek Audio Driver FUNC_01")]
    [InlineData("Intel Wireless Extension MI_02")]
    public void RawPrivateMarkersAreRejectedBeforeTheyCanBeShown(
        string title)
    {
        Assert.Null(WuaDriverRelatedSoftwareClassifier.TryCreate(
            IdA, title, Online));
    }

    [Theory]
    [InlineData("Intel PROSet/Wireless WiFi Software extension")]
    [InlineData("Realtek Áudio Driver - 1.0")]
    [InlineData("Killer Networking Software")]
    [InlineData("NVIDIA Platform Controller")]
    public void ValidCommercialTitlesRemainInformational(string title)
    {
        var item = Assert.IsType<WuaDriverRelatedSoftware>(
            WuaDriverRelatedSoftwareClassifier.TryCreate(
                IdA, title, Online));
        Assert.Equal(title, item.Title);
        Assert.False(item.IsDriverUpdate);
        Assert.False(item.IsInstallable);
    }

    [Fact]
    public void MergeDoesNotAllowPrivateSameGuidToHideLaterValidWuaTitle()
    {
        var guid = Guid.Parse(IdA);
        var corrupted = Hint(guid.ToString("B"),
            "Intel Wireless Driver SECRET_TOKEN_123");
        var forged = Hint(guid.ToString("N"), source: "PRIVATE_OEM");
        var valid = Hint(guid.ToString("P"));
        var another = Hint(IdB, "NVIDIA Platform Controller", Microsoft);
        var destination = new Dictionary<string, WuaDriverRelatedSoftware>();

        var count = WuaDriverRelatedSoftwareClassifier.Merge(
            destination, [null!, corrupted, forged, valid, another],
            out var omitted);

        Assert.Equal(2, count);
        Assert.Equal(0, omitted);
        Assert.Equal(IdA, destination[IdA].UpdateId);
        Assert.Equal(valid.Title, destination[IdA].Title);
        Assert.Equal(Online, destination[IdA].SearchSource);
        Assert.Equal(Microsoft, destination[IdB].SearchSource);
        Assert.All(destination.Values, item =>
        {
            Assert.False(item.IsDriverUpdate);
            Assert.False(item.IsInstallable);
        });
    }

    [Fact]
    public void FairSelectionValidatesBeforeDedupAndPreservesLateVendors()
    {
        var guid = Guid.Parse(IdA);
        var privateFirst = Hint(guid.ToString("B"),
            "Intel Wireless Driver ORDER_4588");
        var validSameGuid = Hint(guid.ToString("N"));
        var invalidSource = Hint("00000000-0000-4000-8000-000000000494",
            source: "PRIVATE_OEM_SERVER");
        var manyIntel = Enumerable.Range(1, 90)
            .Select(i => Hint($"00000000-0000-4000-8000-{i:X12}"))
            .ToArray();
        var nvidia = Hint(IdB, "NVIDIA Platform Controllers", Microsoft);
        var killer = Hint("00000000-0000-4000-8000-000000000495",
            "Killer Networking Software", Configured);

        var selection = WuaDriverRelatedSoftwareClassifier.SelectFair(
            [null!, privateFirst, invalidSource,
             .. manyIntel, validSameGuid, nvidia, killer]);

        Assert.Equal(32, selection.Hints.Count);
        Assert.Equal(93 - 32, selection.Omitted);
        Assert.Contains(selection.Hints, h => h.UpdateId == IdA);
        Assert.Contains(selection.Hints, h => h.UpdateId == IdB);
        Assert.Contains(selection.Hints, h => h.UpdateId == killer.UpdateId);
        Assert.DoesNotContain(selection.Hints, h =>
            h.Title.Contains("ORDER_", StringComparison.Ordinal) ||
            h.SearchSource.Contains("PRIVATE_", StringComparison.Ordinal));
        Assert.All(selection.Hints, item =>
        {
            Assert.False(item.IsInstallable);
            Assert.True(Guid.TryParseExact(item.UpdateId, "D", out _));
        });
    }

    [Fact]
    public void CompletedReportIgnoresNullAndUnsafeLegacySoftwareHints()
    {
        var malformed = Hint(IdA,
            "Intel Wireless Driver SECRET_LOCAL_ORDER");
        var valid = Hint("{" + IdA + "}");
        var second = Hint(IdB, "NVIDIA Platform Controllers", Microsoft);
        var report = new DriverSearchReport(
            [],
            [new DriverSearchPassStatus("Windows Update online",
                "Completed", 0, "")],
            RelatedSoftware: [null!, malformed, valid, second],
            RelatedSoftwareSources:
            [
                new DriverSearchPassStatus(Online, "Completed", 2, ""),
                new DriverSearchPassStatus(Microsoft, "Completed", 1, "")
            ]);

        var verified = WuaDriverRelatedSoftwareClassifier
            .ConfirmedForReport(report);
        Assert.Equal(2, verified.Count);
        Assert.Contains(verified, h => h.UpdateId == IdA);
        Assert.Contains(verified, h => h.UpdateId == IdB);
        Assert.All(verified, h => Assert.False(h.IsInstallable));
        Assert.Empty(report.Updates);

        var rows = ReferenceDriverCoverage.Create([], report);
        Assert.Equal(1, rows[2].SoftwareTitleHints);
        Assert.Equal(1, rows[7].SoftwareTitleHints);
        Assert.All(rows, row => Assert.Equal(0, row.WindowsDriverCandidates));

        var copied = ScanCoverageText.Create(report, [], 0, 0, 0);
        Assert.DoesNotContain("SECRET_LOCAL_ORDER", copied);
        Assert.DoesNotContain(IdA, copied);
        Assert.DoesNotContain(IdB, copied);
    }

    [Fact]
    public void FakeSoftwareSourceDoesNotConsumeOmissionBudget()
    {
        var full = Enumerable.Range(1, 32)
            .Select(i => Hint($"00000000-0000-4000-8000-{i:X12}"))
            .ToArray();
        var forged = Hint(IdA, "NVIDIA Platform Controllers",
            "PRIVATE_OEM");
        var legitimate = Hint(IdA, "NVIDIA Platform Controllers", Online);
        var destination = new Dictionary<string, WuaDriverRelatedSoftware>();
        Assert.Equal(32, WuaDriverRelatedSoftwareClassifier.Merge(
            destination, full, out var initialOmitted));
        Assert.Equal(0, initialOmitted);

        var omittedIds = new HashSet<string>();
        Assert.Equal(0, WuaDriverRelatedSoftwareClassifier.Merge(
            destination, [forged, legitimate], out var omitted,
            omittedIds));
        Assert.Equal(1, omitted);
        Assert.Single(omittedIds);
        Assert.Contains(IdA, omittedIds);
    }
}
