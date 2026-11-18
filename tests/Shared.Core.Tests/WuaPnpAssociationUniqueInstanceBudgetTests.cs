using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

/// <summary>
/// #544: WUA→PnP QA must use the same distinct-instance/raw-row budgets
/// as the OEM vendor coverage matrix, without leaking device identities
/// or accepting contradictory late WMI records.
/// </summary>
public sealed class WuaPnpAssociationUniqueInstanceBudgetTests
{
    private const string Nvidia = @"PCI\VEN_10DE&DEV_28A0";
    private const string Realtek = @"HDAUDIO\FUNC_01&VEN_10EC&DEV_0295";
    private const string Intel = @"PCI\VEN_8086&DEV_51F1";
    private const string Killer =
        @"PCI\VEN_8086&DEV_51F1&SUBSYS_16721A56";

    private static DriverInfo Device(string instance, string model,
        string cls = "Net") =>
        new(instance, "Microsoft Generic Device", "Microsoft", cls,
            "Microsoft", "31.0.0.1", null, "oem42.inf", true, true, 0,
            [model], []);

    private static DriverUpdateInfo Offer(int index, string model) =>
        new($"00000000-0000-4000-8000-{index:D12}",
            "Windows driver", null, "Display", "Microsoft",
            "31.0.0.2", null, false, "Microsoft", null, model, null,
            SearchSource: "Windows Update online");

    private static DriverSearchReport Report(params DriverUpdateInfo[] offers) =>
        new(offers,
        [
            new DriverSearchPassStatus("Windows Update online", "Completed",
                offers.Length, "PRIVATE_COM_ERROR")
        ]);

    [Fact]
    public void DuplicatedIntelRowsDoNotHideLateNvidiaAndRealtekGuidMatches()
    {
        var intel = Device("PRIVATE_INTEL_SERIAL", Intel);
        var gpu = Device("PRIVATE_NVIDIA_SERIAL", Nvidia, "Display");
        var codec = Device("PRIVATE_REALTEK_SERIAL", Realtek, "MEDIA");
        var devices = Enumerable.Repeat(intel, 2300)
            .Concat([gpu, codec]).ToArray();
        var search = Report(Offer(1, Nvidia), Offer(2, Realtek));

        var audit = WuaPnpAssociationAudit.Create(
            devices, search, inventoryComplete: true);
        var matrix = VendorInventoryCoverage.Create(devices, search.Updates);

        Assert.True(audit.SearchVerified);
        Assert.True(audit.InventoryVerified);
        Assert.False(audit.OverBudget);
        Assert.Equal(2, audit.ConfirmedGuids);
        Assert.Equal(2, audit.ExactHardware);
        Assert.Equal(0, audit.Unattributed);
        Assert.Equal(3, matrix.Rows.Sum(row => row.Devices));
        Assert.Equal(2, matrix.Rows.Sum(row => row.DevicesWithWuaMatches));
        Assert.Contains("2 con Hardware ID esatto", audit.Text);
        Assert.DoesNotContain("PRIVATE_", audit.Text);

        var copy = ScanCoverageText.Create(
            search, matrix.Rows, devices.Length, 0, 0,
            devices, inventoryComplete: true, vendorCoverage: matrix);
        Assert.Contains("2 con Hardware ID esatto", copy);
        Assert.DoesNotContain("PRIVATE_", copy);
        Assert.DoesNotContain(Nvidia, copy);
        Assert.DoesNotContain(Realtek, copy);
        Assert.DoesNotContain("00000000-0000-4000", copy);
    }

    [Fact]
    public void LateContradictorySameInstanceModelCannotProduceFalseWuaMatch()
    {
        var gpu = Device("PRIVATE_GPU_INSTANCE", Nvidia, "Display");
        var conflict = Device("private_gpu_instance", Realtek, "MEDIA");
        var wifi = Device("PRIVATE_WIFI_INSTANCE", Killer);
        var devices = Enumerable.Repeat(gpu, 2200)
            .Concat([conflict, wifi]).ToArray();
        var search = Report(Offer(1, Nvidia), Offer(2, Killer));

        var audit = WuaPnpAssociationAudit.Create(
            devices, search, inventoryComplete: true);

        Assert.True(audit.InventoryVerified);
        Assert.False(audit.OverBudget);
        Assert.Equal(2, audit.ConfirmedGuids);
        Assert.Equal(1, audit.ExactHardware);
        Assert.Equal(1, audit.Unattributed);
        Assert.Equal(1, audit.ExcludedAmbiguousPnpInstances);
        Assert.Contains("identità hardware contraddittorie", audit.Text);
        Assert.DoesNotContain("PRIVATE_GPU_INSTANCE", audit.Text);
    }

    [Fact]
    public void Exactly2048DistinctInstancesRemainVerifiableWithMoreRawRows()
    {
        var gpu = Device("PRIVATE_GPU", Nvidia, "Display");
        var others = Enumerable.Range(0,
            VendorInventoryCoverage.MaximumDevices - 1)
            .Select(i => Device($"PRIVATE_INTEL_{i}",
                $@"PCI\VEN_8086&DEV_{i:X4}"));
        var devices = new[] { gpu }.Concat(others)
            .Concat(Enumerable.Repeat(gpu, 350)).ToArray();
        var audit = WuaPnpAssociationAudit.Create(
            devices, Report(Offer(1, Nvidia)), true);

        Assert.False(audit.OverBudget);
        Assert.True(audit.InventoryVerified);
        Assert.Equal(1, audit.ExactHardware);
        Assert.Equal(0, audit.Unattributed);
    }

    [Fact]
    public void Over2048DistinctInstancesFailClosedRegardlessOfRawCapacity()
    {
        var distinct = Enumerable.Range(0,
            VendorInventoryCoverage.MaximumDevices + 1)
            .Select(i => Device($"PRIVATE_INTEL_{i}",
                $@"PCI\VEN_8086&DEV_{i:X4}"));
        var audit = WuaPnpAssociationAudit.Create(
            distinct, Report(Offer(1, Nvidia)), true);

        Assert.True(audit.SearchVerified);
        Assert.False(audit.InventoryVerified);
        Assert.True(audit.OverBudget);
        Assert.Contains("associazioni non valutabili", audit.Text);
        Assert.DoesNotContain("0 non attribuite", audit.Text);
    }

    [Fact]
    public void Exactly8192RawDuplicateRowsAreStillWithinTheSafetyLimit()
    {
        var single = Device("PRIVATE_INTEL", Intel);
        var devices = Enumerable.Repeat(single,
            VendorInventoryCoverage.MaximumRawPnpRows).ToArray();
        var audit = WuaPnpAssociationAudit.Create(
            devices, Report(Offer(1, Intel)), true);

        Assert.True(audit.InventoryVerified);
        Assert.False(audit.OverBudget);
        Assert.Equal(1, audit.ExactHardware);
    }

    [Fact]
    public void RawRowOverBudgetCannotImplyZeroDriverMatches()
    {
        var single = Device("PRIVATE_INTEL", Intel);
        var devices = Enumerable.Repeat(single,
            VendorInventoryCoverage.MaximumRawPnpRows)
            .Append(Device("PRIVATE_NVIDIA", Nvidia, "Display")).ToArray();
        var audit = WuaPnpAssociationAudit.Create(
            devices, Report(Offer(1, Nvidia)), true);

        Assert.True(audit.OverBudget);
        Assert.False(audit.InventoryVerified);
        Assert.Contains("input oltre il limite", audit.Text);
        Assert.DoesNotContain("0 non attribuite", audit.Text);
        Assert.DoesNotContain("PRIVATE_", audit.Text);
    }
}
