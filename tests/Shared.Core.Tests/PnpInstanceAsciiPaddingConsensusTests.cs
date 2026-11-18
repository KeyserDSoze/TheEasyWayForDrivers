using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

/// <summary>
/// #545: a legacy WMI/IPC DeviceId with ASCII padding must not split
/// the same PnP instance into multiple trusted identities.
/// </summary>
public sealed class PnpInstanceAsciiPaddingConsensusTests
{
    private const string Nvidia = @"PCI\VEN_10DE&DEV_28A0";
    private const string Realtek = @"HDAUDIO\FUNC_01&VEN_10EC&DEV_0295";
    private const string Killer = @"PCI\VEN_8086&DEV_51F1&SUBSYS_16721A56";

    private static DriverInfo Device(string instance, string hardware,
        string cls = "Unknown") =>
        new(instance, "Microsoft Generic Device", "Microsoft", cls,
            "Microsoft", "31.0.0.1", null, "oem42.inf", true, true, 0,
            [hardware], []);

    private static DriverUpdateInfo Offer(int number, string id) =>
        new($"00000000-0000-4000-8000-{number:D12}",
            "Windows driver", null, "Display", "Microsoft",
            null, null, false, "Microsoft", null, id, null,
            SearchSource: "Windows Update online");

    private static DriverSearchReport Report(params DriverUpdateInfo[] updates) =>
        new(updates, [
            new DriverSearchPassStatus("Windows Update online", "Completed",
                updates.Length, "")
        ]);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PaddedConflictingRowsAreExcludedInBothOrders(bool reverse)
    {
        var gpu = Device(" PRIVATE_SAME ", Nvidia, "Display");
        var codec = Device("private_same", Realtek, "MEDIA");
        var wifi = Device("PRIVATE_WIFI", Killer, "Net");
        DriverInfo[] devices = reverse
            ? [codec, gpu, wifi]
            : [gpu, codec, wifi];
        var report = Report(Offer(1, Nvidia), Offer(2, Killer));

        Assert.Same(wifi, Assert.Single(
            PnpDistinctTrustedDevices.Create(devices)));

        var matrix = VendorInventoryCoverage.Create(devices, report.Updates);
        Assert.True(matrix.InventoryComplete);
        Assert.True(matrix.CorrelationsComplete);
        var row = Assert.Single(matrix.Rows);
        Assert.Equal("Killer / Intel", row.Vendor);
        Assert.Equal(1, row.Devices);
        Assert.Equal(1, row.DevicesWithWuaMatches);

        var audit = WuaPnpAssociationAudit.Create(devices, report, true);
        Assert.True(audit.InventoryVerified);
        Assert.False(audit.OverBudget);
        Assert.Equal(2, audit.ConfirmedGuids);
        Assert.Equal(1, audit.ExactHardware);
        Assert.Equal(1, audit.Unattributed);
        Assert.Equal(1, audit.ExcludedAmbiguousPnpInstances);
        Assert.DoesNotContain("PRIVATE_", audit.Text);
        Assert.DoesNotContain(Nvidia, audit.Text);

        Assert.Equal(" PRIVATE_SAME ", gpu.DeviceId);
        Assert.Equal(Nvidia, Assert.Single(gpu.HardwareIds));
    }

    [Fact]
    public void PaddedAliasAfter2048UniqueInstancesKeepsFullCoverage()
    {
        var unique = Enumerable.Range(0,
            VendorInventoryCoverage.MaximumDevices)
            .Select(i => Device($"PRIVATE_INTEL_{i}",
                $@"PCI\VEN_8086&DEV_{i:X4}", "Net")).ToArray();
        var alias = unique[0] with
        {
            DeviceId = " " + unique[0].DeviceId.ToLowerInvariant() + " "
        };
        var devices = unique.Append(alias).ToArray();
        var report = Report(Offer(1, unique[0].HardwareIds[0]));

        var matrix = VendorInventoryCoverage.Create(devices, report.Updates);
        Assert.True(matrix.InventoryComplete);
        Assert.True(matrix.CorrelationsComplete);
        Assert.Equal(VendorInventoryCoverage.MaximumDevices,
            Assert.Single(matrix.Rows).Devices);

        var audit = WuaPnpAssociationAudit.Create(devices, report, true);
        Assert.False(audit.OverBudget);
        Assert.True(audit.InventoryVerified);
        Assert.Equal(1, audit.ExactHardware);
        Assert.Equal(0, audit.Unattributed);
        Assert.Equal(VendorInventoryCoverage.MaximumDevices,
            PnpDistinctTrustedDevices.Create(devices).Count);
    }

    [Fact]
    public void WhitespaceOnlyIdentityCannotBecomeTrustedDevice()
    {
        var invalid = Device("   ", Nvidia, "Display");
        var good = Device("PRIVATE_GOOD", Killer, "Net");
        Assert.Same(good, Assert.Single(
            PnpDistinctTrustedDevices.Create([invalid, good])));
        Assert.Equal(1,
            VendorInventoryCoverage.Create([invalid, good], [])
                .Rows.Sum(row => row.Devices));
    }
}
