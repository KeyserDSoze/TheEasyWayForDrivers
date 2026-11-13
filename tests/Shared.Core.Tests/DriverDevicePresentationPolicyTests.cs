using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

/// <summary>
/// #449: a device with unknown signed-driver metadata can be classified
/// correctly in the aggregate report while an old Desktop row marked it
/// green OK and dropped it from the review count. Core and Desktop
/// share this status policy; no WUA install eligibility is inferred.
/// </summary>
public sealed class DriverDevicePresentationPolicyTests
{
    private static DriverInfo Device(bool hasDriver, bool metadataVerified,
        uint problemCode = 0, bool problemVerified = true) =>
        new(@"PCI\VEN_10DE&DEV_28A0\PRIVATE_INSTANCE",
            "Microsoft Basic Display Adapter", "Microsoft",
            "Display", "Microsoft", null, null, null,
            false, hasDriver, problemCode,
            [@"PCI\VEN_10DE&DEV_28A0"], [],
            DriverMetadataQuerySucceeded: metadataVerified,
            PnpProblemCodeVerified: problemVerified);

    [Fact]
    public void FailedSignedDriverQueryRequiresReviewWithoutClaimingMissingDriver()
    {
        var device = Device(hasDriver: false, metadataVerified: false);
        var summary = DeviceAttentionSummary.Create([device]);

        Assert.False(device.NeedsAttention); // no confirmed PnP problem
        Assert.True(device.IsDriverUnverified);
        Assert.False(device.IsDriverMissing);
        Assert.Equal("Da verificare", device.Status);
        Assert.Equal(1, summary.Unverified);
        Assert.Equal(0, summary.Problems);
        Assert.Equal(0, summary.Healthy);

        Assert.True(DriverDevicePresentationPolicy.NeedsAttention(device, false));
        Assert.Equal("Da verificare",
            DriverDevicePresentationPolicy.Status(device, false, false));
        Assert.Equal("Da verificare",
            DriverDevicePresentationPolicy.Status(device, true, true));
    }

    [Fact]
    public void VerifiedWindowsPnpHealthyWithInstalledDriverStaysGreen()
    {
        var device = Device(hasDriver: true, metadataVerified: true);

        Assert.False(DriverDevicePresentationPolicy.NeedsAttention(device, false));
        Assert.Equal("OK",
            DriverDevicePresentationPolicy.Status(device, false, false));
        Assert.Equal(1, DeviceAttentionSummary.Create([device]).Healthy);
    }

    [Fact]
    public void VerifiedPnpCodeZeroWithoutSignedMetadataIsStillReview()
    {
        var device = Device(hasDriver: false, metadataVerified: true);

        Assert.True(device.NeedsAttention);
        Assert.True(device.IsDriverUnverified);
        Assert.True(DriverDevicePresentationPolicy.NeedsAttention(device, false));
        Assert.Equal("Da verificare",
            DriverDevicePresentationPolicy.Status(device, false, false));
        Assert.Equal(1, DeviceAttentionSummary.Create([device]).Unverified);
        Assert.False(device.IsDriverMissing);
    }

    [Theory]
    [InlineData(10U, "Errore Windows")]
    [InlineData(28U, "Driver mancante")]
    public void ConfirmedPnpProblemsKeepWindowsErrorPriority(
        uint code, string expected)
    {
        var device = Device(hasDriver: true, metadataVerified: true,
            problemCode: code);

        Assert.True(device.HasWindowsProblem);
        Assert.True(DriverDevicePresentationPolicy.NeedsAttention(device, true));
        Assert.Equal(expected,
            DriverDevicePresentationPolicy.Status(device, true, false));
        Assert.Equal(expected, device.Status);
    }

    [Theory]
    [InlineData(0U)]
    [InlineData(28U)]
    public void UnknownPnpCodeNeverBecomesConfirmedMissingOrHealthy(uint code)
    {
        var device = Device(hasDriver: true, metadataVerified: true,
            problemCode: code, problemVerified: false);

        Assert.False(device.HasWindowsProblem);
        Assert.False(device.IsDriverMissing);
        Assert.True(device.IsDriverUnverified);
        Assert.True(DriverDevicePresentationPolicy.NeedsAttention(device, false));
        Assert.Equal("Da verificare",
            DriverDevicePresentationPolicy.Status(device, false, false));
        Assert.Equal(1, DeviceAttentionSummary.Create([device]).Unverified);
    }

    [Fact]
    public void OrdinaryWuaUpdateOnHealthyDeviceIsVisibleAndNeedsAttention()
    {
        var device = Device(hasDriver: true, metadataVerified: true);

        Assert.True(DriverDevicePresentationPolicy.NeedsAttention(device, true));
        Assert.Equal("Aggiornamento disponibile",
            DriverDevicePresentationPolicy.Status(device, true, false));
        Assert.Equal("Aggiornamento disponibile",
            DriverDevicePresentationPolicy.Status(device, true, true));
        // WUA does not rewrite the independent PnP health inventory.
        Assert.Equal(1, DeviceAttentionSummary.Create([device]).Healthy);
    }

    [Fact]
    public void ReviewOnlyWuaHintIsInformationalAndNeverBecomesOrdinary()
    {
        var device = Device(hasDriver: true, metadataVerified: true);
        Assert.False(DriverDevicePresentationPolicy.NeedsAttention(device, false));
        Assert.Equal("WUA da verificare",
            DriverDevicePresentationPolicy.Status(device, false, true));
    }

    [Fact]
    public void SummaryAndRowAgreeOnEachUnverifiedReason()
    {
        var rows = new[]
        {
            Device(false, false),
            Device(false, true),
            Device(true, false, problemVerified: false),
            Device(true, true, 28),
            Device(true, true, 10),
            Device(true, true)
        };
        var summary = DeviceAttentionSummary.Create(rows);
        var fromRows = rows.Count(row =>
            DriverDevicePresentationPolicy.NeedsAttention(row, false));

        Assert.Equal(3, summary.Unverified);
        Assert.Equal(2, summary.Problems);
        Assert.Equal(1, summary.Healthy);
        Assert.Equal(summary.NeedsReview, fromRows);
        Assert.Equal(5, fromRows);
    }
}
