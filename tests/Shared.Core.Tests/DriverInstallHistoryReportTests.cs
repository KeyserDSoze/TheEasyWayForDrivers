using TheEasyWayForDrivers.Core.Drivers;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

public sealed class DriverInstallHistoryReportTests
{
    private static DriverInstallHistoryEntry Entry(string outcome, string title = "GPU update", bool reboot = false) =>
        new(new DateTimeOffset(2026, 10, 8, 10, 30, 0, TimeSpan.Zero),
            title, outcome, reboot, "Risultato del servizio");

    [Fact]
    public void Matches_SearchesTitlesAndLimitsByOutcome()
    {
        var entry = Entry("Batch riuscito (servizio)");
        Assert.True(DriverInstallHistoryReport.Matches(entry, "gpu", "succeeded"));
        Assert.False(DriverInstallHistoryReport.Matches(entry, "audio", "succeeded"));
        Assert.False(DriverInstallHistoryReport.Matches(entry, null, "failed"));
    }

    [Fact]
    public void Matches_SeparatesInterruptedAndReboot()
    {
        var entry = Entry("Operazione interrotta (esito driver sconosciuto)", reboot: true);
        Assert.True(DriverInstallHistoryReport.Matches(entry, null, "interrupted"));
        Assert.True(DriverInstallHistoryReport.Matches(entry, null, "reboot"));
        Assert.False(DriverInstallHistoryReport.Matches(entry, null, "succeeded"));
    }

    [Theory]
    [InlineData("=SUM(1,2)")]
    [InlineData("+cmd")]
    [InlineData("-1+2")]
    [InlineData("@formula")]
    [InlineData("  =SUM(1,2)")]
    public void ToCsv_PreventsSpreadsheetFormulaExecution(string unsafeTitle)
    {
        var csv = DriverInstallHistoryReport.ToCsv([
            Entry("Batch riuscito (servizio)", unsafeTitle)
        ]);

        Assert.Contains("\"'" + unsafeTitle + "\"", csv);
    }

    [Fact]
    public void ToCsv_EscapesQuotesAndSeparators()
    {
        var csv = DriverInstallHistoryReport.ToCsv([
            Entry("Batch riuscito (servizio)", "GPU; \"special\"")
        ]);
        Assert.Contains("Data UTC;Driver richiesti;", csv);
        Assert.Contains("\"GPU; \"\"special\"\"\"", csv);
        Assert.Contains("2026-10-08 10:30:00", csv);
    }
}
