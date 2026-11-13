using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;
using TheEasyWayForDrivers.Core.Update;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

/// <summary>
/// #440: known WUA source names cannot attest malformed IPC outcome/count
/// data, even when other genuine driver queries completed successfully.
/// </summary>
public sealed class WuaDriverPassStatusIntegrityTests
{
    private static readonly DateTimeOffset Attempted =
        new(2026, 11, 13, 16, 15, 0, TimeSpan.Zero);

    private static DriverSearchPassStatus Status(
        string source, string outcome, int candidates = 0, int unique = 0) =>
        new(source, outcome, candidates,
            @"PRIVATE_PROXY\SERIAL_PNP_VALUE", unique, Attempted);

    [Theory]
    [InlineData("Completed", 3, 2)]
    [InlineData("Partial", 3, 0)]
    [InlineData("Failed", 0, 0)]
    [InlineData("NetworkError", 0, 0)]
    [InlineData("PolicyRestricted", 0, 0)]
    [InlineData("NotRegistered", 0, 0)]
    public void KnownOutcomesWithPlausibleCountersKeepTheirSourceStatus(
        string outcome, int count, int unique)
    {
        var source = outcome == "NotRegistered"
            ? "Microsoft Update online"
            : "Windows Update online";
        var row = Status(source, outcome, count, unique);

        Assert.True(WindowsDriverCandidateMerge.IsRecognizedPassOutcome(
            row.Outcome));
        Assert.True(WindowsDriverCandidateMerge.IsVerifiablePassStatus(
            row, [row]));
        Assert.False(WindowsDriverCandidateMerge.HasUnverifiablePassStatuses(
            [row]));
    }

    [Theory]
    [InlineData("UnknownOutcome")]
    [InlineData("completed")]
    [InlineData("")]
    [InlineData("COMPLETED")]
    public void UnknownOutcomeNeverAttestsSourceOrAttemptedTimestamp(
        string outcome)
    {
        var row = Status("Windows Update online", outcome);
        var completed = Status("Windows configurato", "Completed", 1, 1);
        var report = new DriverSearchReport([], [completed, row]);

        Assert.False(WindowsDriverCandidateMerge.IsRecognizedPassOutcome(
            row.Outcome));
        Assert.False(WindowsDriverCandidateMerge.IsVerifiablePassStatus(
            row, report.Sources));
        Assert.True(WindowsDriverCandidateMerge.HasUnverifiablePassStatuses(
            report.Sources));
        Assert.True(report.IsPartial);
        Assert.False(report.AllFailed);
        Assert.Equal(["Windows Update online"],
            WuaSourceFailurePolicy.UnavailableSources(report));
        var display = WuaPassStatusPresentation.Create(report);
        Assert.Contains("esito non riconosciuto", display[1]);
        Assert.DoesNotContain("tentata", display[1]);
        Assert.DoesNotContain("PRIVATE_PROXY", string.Join(" ", display));
        Assert.Contains("fonte NON verificata",
            ScanCoverageText.Create(report, [], 0, 0, 0));
    }

    [Theory]
    [InlineData("Partial", -1, 0)]
    [InlineData("Failed", 0, -1)]
    [InlineData("NetworkError", 1, 2)]
    [InlineData("PolicyRestricted", -7, -7)]
    [InlineData("Partial", 0, 1)]
    public void InvalidNonCompletedCountersAreNotVerifiable(
        string outcome, int count, int unique)
    {
        var row = Status("Windows Update online", outcome, count, unique);
        var completed = Status("Windows configurato", "Completed", 1, 1);
        var report = new DriverSearchReport([], [completed, row]);

        Assert.False(WindowsDriverCandidateMerge.IsVerifiablePassStatus(
            row, report.Sources));
        Assert.True(WindowsDriverCandidateMerge.HasUnverifiablePassStatuses(
            report.Sources));
        Assert.True(report.IsPartial);
        Assert.True(WindowsDriverCandidateMerge.IsVerifiablePassStatus(
            completed, report.Sources));
        Assert.Equal(["Windows Update online"],
            WuaSourceFailurePolicy.UnavailableSources(report));

        var presentation = WuaPassStatusPresentation.Create(report);
        Assert.Contains("fonte NON verificata", presentation[1]);
        Assert.DoesNotContain("tentata", presentation[1]);
        Assert.DoesNotContain("PRIVATE_PROXY", string.Join(" ", presentation));
    }

    [Fact]
    public void MalformedPartialCountsCannotAuthenticateOmittedHints()
    {
        var partial = Status("Windows Update online", "Partial", -1, 0);
        var report = new DriverSearchReport(
            [], [partial], PartialDriverHintsOmitted: 1);
        Assert.Equal(0,
            WuaPartialDriverHintCollector.VerifiedOmittedForReport(report));
    }

    [Fact]
    public void ValidPartialCountCanAuthenticateOmittedHints()
    {
        var partial = Status("Windows Update online", "Partial", 2, 0);
        var report = new DriverSearchReport(
            [], [partial], PartialDriverHintsOmitted: 1);
        Assert.Equal(1,
            WuaPartialDriverHintCollector.VerifiedOmittedForReport(report));
    }

    [Fact]
    public void InvalidMicrosoftNotRegisteredStillFailsClosed()
    {
        var invalid = Status("Microsoft Update online", "NotRegistered", 1, 0);
        var wrongName = Status("Windows configurato", "NotRegistered");
        Assert.False(WindowsDriverCandidateMerge.IsVerifiablePassStatus(
            invalid, [invalid]));
        Assert.False(WindowsDriverCandidateMerge.IsVerifiablePassStatus(
            wrongName, [wrongName]));
        Assert.True(WindowsDriverCandidateMerge.HasUnverifiablePassStatuses(
            [invalid]));
        Assert.True(WindowsDriverCandidateMerge.HasUnverifiablePassStatuses(
            [wrongName]));
    }

    [Fact]
    public void DuplicatedCompletedSourceDoesNotAuthenticateEitherRow()
    {
        var first = Status("Windows configurato", "Completed", 1, 1);
        var duplicate = Status("WINDOWS CONFIGURATO", "Completed", 0);
        var report = new DriverSearchReport([], [first, duplicate]);
        Assert.False(WindowsDriverCandidateMerge.IsVerifiablePassStatus(
            first, report.Sources));
        Assert.False(WindowsDriverCandidateMerge.IsVerifiablePassStatus(
            duplicate, report.Sources));
        Assert.True(report.IsPartial);
        Assert.Empty(WindowsDriverCandidateMerge.ConfirmedDistinctForReport(
            report));
    }
}
