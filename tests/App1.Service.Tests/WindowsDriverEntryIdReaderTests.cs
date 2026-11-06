using TheEasyWayForDrivers.ServiceApp.Services;
using Xunit;

namespace TheEasyWayForDrivers.ServiceApp.Tests;

public sealed class WindowsDriverEntryIdReaderTests
{
    public sealed class FakeEntry(string id)
    {
        public string DriverHardwareID { get; } = id;
    }

    public sealed class FakeEntryCollection(params string[] ids)
    {
        public int Count => ids.Length;
        public FakeEntry Item(int index) => new(ids[index]);
    }

    public sealed class FakeUpdate(params string[] ids)
    {
        public FakeEntryCollection WindowsDriverUpdateEntries { get; } = new(ids);
    }

    public sealed class OldWuaUpdate
    {
        // No WindowsDriverUpdateEntries member: optional COM interface absent.
    }

    public sealed class BrokenUpdate
    {
        public object WindowsDriverUpdateEntries =>
            throw new InvalidOperationException("COM enumeration unavailable");
    }

    public sealed class BrokenEntryCollection
    {
        public int Count => 2;
        public FakeEntry Item(int index) =>
            index == 0 ? new(@"PCI\VEN_10DE&DEV_28A0") :
                throw new InvalidOperationException("COM entry failed");
    }

    public sealed class BrokenEntryUpdate
    {
        public BrokenEntryCollection WindowsDriverUpdateEntries { get; } = new();
    }

    [Fact]
    public void ReadsDistinctAdditionalHardwareAndCompatibleIds()
    {
        var update = new FakeUpdate(
            @"pci\ven_10de&dev_28a0",
            @"HDAUDIO\FUNC_01&VEN_10DE&DEV_009B",
            @"hdaudio\func_01&ven_10de&dev_009b",
            @"ACPI\NVDA0820");

        var result = WindowsDriverEntryIdReader.ReadAdditionalIds(
            update, @"PCI\VEN_10DE&DEV_28A0");

        Assert.Equal(2, result.Count);
        Assert.Equal(@"HDAUDIO\FUNC_01&VEN_10DE&DEV_009B", result[0]);
        Assert.Equal(@"ACPI\NVDA0820", result[1]);
    }

    [Fact]
    public void MissingOrBrokenComInterfaceDoesNotLoseOriginalWuaUpdate()
    {
        Assert.Empty(WindowsDriverEntryIdReader.ReadAdditionalIds(
            new OldWuaUpdate(), @"PCI\VEN_8086&DEV_46A6"));
        Assert.Empty(WindowsDriverEntryIdReader.ReadAdditionalIds(
            new BrokenUpdate(), @"PCI\VEN_8086&DEV_46A6"));
        var partial = WindowsDriverEntryIdReader.ReadAdditionalIdsWithCoverage(
            new BrokenEntryUpdate(), @"PCI\VEN_8086&DEV_46A6");
        Assert.Equal([@"PCI\VEN_10DE&DEV_28A0"], partial.AdditionalHardwareIds);
        Assert.True(partial.Truncated);
    }

    [Fact]
    public void RejectsMalformedIdsAndCapsEnumeration()
    {
        var candidates = Enumerable.Range(0, 180)
            .Select(i => $@"PCI\VEN_8086&DEV_{i:X4}")
            .Prepend("not-a-hardware-id")
            .Prepend(@"C:\Users\Somebody\file.inf")
            .Prepend(@"PCI\VEN_10DE" + "\nextra")
            .ToArray();

        var result = WindowsDriverEntryIdReader.ReadAdditionalIds(
            new FakeUpdate(candidates), null);

        Assert.True(result.Count <= WindowsDriverEntryIdReader.MaximumEntries);
        Assert.All(result, id => Assert.StartsWith(@"PCI\VEN_8086", id));
        Assert.DoesNotContain(result, id => id.Contains("Users"));
    }

    [Fact]
    public void CancellationPropagatesRatherThanReturningAnEmptySuccessfulScan()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.Throws<OperationCanceledException>(() =>
            WindowsDriverEntryIdReader.ReadAdditionalIds(
                new FakeUpdate(@"PCI\VEN_8086&DEV_46A6"), null, cts.Token));
    }

    [Fact]
    public void SupportsZeroOrSingleEntryWithoutInventingOtherDevices()
    {
        Assert.Empty(WindowsDriverEntryIdReader.ReadAdditionalIds(
            new FakeUpdate(), @"PCI\VEN_8086&DEV_46A6"));
        Assert.Empty(WindowsDriverEntryIdReader.ReadAdditionalIds(
            new FakeUpdate(@"PCI\VEN_8086&DEV_46A6"),
            @"PCI\VEN_8086&DEV_46A6"));
    }
}
