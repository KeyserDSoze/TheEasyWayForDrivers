using TheEasyWayForDrivers.Core.Update;
using TheEasyWayForDrivers.ServiceApp.Services;
using Xunit;

namespace TheEasyWayForDrivers.ServiceApp.Tests;

public sealed class WindowsDriverEntryCoverageTests
{
    public sealed class Entry(string hardwareId)
    {
        public string DriverHardwareID => hardwareId;
    }

    public sealed class Entries(IReadOnlyList<string> ids, int? failAt = null,
        Action<int>? onRead = null)
    {
        public int Count => ids.Count;
        public Entry Item(int index)
        {
            onRead?.Invoke(index);
            if (index == failAt)
                throw new InvalidOperationException("Synthetic COM failure");
            return new Entry(ids[index]);
        }
    }

    public sealed class Update(Entries entries)
    {
        public Entries WindowsDriverUpdateEntries { get; } = entries;
    }

    public sealed class MissingInterface
    {
    }

    public sealed class NegativeEntries
    {
        public int Count => -1;
    }

    public sealed class NegativeUpdate
    {
        public NegativeEntries WindowsDriverUpdateEntries { get; } = new();
    }

    private static IReadOnlyList<string> GenericIds(int count) =>
        Enumerable.Range(0, count)
            .Select(i => $@"PCI\VEN_8086&DEV_{i:X4}&SUBSYS_00008086")
            .ToArray();

    [Fact]
    public void ReadsLateGpuAndWifiIdsPastPreviouslyTruncatedFirst128()
    {
        var ids = GenericIds(220).ToList();
        ids.Add(@"PCI\VEN_10DE&DEV_28A0&SUBSYS_16A217AA");
        ids.Add(@"PCI\VEN_8086&DEV_51F1&SUBSYS_11118086");
        var result = WindowsDriverEntryIdReader.ReadAdditionalIdsWithCoverage(
            new Update(new Entries(ids)),
            @"PCI\VEN_8086&DEV_0000&SUBSYS_00008086");

        Assert.Equal(221, result.AdditionalHardwareIds.Count);
        Assert.False(result.Truncated);
        Assert.Contains(@"PCI\VEN_10DE&DEV_28A0&SUBSYS_16A217AA",
            result.AdditionalHardwareIds);
        Assert.Contains(@"PCI\VEN_8086&DEV_51F1&SUBSYS_11118086",
            result.AdditionalHardwareIds);
        Assert.Equal(WuaDriverEntryLimits.MaximumAdditionalIds,
            WindowsDriverEntryIdReader.MaximumEntries);
    }

    [Fact]
    public void Over512EntriesAreBoundedAndDeclaredIncomplete()
    {
        var allIds = GenericIds(550);
        var result = WindowsDriverEntryIdReader.ReadAdditionalIdsWithCoverage(
            new Update(new Entries(allIds)), null);

        Assert.Equal(512, result.AdditionalHardwareIds.Count);
        Assert.True(result.Truncated);
        Assert.DoesNotContain(allIds[525], result.AdditionalHardwareIds);
    }

    [Fact]
    public void ComErrorAtEntry300RetainsValidNvidiaAndKillerIdsBeforeAndAfter()
    {
        var ids = GenericIds(500).ToArray();
        const string nvidia = @"PCI\VEN_10DE&DEV_28A0&SUBSYS_16A217AA";
        const string killer = @"PCI\VEN_8086&DEV_51F1&SUBSYS_11118086";
        ids[299] = nvidia;
        ids[301] = killer;

        var result = WindowsDriverEntryIdReader.ReadAdditionalIdsWithCoverage(
            new Update(new Entries(ids, failAt: 300)), null);

        Assert.Equal(499, result.AdditionalHardwareIds.Count);
        Assert.Contains(nvidia, result.AdditionalHardwareIds);
        Assert.Contains(killer, result.AdditionalHardwareIds);
        Assert.True(result.Truncated);
        // An unreadable entry is NOT a confirmed zero driver association.
    }

    [Fact]
    public void OptionalInterfaceMissingIsNotInterpretedAsFullCoverage()
    {
        var result = WindowsDriverEntryIdReader.ReadAdditionalIdsWithCoverage(
            new MissingInterface(), @"PCI\VEN_10DE&DEV_28A0");
        Assert.Empty(result.AdditionalHardwareIds);
        Assert.True(result.Truncated);

        var invalid = WindowsDriverEntryIdReader.ReadAdditionalIdsWithCoverage(
            new NegativeUpdate(), null);
        Assert.Empty(invalid.AdditionalHardwareIds);
        Assert.True(invalid.Truncated);
    }

    public sealed class NullEntriesUpdate
    {
        public object? WindowsDriverUpdateEntries => null;
    }

    public sealed class FaultyDriverHardwareId
    {
        public string DriverHardwareID =>
            throw new InvalidOperationException("Synthetic single-property COM error");
    }

    public sealed class PropertyFailureEntries
    {
        public int Count => 3;
        public object Item(int index) => index switch
        {
            0 => new Entry(@"HDAUDIO\FUNC_01&VEN_10DE&DEV_009B"),
            1 => new FaultyDriverHardwareId(),
            _ => new Entry(@"PCI\VEN_8086&DEV_51F1")
        };
    }

    public sealed class PropertyFailureUpdate
    {
        public PropertyFailureEntries WindowsDriverUpdateEntries { get; } = new();
    }

    [Fact]
    public void NullCollectionMarksMetadataIncomplete()
    {
        var result = WindowsDriverEntryIdReader.ReadAdditionalIdsWithCoverage(
            new NullEntriesUpdate(), null);
        Assert.Empty(result.AdditionalHardwareIds);
        Assert.True(result.Truncated);
    }

    [Fact]
    public void DriverHardwareIdPropertyFailureKeepsOtherValidatedEntries()
    {
        var result = WindowsDriverEntryIdReader.ReadAdditionalIdsWithCoverage(
            new PropertyFailureUpdate(), null);
        Assert.Equal(2, result.AdditionalHardwareIds.Count);
        Assert.Equal(@"HDAUDIO\FUNC_01&VEN_10DE&DEV_009B", result.AdditionalHardwareIds[0]);
        Assert.Equal(@"PCI\VEN_8086&DEV_51F1", result.AdditionalHardwareIds[1]);
        Assert.True(result.Truncated);
    }

    [Fact]
    public void CancellationMidEnumerationNeverReturnsPartialEvidence()
    {
        using var cts = new CancellationTokenSource();
        var ids = GenericIds(300);
        var reader = new Update(new Entries(ids, onRead: i =>
        {
            if (i == 180)
                cts.Cancel();
        }));

        Assert.Throws<OperationCanceledException>(() =>
            WindowsDriverEntryIdReader.ReadAdditionalIdsWithCoverage(
                reader, null, cts.Token));
    }

    [Fact]
    public void InvalidHardwareIdsAreStillRejectedWithinExtendedBudget()
    {
        var all = GenericIds(220).ToList();
        all.Add(@"C:\Users\PRIVATE\inf");
        all.Add(@"PCI\VEN_10DE&DEV_28A0" + "\r\nBAD");
        all.Add(@"PCI\VEN_10DE&DEV_28A0" + "\u202E");
        all.Add(@"PCI\VEN_10DE&DEV_28A0\PRIVATE_INSTANCE");
        all.Add(@"PCI\VEN_10DE&DEV_28A0..");
        all.Add(@"PCI\VEN_10DE&DEV_28A0");
        var result = WindowsDriverEntryIdReader.ReadAdditionalIdsWithCoverage(
            new Update(new Entries(all)), null);
        Assert.True(result.Truncated); // malformed rows make coverage incomplete
        Assert.Equal(221, result.AdditionalHardwareIds.Count);
        Assert.Contains(@"PCI\VEN_10DE&DEV_28A0", result.AdditionalHardwareIds);
        Assert.DoesNotContain(result.AdditionalHardwareIds,
            id => id.Contains("PRIVATE") || id.Contains("BAD"));
    }
}
