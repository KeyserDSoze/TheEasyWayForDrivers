using TheEasyWayForDrivers.ServiceApp.Services;
using Xunit;

namespace TheEasyWayForDrivers.ServiceApp.Tests;

/// <summary>
/// No real WMI/driver changes: read-only synthetic signed-driver rows.
/// </summary>
public sealed class WmiSignedDriverFieldReaderTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("PRIVATE_PC\\C:\\Users\\secret")]
    [InlineData("20261004153000.000000+***")]
    [InlineData("202610041530**.000000+060")]
    [InlineData("20261304153000.000000+000")]
    [InlineData("20260230153000.000000+000")]
    [InlineData("20261004153000.000000+0G0")]
    [InlineData("20261004153000.000000+00")]
    [InlineData("20261004153000.000000+000 EXTRA")]
    public void MissingMalformedOrPartialDateDoesNotStopDriverMetadata(
        string? rawDate)
    {
        var record = WmiSignedDriverFieldReader.Create(
            "NVIDIA", "32.0.15.7010", rawDate, "oem42.inf", true);

        Assert.Null(record.Date);
        Assert.Equal("NVIDIA", record.Provider);
        Assert.Equal("32.0.15.7010", record.Version);
        Assert.Equal("oem42.inf", record.InfName);
        Assert.True(record.IsSigned);
    }

    [Theory]
    [InlineData("20261004153000.000000+000")]
    [InlineData("20261004153000.000000+060")]
    [InlineData("20261109113000.000000+000")]
    public void ValidDmtfDateRemainsAvailableWithInstalledDriverFields(
        string rawDate)
    {
        var record = WmiSignedDriverFieldReader.Create(
            "Intel", "23.160.0.4", rawDate, "netwtw.inf", true);

        Assert.NotNull(record.Date);
        Assert.Equal(2026, record.Date.Value.Year);
        Assert.Equal("Intel", record.Provider);
        Assert.Equal("23.160.0.4", record.Version);
        Assert.Equal("netwtw.inf", record.InfName);
        Assert.True(record.IsSigned);
    }

    [Fact]
    public void OversizedUntrustedWmiValueIsRejectedBeforeDateParsing()
    {
        var text = new string('9', 1_000_000);
        Assert.Null(WmiSignedDriverFieldReader.ParseOptionalDate(text));
        Assert.Null(WmiSignedDriverFieldReader.ParseOptionalDate(
            "20261004153000.000000+000\\PRIVATE_INSTANCE"));
    }

    [Fact]
    public void IndividualMalformedDateDoesNotInvalidateOtherDriverRows()
    {
        var raw = new[]
        {
            ("NVIDIA", "31.0.15.7000", "PRIVATE_INVALID_DMTF", "nv.inf"),
            ("Intel/Killer", "23.1.0.1", "20261004153000.000000+000",
                "net.inf"),
            ("Realtek", "6.0.9600.2", (string?)null, "rtk.inf")
        };
        var rows = raw.Select(x => WmiSignedDriverFieldReader.Create(
            x.Item1, x.Item2, x.Item3, x.Item4, true)).ToArray();
        Assert.Equal(3, rows.Length);
        Assert.Null(rows[0].Date);
        Assert.NotNull(rows[1].Date);
        Assert.Null(rows[2].Date);
        Assert.Equal(["31.0.15.7000", "23.1.0.1", "6.0.9600.2"],
            rows.Select(x => x.Version).ToArray());
        Assert.All(rows, x => Assert.True(x.IsSigned));
    }
}
