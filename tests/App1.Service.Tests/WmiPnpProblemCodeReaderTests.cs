using System.Management;
using System.Runtime.InteropServices;
using System.Security;
using TheEasyWayForDrivers.ServiceApp.Services;
using Xunit;

namespace TheEasyWayForDrivers.ServiceApp.Tests;

public sealed class WmiPnpProblemCodeReaderTests
{
    [Theory]
    [InlineData((uint)0, 0u)]
    [InlineData((uint)28, 28u)]
    [InlineData((uint)43, 43u)]
    [InlineData("28", 28u)]
    [InlineData("0000000043", 43u)]
    [InlineData(28, 28u)]
    [InlineData(0L, 0u)]
    public void KnownWmiProblemCodesRemainVerified(object raw, uint expected)
    {
        var report = WmiPnpProblemCodeReader.Read(() => raw);
        Assert.True(report.Verified);
        Assert.Equal(expected, report.Code);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("-1")]
    [InlineData("4294967296")]
    [InlineData(" PRIVATE_SERIAL")]
    [InlineData("28 EXTRA")]
    [InlineData(true)]
    [InlineData(-1)]
    [InlineData(-1L)]
    [InlineData(4294967296L)]
    public void BadWmiVariantPreservesOtherDevicesWithoutClaimingHealthy(
        object? invalid)
    {
        var report = WmiPnpProblemCodeReader.Read(() => invalid);
        Assert.False(report.Verified);
        Assert.Equal(0u, report.Code);

        var later = WmiPnpProblemCodeReader.Read(() => (uint)28);
        Assert.True(later.Verified);
        Assert.Equal(28u, later.Code);
    }

    [Theory]
    [InlineData("wmi")]
    [InlineData("com")]
    [InlineData("access")]
    [InlineData("security")]
    public void OneOptionalWmiGetterFailureIsIsolated(string failure)
    {
        var first = WmiPnpProblemCodeReader.Read(() =>
            throw failure switch
            {
                "wmi" => new ManagementException("PRIVATE_SERIAL"),
                "com" => new COMException("PRIVATE_INSTANCE"),
                "access" => new UnauthorizedAccessException("PRIVATE_PATH"),
                _ => new SecurityException("PRIVATE_USER")
            });
        Assert.False(first.Verified);
        Assert.Equal(0u, first.Code);
        Assert.Equal(10u, WmiPnpProblemCodeReader
            .Read(() => (uint)10).Code);
    }

    [Fact]
    public void CancellationAndProgrammingErrorsStillPropagate()
    {
        Assert.Throws<OperationCanceledException>(() =>
            WmiPnpProblemCodeReader.Read(() =>
                throw new OperationCanceledException("cancelled")));
        Assert.Throws<InvalidOperationException>(() =>
            WmiPnpProblemCodeReader.Read(() =>
                throw new InvalidOperationException("implementation bug")));
    }
}
