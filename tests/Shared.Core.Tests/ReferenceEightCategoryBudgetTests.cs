using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

/// <summary>
/// #547: the eight-category Home/copy matrix must honor the same
/// conservative diagnostic limits as WuaPnpAssociationAudit.
/// </summary>
public sealed class ReferenceEightCategoryBudgetTests
{
    private const string Nvidia = @"PCI\VEN_10DE&DEV_28A0";

    private static DriverInfo Gpu(string instance) =>
        new(instance, "Microsoft Basic Display Adapter", "Microsoft",
            "Display", "Microsoft", "31.0.0.1", null, "oem42.inf",
            true, true, 0, [Nvidia], []);

    private static DriverUpdateInfo Offer(int number,
        IReadOnlyList<string>? additional = null) =>
        new($"00000000-0000-4000-8000-{number:D12}", "Synthetic driver",
            null, "Display", "Microsoft", null, null, false,
            "Microsoft", null, Nvidia, null,
            SearchSource: "Windows Update online",
            AdditionalHardwareIds: additional);

    private static DriverSearchReport Report(
        IReadOnlyList<DriverUpdateInfo> updates) =>
        new(updates, [
            new DriverSearchPassStatus("Windows Update online",
                "Completed", updates.Count, "")
        ]);

    private static void AssertFailClosed(
        IReadOnlyList<DriverInfo> devices, DriverSearchReport report)
    {
        var audit = WuaPnpAssociationAudit.Create(
            devices, report, inventoryComplete: true);
        Assert.True(audit.OverBudget);
        Assert.False(audit.InventoryVerified);

        var rows = ReferenceDriverCoverage.Create(devices, report);
        Assert.Equal(8, rows.Count);
        Assert.All(rows, row =>
        {
            Assert.True(row.DiagnosticOverBudget);
            Assert.Contains("non valutabili",
                row.DisplayLine(true, true, true));
            Assert.DoesNotContain("0 candidati",
                row.DisplayLine(true, true, true));
        });

        var home = ReferenceCoveragePresentation.Create(
            devices, report, inventoryComplete: true);
        Assert.All(home, row =>
        {
            Assert.Contains("non valutabili", row.WindowsDriverStatus);
            Assert.DoesNotContain("0 candidati", row.WindowsDriverStatus);
            Assert.DoesNotContain("PRIVATE_", string.Join(" ",
                row.PnpStatus, row.WindowsDriverStatus, row.NextStep));
            Assert.False(row.IsInstallable);
        });

        var copy = ScanCoverageText.Create(report, [], devices.Count,
            0, 0, devices, inventoryComplete: true);
        Assert.Contains("input oltre il limite", copy);
        Assert.Contains("diagnostica oltre i limiti", copy);
        Assert.DoesNotContain("0 candidati WUA Type=Driver correlati", copy);
        Assert.DoesNotContain("PRIVATE_", copy);
        Assert.DoesNotContain(Nvidia, copy);
    }

    [Fact]
    public void MoreThan2048DistinctInstancesAreNotReportedAsVerified()
    {
        var devices = Enumerable.Range(0,
            VendorInventoryCoverage.MaximumDevices + 1)
            .Select(i => Gpu($"PRIVATE_GPU_{i}")).ToArray();
        AssertFailClosed(devices, Report([Offer(547)]));
    }

    [Fact]
    public void MoreThan8192RawDuplicateRowsAreNotAccepted()
    {
        var devices = Enumerable.Repeat(Gpu("PRIVATE_GPU_DUP"),
            VendorInventoryCoverage.MaximumRawPnpRows + 1).ToArray();
        AssertFailClosed(devices, Report([Offer(547)]));
    }

    [Fact]
    public void MoreThan4000WuaRowsAreNotTreatedAsVerifiedAssociations()
    {
        var updates = Enumerable.Range(0,
            VendorInventoryCoverage.MaximumDriverUpdates + 1)
            .Select(i => Offer(i)).ToArray();
        AssertFailClosed([Gpu("PRIVATE_GPU")], Report(updates));
    }

    [Fact]
    public void WuaIdVolumeBudgetFailsBeforeMergeEvenWith4000Rows()
    {
        var repeatedIds = Enumerable.Repeat(Nvidia, 600).ToArray();
        var updates = Enumerable.Range(0,
            VendorInventoryCoverage.MaximumDriverUpdates)
            .Select(i => Offer(i, repeatedIds)).ToArray();
        AssertFailClosed([Gpu("PRIVATE_GPU")], Report(updates));
    }

    [Fact]
    public void PnpIdVolumeBudgetFailsBeforeCorrelations()
    {
        var ids = Enumerable.Repeat(Nvidia,
            (int)VendorInventoryCoverage.MaximumPnpIds + 1).ToArray();
        var device = Gpu("PRIVATE_GPU") with { HardwareIds = ids };
        AssertFailClosed([device], Report([Offer(547)]));
    }

    [Fact]
    public void AdmittedPaddedDuplicateAfter2048InstancesKeepsRealCounts()
    {
        var unique = Enumerable.Range(0,
            VendorInventoryCoverage.MaximumDevices)
            .Select(i => Gpu($"PRIVATE_GPU_{i}")).ToArray();
        var devices = unique.Append(
            Gpu(" private_gpu_0 ")).ToArray();
        var report = Report([Offer(547)]);

        Assert.False(WuaPnpAssociationAudit.Create(
            devices, report, true).OverBudget);
        var gpu = ReferenceDriverCoverage.Create(devices, report)[5];
        Assert.False(gpu.DiagnosticOverBudget);
        Assert.Equal(VendorInventoryCoverage.MaximumDevices, gpu.Devices);
        Assert.Equal(1, gpu.WindowsDriverCandidates);

        var home = ReferenceCoveragePresentation.Create(
            devices, report, true)[5];
        Assert.Contains("2048 dispositivi", home.PnpStatus);
        Assert.Contains("1 candidati driver Windows", home.WindowsDriverStatus);
        Assert.DoesNotContain("non valutabili", home.WindowsDriverStatus);
    }
}
