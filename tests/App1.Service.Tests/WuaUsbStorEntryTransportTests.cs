using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;
using TheEasyWayForDrivers.ServiceApp.Services;
using Xunit;

namespace TheEasyWayForDrivers.ServiceApp.Tests;

/// <summary>
/// Exercise the COM-style WUA reader, not just the shared selection policy.
/// An exact late USBSTOR Hardware ID is diagnostic association evidence.
/// </summary>
public sealed class WuaUsbStorEntryTransportTests
{
    private const string Storage =
        @"USBSTOR\Disk&Ven_SanDisk&Prod_Cruzer_Blade&Rev_1.00";
    private const string Padded =
        @"USBSTOR\DiskSanDisk_Cruzer_Blade____0001";

    private static string[] Prefix(int count) =>
        Enumerable.Range(1, count)
            .Select(i => $@"PCI\VEN_9999&DEV_{i:X4}")
            .ToArray();

    private static DriverInfo Device(string id) =>
        new("PRIVATE_SERIAL", "Windows storage", "Microsoft",
            "DiskDrive", "Microsoft", "1.0", null, null,
            true, true, 0, [id], []);

    [Fact]
    public void ComEntryReaderKeepsLateSpecificStorageModelsAndExactMatches()
    {
        var input = Prefix(1750).Concat([Storage, Padded]).ToArray();
        var read = WindowsDriverEntryIdReader.ReadAdditionalIdsWithCoverage(
            new WindowsDriverEntryIdReaderTests.FakeUpdate(input), null);

        Assert.Equal(1024, read.AdditionalHardwareIds.Count);
        Assert.Equal(input.Take(960), read.AdditionalHardwareIds.Take(960));
        Assert.Contains(Storage, read.AdditionalHardwareIds);
        Assert.Contains(Padded, read.AdditionalHardwareIds);
        Assert.True(read.Truncated);

        var offer = new DriverUpdateInfo(
            "00000000-0000-4000-8000-000000000384",
            "Windows driver", null, "DiskDrive", "Microsoft", null,
            null, false, "Microsoft", null, null, null,
            SearchSource: "Windows Update online",
            AdditionalHardwareIds: read.AdditionalHardwareIds,
            AdditionalHardwareIdsIncomplete: read.Truncated);
        Assert.Equal(2, DriverUpdateMatcher.MatchStrength(
            Device(Storage), offer));
        Assert.Equal(2, DriverUpdateMatcher.MatchStrength(
            Device(Padded), offer));
        Assert.Equal(0, DriverUpdateMatcher.MatchStrength(
            Device(@"USBSTOR\GenDisk"), offer));
    }

    [Fact]
    public void SamplingLongWuaCollectionNeverClaimsFullUsbStorCoverage()
    {
        var ids = Prefix(5100);
        ids[^1] = Storage;
        var read = WindowsDriverEntryIdReader.ReadAdditionalIdsWithCoverage(
            new WindowsDriverEntryIdReaderTests.FakeUpdate(ids), null);

        Assert.True(read.Truncated);
        Assert.Equal(1024, read.AdditionalHardwareIds.Count);
        Assert.Contains(Storage, read.AdditionalHardwareIds);
        Assert.DoesNotContain(read.AdditionalHardwareIds,
            id => id.Contains("PRIVATE", StringComparison.Ordinal));
    }
}
