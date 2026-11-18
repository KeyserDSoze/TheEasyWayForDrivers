using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

/// <summary>Regression #553: Home must honor audited WUA/PnP limits.</summary>
public sealed class HomeVendorDiagnosticBudgetTests
{
    private const string GpuId = @"PCI\VEN_10DE&DEV_28A0";
    private const string AudioId = @"HDAUDIO\FUNC_01&VEN_10EC&DEV_0295";
    private const string KillerId = @"PCI\VEN_8086&DEV_51F1";

    private static DriverInfo Device(string instance, string name, string cls, string id) =>
        new(instance, name, "Microsoft", cls, "Microsoft",
            "1.0", null, "oem42.inf", true, true, 0, [id], []);

    private static DriverInfo Gpu(string instance = "PRIVATE_GPU") =>
        Device(instance, "Microsoft Basic Display Adapter", "Display", GpuId);

    private static DriverUpdateInfo Offer(int index, string id = GpuId,
        IReadOnlyList<string>? additional = null) =>
        new($"00000000-0000-4000-8000-{index:D12}",
            "Synthetic driver", null, "Display", "Microsoft", null,
            null, false, "Microsoft", null, id, null,
            SearchSource: "Windows Update online",
            AdditionalHardwareIds: additional);

    private static DriverSearchReport Report(IReadOnlyList<DriverUpdateInfo> updates) =>
        new(updates, [new DriverSearchPassStatus(
            "Windows Update online", "Completed", updates.Count, "")]);

    private static void AssertPnpOverflow(IReadOnlyList<DriverInfo> devices)
    {
        var report = Report([Offer(553)]);
        Assert.True(WuaPnpAssociationAudit.Create(devices, report, true).OverBudget);
        var rows = HomeVendorCoverageSummary.Create(devices, report, true);
        Assert.Equal(3, rows.Count);
        Assert.All(rows, row =>
        {
            Assert.Contains("oltre i limiti", row.DeviceStatus);
            Assert.Contains("non disponibile", row.WindowsStatus);
            Assert.DoesNotContain("0 candidati", row.WindowsStatus);
            Assert.False(row.CanInspectLocalDrivers);
            Assert.False(row.IsInstallable);
        });
        Assert.Null(HomeVendorCoverageSummary.ChooseLocalDriverStoreDevice(
            devices, "NVIDIA", true));
        Assert.DoesNotContain("PRIVATE_", string.Join(" ",
            rows.Select(row => row.DeviceStatus + row.WindowsStatus)));
    }

    [Fact]
    public void MoreThan2048DistinctInstancesFailsClosed() =>
        AssertPnpOverflow(Enumerable.Range(0,
            VendorInventoryCoverage.MaximumDevices + 1)
            .Select(i => Gpu($"PRIVATE_GPU_{i}")).ToArray());

    [Fact]
    public void MoreThan8192RawDuplicatesFailsClosed() =>
        AssertPnpOverflow(Enumerable.Repeat(Gpu(),
            VendorInventoryCoverage.MaximumRawPnpRows + 1).ToArray());

    [Fact]
    public void MoreThan262144PnpIdsFailsClosed()
    {
        var ids = Enumerable.Repeat(GpuId,
            (int)VendorInventoryCoverage.MaximumPnpIds + 1).ToArray();
        AssertPnpOverflow([Gpu() with { HardwareIds = ids }]);
    }

    private static void AssertWuaOverflow(IReadOnlyList<DriverUpdateInfo> updates)
    {
        var devices = new[]
        {
            Gpu(),
            Device("PRIVATE_KILLER", "Killer Wi-Fi", "Net", KillerId),
            Device("PRIVATE_AUDIO", "High Definition Audio", "MEDIA", AudioId)
        };
        var report = Report(updates);
        Assert.True(WuaPnpAssociationAudit.Create(devices, report, true).OverBudget);
        var rows = HomeVendorCoverageSummary.Create(devices, report, true);
        Assert.All(rows, row =>
        {
            Assert.Contains("1 dispositivo", row.DeviceStatus);
            Assert.Contains("non valutabili", row.WindowsStatus);
            Assert.DoesNotContain("0 candidati", row.WindowsStatus);
            Assert.True(row.CanInspectLocalDrivers);
            Assert.False(row.IsInstallable);
        });
        Assert.Equal("PRIVATE_GPU",
            HomeVendorCoverageSummary.ChooseLocalDriverStoreDevice(
                devices, "NVIDIA", true)?.DeviceId);
        Assert.DoesNotContain("PRIVATE_", string.Join(" ",
            rows.Select(row => row.DeviceStatus + row.WindowsStatus)));
    }

    [Fact]
    public void MoreThan4000RawWuaOffersFailsClosed() =>
        AssertWuaOverflow(Enumerable.Range(0,
            VendorInventoryCoverage.MaximumDriverUpdates + 1)
            .Select(i => Offer(i)).ToArray());

    [Fact]
    public void MoreThanTwoMillionWuaIdsFailsClosed()
    {
        var repeated = Enumerable.Repeat(GpuId, 600).ToArray();
        AssertWuaOverflow(Enumerable.Range(0,
            VendorInventoryCoverage.MaximumDriverUpdates)
            .Select(i => Offer(i, additional: repeated)).ToArray());
    }

    [Fact]
    public void DuplicateAliasAt2048DistinctInstancesKeepsMeasuredCount()
    {
        var original = Enumerable.Range(0,
            VendorInventoryCoverage.MaximumDevices)
            .Select(i => Gpu($"PRIVATE_GPU_{i}")).ToArray();
        var devices = original.Append(Gpu(" private_gpu_0 ")).ToArray();
        var report = Report([Offer(553)]);
        Assert.False(WuaPnpAssociationAudit.Create(devices, report, true).OverBudget);
        var row = HomeVendorCoverageSummary.Create(devices, report, true)[0];
        Assert.Contains("2048 dispositivi", row.DeviceStatus);
        Assert.Contains("1 candidato WUA", row.WindowsStatus);
        Assert.True(row.CanInspectLocalDrivers);
    }

    [Fact]
    public void RepeatedSameGuidCountsOncePerCategoryAndRetainsCrossCategoryMatch()
    {
        var first = Offer(553, GpuId, [AudioId, AudioId]);
        var second = first with { HardwareId = AudioId,
            AdditionalHardwareIds = [GpuId] };
        var report = Report([first, second, Offer(554, KillerId)]);
        var rows = HomeVendorCoverageSummary.Create(
            [Gpu(), Device("PRIVATE_KILLER", "Killer Wi-Fi", "Net", KillerId),
             Device("PRIVATE_AUDIO", "High Definition Audio", "MEDIA", AudioId)],
            report, true);
        Assert.All(rows, row =>
            Assert.Contains("1 candidato WUA", row.WindowsStatus));
        Assert.DoesNotContain("PRIVATE_", string.Join(" ",
            rows.Select(row => row.WindowsStatus)));
        Assert.All(rows, row => Assert.False(row.IsInstallable));
    }

    [Fact]
    public void UnverifiedInventoryIsNotEnumerated()
    {
        var read = 0;
        IEnumerable<DriverInfo> Guarded()
        {
            while (true)
            {
                if (++read > 2)
                    throw new InvalidOperationException("Unverified inventory enumerated.");
                yield return Gpu();
            }
        }
        var rows = HomeVendorCoverageSummary.Create(
            Guarded(), Report([Offer(553)]), false);
        Assert.Equal(0, read);
        Assert.All(rows, row => Assert.False(row.CanInspectLocalDrivers));
    }
}
