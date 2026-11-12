using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;
using TheEasyWayForDrivers.Core.Update;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

/// <summary>
/// #421: Type=Driver WUA partial titles never become trusted merely because
/// a legacy IPC snapshot contains them. These are NEVER install offers.
/// </summary>
public sealed class WuaPartialDriverProvenanceTests
{
    private const string Configured = "Windows configurato";
    private const string Online = "Windows Update online";
    private const string IdA = "00000000-0000-4000-8000-000000000421";
    private const string IdB = "00000000-0000-4000-8000-000000000422";
    private const string IdC = "00000000-0000-4000-8000-000000000423";

    private static DriverSearchPassStatus Pass(string source,
        string outcome = "Partial", int candidates = 2,
        int unique = 0) =>
        new(source, outcome, candidates, "PRIVATE_COM_DETAIL", unique);

    private static WuaPartialDriverHint Hint(
        string id = IdA, string source = Configured,
        string title = "NVIDIA - Display") =>
        new(id, title, source);

    private static DriverSearchReport Report(
        IReadOnlyList<WuaPartialDriverHint> hints,
        params DriverSearchPassStatus[] sources) =>
        new([], sources, PartialDriverHints: hints);

    private static DriverUpdateInfo OnlineOffer(string id) =>
        new(id, "Intel - Net", null, "Net", "Intel", null, null,
            false, "Intel", null, @"PCI\VEN_8086&DEV_51F1",
            null, SearchSource: Online);

    [Theory]
    [InlineData("B")]
    [InlineData("N")]
    [InlineData("P")]
    public void PartialGuidSpellingsCanonicalizeToOneIdentity(string format)
    {
        var parsed = Guid.Parse(IdA);
        var hint = WuaPartialDriverHintCollector.TryCreate(
            parsed.ToString(format), "NVIDIA - Display", Configured);
        Assert.Equal(IdA, Assert.IsType<WuaPartialDriverHint>(hint).UpdateId);

        var staged = new Dictionary<string, WuaPartialDriverHint>();
        Assert.Equal(1, WuaPartialDriverHintCollector.MergeStaged(staged,
            [Hint(IdA), Hint(parsed.ToString(format))]));
        Assert.Single(staged);

        var fair = WuaPartialDriverHintCollector.SelectFair(
            [Hint(parsed.ToString(format)), Hint(IdA), Hint(IdB)]);
        Assert.Equal(2, fair.Hints.Count);
        Assert.Contains(fair.Hints, x => x.UpdateId == IdA);
        Assert.Equal(0, fair.Omitted);
        Assert.Empty(WuaPartialDriverHintCollector.SelectFair(
            [Hint(parsed.ToString(format))],
            [parsed.ToString("B")]).Hints);
    }

    [Fact]
    public void LegacyMergeCountsCanonicalDuplicatesAndOmissionsOnlyOnce()
    {
        var destination = new Dictionary<string, WuaPartialDriverHint>
        {
            [Guid.Parse(IdA).ToString("B")] = Hint(IdA)
        };
        var omitted = new HashSet<string>
        {
            Guid.Parse(IdB).ToString("P")
        };
        var added = WuaPartialDriverHintCollector.Merge(destination,
        [
            Hint(Guid.Parse(IdA).ToString("N")),
            Hint(IdB),
            Hint(IdC),
            Hint(Guid.Parse(IdC).ToString("B")),
            Hint("PRIVATE_ID", title: "NVIDIA - Display"),
            Hint(IdB, title: @"C:\PRIVATE\driver.inf")
        ], out var over, omitted);

        Assert.Equal(1, added);
        Assert.Equal(0, over);
        Assert.Equal(2, destination.Count);
        Assert.Equal(IdC, destination[IdC].UpdateId);
        Assert.DoesNotContain(destination.Values,
            hint => hint.Title.Contains("PRIVATE"));
    }

    [Fact]
    public void TwoValidPartialSourcesRetainOnlyTheirOwnSafeTitles()
    {
        var report = Report(
            [Hint(), Hint(IdB, Online, "Intel - Net")],
            Pass(Configured, candidates: 1),
            Pass(Online, candidates: 1));
        var verified = WuaPartialDriverHintCollector.ConfirmedForReport(report);
        Assert.Equal(2, verified.Count);
        Assert.All(verified, hint => Assert.False(hint.IsInstallable));
        Assert.False(verified[0].IsVerified);
        Assert.Equal(IdA, verified[0].UpdateId);

        var copy = ScanCoverageText.Create(report, [], 0, 0, 0);
        Assert.Contains("2 visualizzati, 0 oltre limite", copy);
        Assert.DoesNotContain("PRIVATE_COM_DETAIL", copy);
        Assert.DoesNotContain("NVIDIA - Display", copy);
        Assert.DoesNotContain(IdA, copy);
    }

    [Theory]
    [InlineData("Completed")]
    [InlineData("Failed")]
    [InlineData("NotRegistered")]
    [InlineData("Other")]
    public void NonPartialSourceCannotAuthenticatePartialHint(string outcome)
    {
        var report = Report([Hint()],
            Pass(Configured, outcome, candidates: 1));
        Assert.Empty(WuaPartialDriverHintCollector.ConfirmedForReport(report));
        Assert.Contains("0 visualizzati, 0 oltre limite",
            ScanCoverageText.Create(report, [], 0, 0, 0));
    }

    [Theory]
    [InlineData("Partial")]
    [InlineData("Completed")]
    public void DuplicateAndConflictingPassStatusesFailClosed(string otherOutcome)
    {
        var report = Report([Hint()],
            Pass(Configured, candidates: 1),
            Pass(Configured, otherOutcome, candidates: 1));
        Assert.Empty(WuaPartialDriverHintCollector.ConfirmedForReport(report));
        Assert.Equal(0,
            WuaPartialDriverHintCollector.VerifiedOmittedForReport(report));
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(1, -1)]
    [InlineData(1, 2)]
    [InlineData(0, 0)]
    public void ImpossibleSourceCountersCannotValidateHints(
        int candidates, int unique)
    {
        var report = Report([Hint()], Pass(Configured,
            candidates: candidates, unique: unique));
        Assert.Empty(WuaPartialDriverHintCollector.ConfirmedForReport(report));
    }

    [Fact]
    public void SpoofedSourceAndPrivateTitleAreNotDisplayed()
    {
        var report = Report(
            [Hint(), Hint(IdB, "PRIVATE_COMPANION_SOURCE"),
             Hint(IdC, Configured, @"Intel C:\Users\PRIVATE\driver.inf")],
            Pass(Configured, candidates: 3),
            Pass("PRIVATE_COMPANION_SOURCE", candidates: 2));
        var one = Assert.Single(WuaPartialDriverHintCollector
            .ConfirmedForReport(report));
        Assert.Equal(IdA, one.UpdateId);
        var copy = ScanCoverageText.Create(report, [], 0, 0, 0);
        Assert.Contains("1 visualizzati", copy);
        Assert.DoesNotContain("PRIVATE_", copy);
        Assert.Equal(0,
            WuaPartialDriverHintCollector.VerifiedOmittedForReport(report));
    }

    [Theory]
    [InlineData("B")]
    [InlineData("N")]
    [InlineData("P")]
    public void CompletedDriverOfferWinsOverPartialGuidInOtherSyntax(string fmt)
    {
        var completed = OnlineOffer(Guid.Parse(IdA).ToString(fmt));
        var report = new DriverSearchReport([completed],
            [Pass(Configured, candidates: 1),
             Pass(Online, "Completed", candidates: 1, unique: 1)],
            PartialDriverHints: [Hint(IdA)]);
        Assert.Empty(WuaPartialDriverHintCollector.ConfirmedForReport(report));
        Assert.Empty(QuickUpdatePolicy.AllOrdinaryIds(
            WuaPartialDriverHintCollector.ConfirmedForReport(report)
                .Select(_ => completed)));
        Assert.Contains("0 visualizzati, 0 oltre limite",
            ScanCoverageText.Create(report, [], 0, 0, 0));
    }

    [Fact]
    public void IndependentValidPartialPassSurvivesFailedOtherSource()
    {
        var report = Report(
            [Hint(IdA, Configured), Hint(IdB, Online)],
            Pass(Configured, candidates: 1),
            Pass(Online, "Failed", candidates: 0));
        var only = Assert.Single(WuaPartialDriverHintCollector
            .ConfirmedForReport(report));
        Assert.Equal(IdA, only.UpdateId);
        Assert.False(only.IsInstallable);
    }

    [Fact]
    public void UnverifiableOmittedCountIsNotClaimedInCopiedCoverage()
    {
        var report = new DriverSearchReport([],
            [Pass(Configured, candidates: 1),
             Pass(Online, "Failed", candidates: 0)],
            PartialDriverHints: [Hint(IdA), Hint(IdB, Online)],
            PartialDriverHintsOmitted: 300);
        Assert.Equal(0,
            WuaPartialDriverHintCollector.VerifiedOmittedForReport(report));
        var copy = ScanCoverageText.Create(report, [], 0, 0, 0);
        Assert.Contains("1 visualizzati, 0 oltre limite", copy);
        Assert.DoesNotContain("300 oltre limite", copy);
    }
}
