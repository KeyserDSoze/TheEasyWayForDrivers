using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

/// <summary>
/// #543: the OEM coverage matrix budgets distinct PnP identities, not
/// duplicate SignedDriver/WMI rows, while inspecting later representations
/// for contradictory same-instance identities within the raw-row quota.
/// </summary>
public sealed class VendorInventoryUniqueInstanceBudgetTests
{
    private const string Nvidia = @"PCI\VEN_10DE&DEV_28A0";
    private const string Realtek =
        @"HDAUDIO\FUNC_01&VEN_10EC&DEV_0295";
    private const string Intel = @"PCI\VEN_8086&DEV_51F1";
    private const string Killer =
        @"PCI\VEN_8086&DEV_51F1&SUBSYS_16721A56";

    private static DriverInfo Device(
        string instance, string hardware, string cls = "Net") =>
        new(instance, "Windows device", "Microsoft", cls, "Microsoft",
            "31.0.0.1", null, "oem42.inf", true, true, 0, [hardware], []);

    private static DriverUpdateInfo Wua(int ordinal, string hardware) =>
        new($"00000000-0000-4000-8000-{ordinal:D12}",
            "Windows Type Driver", null, null, "Microsoft", null, null,
            false, "Microsoft", null, hardware, null,
            SearchSource: "Windows Update online");

    [Fact]
    public void DuplicateIntelRowsCannotHideLaterNvidiaAndRealtekDevices()
    {
        var repeated = Enumerable.Repeat(
            Device("PRIVATE_INTEL_INSTANCE", Intel), 2300);
        var gpu = Device("PRIVATE_GPU_SERIAL", Nvidia, "Display");
        var codec = Device("PRIVATE_AUDIO_SERIAL", Realtek, "MEDIA");
        var all = repeated.Concat([gpu, codec]).ToArray();
        var report = VendorInventoryCoverage.Create(all,
            [Wua(1, Nvidia), Wua(2, Realtek)]);

        Assert.True(report.InventoryComplete);
        Assert.True(report.CorrelationsComplete);
        Assert.Equal(3, report.Rows.Sum(row => row.Devices));
        Assert.Equal(2, report.Rows.Sum(row => row.DevicesWithWuaMatches));
        Assert.Equal(1, Assert.Single(report.Rows,
            row => row.Vendor == "Intel").Devices);
        Assert.Equal(1, Assert.Single(report.Rows,
            row => row.Vendor == "NVIDIA").DevicesWithWuaMatches);
        Assert.Equal(1, Assert.Single(report.Rows,
            row => row.Vendor == "Realtek").DevicesWithWuaMatches);
        Assert.Contains("inventario PnP completo", report.ScopeNote);

        var copy = ScanCoverageText.Create(null, report.Rows,
            all.Length, 0, 0, all, inventoryComplete: true,
            vendorCoverage: report);
        Assert.Contains("NVIDIA: 1 dispositivi", copy);
        Assert.Contains("Realtek: 1 dispositivi", copy);
        Assert.DoesNotContain("PRIVATE_", copy);
        Assert.DoesNotContain(Nvidia, copy);
        Assert.DoesNotContain(Realtek, copy);
    }

    [Fact]
    public void LaterConflictingSameInstanceModelStillVetoesVendorAndWua()
    {
        var gpu = Device("PRIVATE_GPU", Nvidia, "Display");
        var foreign = Device("private_gpu", Realtek, "MEDIA");
        var good = Device("PRIVATE_KILLER", Killer);
        var all = Enumerable.Repeat(gpu, 2200)
            .Concat([foreign, good]).ToArray();
        var coverage = VendorInventoryCoverage.Create(all,
            [Wua(1, Nvidia), Wua(2, Killer)]);

        Assert.True(coverage.InventoryComplete);
        Assert.True(coverage.CorrelationsComplete);
        var row = Assert.Single(coverage.Rows);
        Assert.Equal("Killer / Intel", row.Vendor);
        Assert.Equal(1, row.Devices);
        Assert.Equal(1, row.DevicesWithWuaMatches);
        Assert.Equal(1, row.WuaCorrelations);
        Assert.DoesNotContain("NVIDIA", row.DisplayLine(true));
    }

    [Fact]
    public void Exactly2048UniqueInstancesWithExtraDuplicatesAreComplete()
    {
        var unique = Enumerable.Range(0,
            VendorInventoryCoverage.MaximumDevices)
            .Select(i => Device($"PRIVATE_INTEL_{i}",
                $@"PCI\VEN_8086&DEV_{i:X4}"))
            .ToArray();
        var all = unique.Concat(
            Enumerable.Repeat(unique[0], 300)).ToArray();
        var coverage = VendorInventoryCoverage.Create(all, []);

        Assert.True(coverage.InventoryComplete);
        Assert.Equal(VendorInventoryCoverage.MaximumDevices,
            Assert.Single(coverage.Rows).Devices);
        Assert.DoesNotContain("parziali", coverage.ScopeNote);
    }

    [Fact]
    public void ExceedingDistinctLimitRemainsPartialAndStillChecksLateDuplicate()
    {
        var gpu = Device("PRIVATE_GPU", Nvidia, "Display");
        var distinct = Enumerable.Range(0,
            VendorInventoryCoverage.MaximumDevices)
            .Select(i => Device($"PRIVATE_INTEL_{i}",
                $@"PCI\VEN_8086&DEV_{i:X4}"));
        var conflict = Device("private_gpu", Realtek, "MEDIA");
        var all = new[] { gpu }.Concat(distinct).Append(conflict).ToArray();
        var coverage = VendorInventoryCoverage.Create(all, [Wua(1, Nvidia)]);

        Assert.False(coverage.InventoryComplete);
        Assert.True(coverage.CorrelationsComplete);
        Assert.DoesNotContain(coverage.Rows,
            row => row.Vendor == "NVIDIA");
        Assert.Contains("parziali", coverage.ScopeNote);
        Assert.All(coverage.Rows, row =>
            Assert.Contains("inventario parziale", row.DisplayLine(true)));
    }

    [Fact]
    public void RawRowBudgetIsBoundedAndCannotClaimCompleteCoverage()
    {
        var intel = Device("PRIVATE_INTEL", Intel);
        var all = Enumerable.Repeat(intel,
            VendorInventoryCoverage.MaximumRawPnpRows)
            .Append(Device("PRIVATE_GPU", Nvidia, "Display")).ToArray();
        var coverage = VendorInventoryCoverage.Create(all, [Wua(1, Nvidia)]);

        Assert.False(coverage.InventoryComplete);
        Assert.True(coverage.CorrelationsComplete);
        Assert.Equal(1, Assert.Single(coverage.Rows).Devices);
        Assert.DoesNotContain(coverage.Rows,
            row => row.Vendor == "NVIDIA");
        Assert.Contains("parziali", coverage.ScopeNote);
    }
}
