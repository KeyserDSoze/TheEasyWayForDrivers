using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

public sealed class DeviceAttentionSummaryTests
{
    private static DriverInfo Device(uint code, bool hasDriver) =>
        new(@"PCI\VEN_TEST", "Test", null, "System", null, null, null, null,
            false, hasDriver, code, [], []);

    [Fact]
    public void Create_SeparatesMissingErrorsAndUnverified()
    {
        var summary = DeviceAttentionSummary.Create([
            Device(28, false), Device(28, true),
            Device(10, true), Device(43, false),
            Device(0, false), Device(0, true)
        ]);

        Assert.Equal(2, summary.Missing);
        Assert.Equal(2, summary.WindowsErrors);
        Assert.Equal(1, summary.Unverified);
        Assert.Equal(1, summary.Healthy);
        Assert.Equal(4, summary.Problems);
        Assert.Equal(5, summary.NeedsReview);
    }

    [Fact]
    public void Create_HandlesEmptyInventory()
    {
        var summary = DeviceAttentionSummary.Create([]);
        Assert.Equal(0, summary.NeedsReview);
        Assert.Equal(0, summary.Healthy);
    }
}
