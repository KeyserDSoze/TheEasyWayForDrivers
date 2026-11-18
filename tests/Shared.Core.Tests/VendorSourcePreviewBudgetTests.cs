using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

/// <summary>
/// #557: manual OEM links may be useful with WUA out-of-budget, but
/// unverified snapshots may never claim a measured zero association.
/// </summary>
public sealed class VendorSourcePreviewBudgetTests
{
    private const string GpuId = @"PCI\VEN_10DE&DEV_28A0";
    private const string WifiId = @"PCI\VEN_14C3&DEV_0608";

    private static DriverInfo Device(int index, string id = GpuId,
        string cls = "Display") =>
        new($"PRIVATE_INSTANCE_{index}", "Generic Windows device",
            "Microsoft", cls, "Microsoft", "1.0", null, "oem1.inf",
            true, true, 0, [id], []);

    private static DriverUpdateInfo Offer(int index,
        IReadOnlyList<string>? extra = null) =>
        new($"00000000-0000-4000-8000-{index:D12}",
            "PRIVATE_WUA_TITLE", null, "Display", "Microsoft",
            null, null, false, "Microsoft", null, GpuId, null,
            SearchSource: "Windows Update online",
            AdditionalHardwareIds: extra);

    private static void AssertPnpUnverifiable(IEnumerable<DriverInfo> devices)
    {
        var enumeratedWua = false;
        IEnumerable<DriverUpdateInfo> GuardedWua()
        {
            enumeratedWua = true;
            yield return Offer(557);
        }
        var preview = VendorSourceDiscovery.FindWithCoverage(
            devices, GuardedWua());
        Assert.False(preview.InventoryComplete);
        Assert.False(preview.CorrelationsComplete);
        Assert.Empty(preview.Rows);
        Assert.Equal(0, preview.OmittedDeviceCount);
        Assert.Contains("NON VERIFICABILE", preview.CoverageNotice);
        Assert.DoesNotContain("PRIVATE_", preview.CoverageNotice);
        Assert.DoesNotContain(GpuId, preview.CoverageNotice);
        Assert.False(enumeratedWua);
    }

    [Fact]
    public void RawPnpOverflowConsumesAtMostOneRowPastTheLimit()
    {
        var reads = 0;
        IEnumerable<DriverInfo> Infinite()
        {
            while (true)
            {
                if (++reads > VendorInventoryCoverage.MaximumRawPnpRows + 1)
                    throw new InvalidOperationException("Unbounded PnP read.");
                yield return Device(1);
            }
        }
        AssertPnpUnverifiable(Infinite());
        Assert.Equal(VendorInventoryCoverage.MaximumRawPnpRows + 1, reads);
    }

    [Fact]
    public void DistinctInstanceOverflowNeverReportsPartialVendorCounts()
    {
        AssertPnpUnverifiable(Enumerable.Range(0,
            VendorInventoryCoverage.MaximumDevices + 1).Select(i => Device(i)));
    }

    [Fact]
    public void TooManyPnpIdsFailBeforeAnyMatching()
    {
        var ids = Enumerable.Repeat(GpuId,
            (int)VendorInventoryCoverage.MaximumPnpIds + 1).ToArray();
        AssertPnpUnverifiable([Device(1) with { HardwareIds = ids }]);
    }

    private static void AssertWuaUnverifiable(
        IEnumerable<DriverUpdateInfo> updates)
    {
        var preview = VendorSourceDiscovery.FindWithCoverage(
            [Device(1), Device(2, WifiId, "Net")], updates);
        Assert.True(preview.InventoryComplete);
        Assert.False(preview.CorrelationsComplete);
        Assert.Equal(2, preview.EligibleDeviceCount);
        Assert.Collection(preview.Rows,
            row => Assert.Equal("MediaTek", row.Vendor),
            row => Assert.Equal("NVIDIA", row.Vendor));
        Assert.Equal(0, preview.OmittedDeviceCount);
        Assert.Contains("NON VALUTABILI", preview.CoverageNotice);
        Assert.DoesNotContain("PRIVATE_", preview.CoverageNotice);

        Assert.All(preview.Rows, row =>
        {
            Assert.False(row.WuaCorrelationsVerified);
            Assert.Equal(0, row.WuaMatches);
            Assert.Contains("non valutabile", row.StatusText);
            Assert.False(row.IsInstallable);
            Assert.True(VendorSourceDiscovery.IsTrustedOfficialUrl(
                row.OfficialUrl));
        });

        var groups = VendorManualHandoffSummary.Create(preview.Rows);
        Assert.Equal(new[] { "MediaTek", "NVIDIA" },
            groups.Select(row => row.Vendor));
        Assert.All(groups, group =>
        {
            Assert.False(group.WuaCorrelationsVerified);
            Assert.Contains("non valutabile", group.StatusText);
            Assert.DoesNotContain("0 con candidato WUA", group.StatusText);
            Assert.Equal(1, group.GroupedManualDevices);
            Assert.False(group.IsInstallable);
        });
    }

    [Fact]
    public void OverFourThousandWuaRowsCannotBecomeMeasuredZero()
    {
        var reads = 0;
        IEnumerable<DriverUpdateInfo> Infinite()
        {
            while (true)
            {
                if (++reads > VendorInventoryCoverage.MaximumDriverUpdates + 1)
                    throw new InvalidOperationException("Unbounded WUA read.");
                yield return Offer(reads);
            }
        }
        AssertWuaUnverifiable(Infinite());
        Assert.Equal(VendorInventoryCoverage.MaximumDriverUpdates + 1, reads);
    }

    [Fact]
    public void OverTwoMillionWuaIdsKeepsOnlyVerifiedPnpLinks()
    {
        var repeated = Enumerable.Repeat(GpuId, 600).ToArray();
        var updates = Enumerable.Range(0,
            VendorInventoryCoverage.MaximumDriverUpdates)
            .Select(i => Offer(i, repeated)).ToArray();
        AssertWuaUnverifiable(updates);
    }

    [Fact]
    public void UnderBudgetPreviewKeepsExactPerGuidMatchesAndFairness()
    {
        var preview = VendorSourceDiscovery.FindWithCoverage(
            [Device(1), Device(2, WifiId, "Net")],
            [Offer(557)], maxResults: 1);
        Assert.True(preview.InventoryComplete);
        Assert.True(preview.CorrelationsComplete);
        Assert.Equal(2, preview.EligibleDeviceCount);
        Assert.Equal(1, preview.OmittedDeviceCount);
        var gpu = Assert.Single(preview.Rows);
        Assert.Equal("NVIDIA", gpu.Vendor);
        Assert.Equal(1, gpu.WuaMatches);
        Assert.True(gpu.WuaCorrelationsVerified);
        Assert.False(gpu.IsInstallable);
        Assert.Contains("PARZIALE", preview.CoverageNotice);
        Assert.DoesNotContain("NON VALUTABILI", preview.CoverageNotice);
    }

    [Fact]
    public void EmptyVerifiedPreviewHasNoFalseFailureNotice()
    {
        var preview = VendorSourceDiscovery.FindWithCoverage([], []);
        Assert.True(preview.InventoryComplete);
        Assert.True(preview.CorrelationsComplete);
        Assert.Empty(preview.Rows);
        Assert.Equal("", preview.CoverageNotice);
    }
}
