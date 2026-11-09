using TheEasyWayForDrivers.Core.Models;
using TheEasyWayForDrivers.Core.Update;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

public sealed class WuaPassStatusPresentationTests
{
    private static DriverSearchReport Report(params DriverSearchPassStatus[] sources) =>
        new([], sources);

    private static DriverSearchPassStatus Pass(
        string name, string outcome, int candidates = 0,
        int unique = 0, DateTimeOffset? attempted = null,
        string detail = "https://private-proxy.internal/device/SERIAL") =>
        new(name, outcome, candidates, detail, unique, attempted);

    [Fact]
    public void NullOrEmptyReportNeverClaimsZeroAvailableDrivers()
    {
        var missing = Assert.Single(WuaPassStatusPresentation.Create(null));
        Assert.Contains("non interrogato", missing);
        Assert.DoesNotContain("0 candidati", missing);

        var empty = Assert.Single(
            WuaPassStatusPresentation.Create(Report()));
        Assert.Contains("nessuna fonte interrogata", empty);
        Assert.Contains("sconosciuta", empty);
    }

    [Fact]
    public void CompletedPassShowsOnlyVerifiedCountsAndUtcStart()
    {
        var report = Report(Pass(
            "Windows configurato", "Completed", 7, 2,
            new DateTimeOffset(2026, 11, 9, 11, 21, 31, TimeSpan.FromHours(2))));
        var text = Assert.Single(WuaPassStatusPresentation.Create(report));

        Assert.Contains("Windows configurato: ricerca completata", text);
        Assert.Contains("7 risultati restituiti", text);
        Assert.Contains("2 ID nuovi", text);
        Assert.Contains("2026-11-09 09:21:31 UTC", text);
        Assert.DoesNotContain("private-proxy", text);
        Assert.DoesNotContain("SERIAL", text);
    }

    [Theory]
    [InlineData("Partial", "PARZIALE")]
    [InlineData("PolicyRestricted", "criteri Windows")]
    [InlineData("NetworkError", "rete/proxy/server")]
    [InlineData("Failed", "NON RIUSCITA")]
    [InlineData("Unexpected", "esito non riconosciuto")]
    public void UnverifiedPassesKeepDistinctOutcomeAndNeverClaimZero(
        string outcome, string expected)
    {
        var report = Report(Pass("Windows Update online", outcome, 23, 0));
        var text = Assert.Single(WuaPassStatusPresentation.Create(report));
        Assert.Contains(expected, text);
        Assert.Contains("non", text.ToLowerInvariant());
        Assert.DoesNotContain("23 risultati restituiti", text);
        Assert.DoesNotContain("0 ID nuovi", text);
        Assert.DoesNotContain("private-proxy", text);
        Assert.DoesNotContain("SERIAL", text);
    }

    [Fact]
    public void MicrosoftUpdateNotRegisteredIsNotAQueryFailureOrDate()
    {
        var report = Report(
            Pass("Windows configurato", "Completed", 0),
            Pass("Microsoft Update online", "NotRegistered", 0, 0,
                new DateTimeOffset(2026, 11, 9, 10, 0, 0, TimeSpan.Zero)));
        var text = WuaPassStatusPresentation.Create(report);

        Assert.False(report.IsPartial);
        Assert.Contains("0 risultati restituiti", text[0]);
        Assert.Contains("non registrato", text[1]);
        Assert.Contains("NON interrogato", text[1]);
        Assert.DoesNotContain("tentata", text[1]);
        Assert.DoesNotContain("NON RIUSCITA", text[1]);
        Assert.DoesNotContain("private-proxy", string.Join(" ", text));
    }

    [Fact]
    public void PartialAndOtherFailedPassesStayVisibleBesideCompleted()
    {
        var report = Report(
            Pass("Windows configurato", "Completed", 2, 2),
            Pass("Windows Update online", "Partial", 9),
            Pass("Windows configurato · avanzato", "PolicyRestricted"),
            Pass("Windows Update online · avanzato", "NetworkError"),
            Pass("Microsoft Update online", "Failed"),
            WindowsDriverSearchPlan.NotRegisteredStatus());

        var text = WuaPassStatusPresentation.Create(report);
        Assert.True(report.IsPartial);
        Assert.Equal(6, text.Count);
        Assert.Contains("2 risultati restituiti", text[0]);
        Assert.Contains("PARZIALE", text[1]);
        Assert.Contains("criteri Windows", text[2]);
        Assert.Contains("rete/proxy/server", text[3]);
        Assert.Contains("NON RIUSCITA", text[4]);
        Assert.Contains("NON interrogato", text[5]);
    }

    [Fact]
    public void UnknownSourceAndUntrustedDetailAreNeverEchoed()
    {
        var report = Report(
            Pass(@"PRIVATE_SERVER\SERIAL-DEVICE", "Failed",
                detail: "C:\\Users\\Someone\\secret.txt; https://private-proxy"),
            Pass("Windows configurato", "Completed", 1, 1,
                detail: "Private local DeviceId = PCI\\VEN_10DE&DEV_1234"));

        var result = string.Join(" ", WuaPassStatusPresentation.Create(report));
        Assert.Contains("Fonte Windows non riconosciuta", result);
        Assert.Contains("1 risultati restituiti", result);
        Assert.DoesNotContain("PRIVATE_SERVER", result);
        Assert.DoesNotContain("SERIAL", result);
        Assert.DoesNotContain("secret", result);
        Assert.DoesNotContain("private-proxy", result);
        Assert.DoesNotContain("VEN_10DE", result);
    }

    [Fact]
    public void DisplayIsBoundedToTwelveSourcesWithoutLeakingTheRest()
    {
        var report = Report(Enumerable.Range(0, 13)
            .Select(i => Pass("Windows configurato",
                i == 12 ? "Failed" : "Completed", i == 12 ? 10000 : 1))
            .ToArray());

        var lines = WuaPassStatusPresentation.Create(report);
        Assert.Equal(WuaPassStatusPresentation.MaximumVisibleSources + 1,
            lines.Count);
        Assert.Contains("1 altre fonti non mostrate", lines[^1]);
        Assert.DoesNotContain("10000", string.Join(" ", lines));
    }
}
