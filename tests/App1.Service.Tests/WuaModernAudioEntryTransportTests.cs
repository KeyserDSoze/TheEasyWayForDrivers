using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;
using TheEasyWayForDrivers.ServiceApp.Services;
using Xunit;

namespace TheEasyWayForDrivers.ServiceApp.Tests;

/// <summary>
/// Exercises the COM-style IWindowsDriverUpdate4 entry reader, not just
/// the Core selection policy. A WUA GUID may have >1024 PnP models,
/// including late SNDW/ACP audio on current Windows laptops.
/// </summary>
public sealed class WuaModernAudioEntryTransportTests
{
    private const string Sndw =
        @"SNDW\CLASS_00&MAN_025D&PART_0711&VER_02&UID_00";
    private const string Acp =
        @"ACP\DEVTYPE_0005&DEVREV_0000&VEN_1022&DEV_15E2";
    private const string Nvidia =
        @"PCI\VEN_10DE&DEV_28A0";

    private static DriverInfo Device(string id) =>
        new("PRIVATE_PNP_INSTANCE", "Windows audio",
            "Microsoft", "MEDIA", "Microsoft", "1.0",
            null, "PRIVATE_INF.inf", true, true, 0, [id], []);

    private static string[] Prefix(int length) =>
        Enumerable.Range(1, length)
            .Select(i => $@"PCI\VEN_9999&DEV_{i:X4}")
            .ToArray();

    [Fact]
    public void RealComReaderKeepsLateSndwAndAcpFromLargeWuaEntryCollection()
    {
        var input = Prefix(1760).Concat(
            [Sndw, Acp, Nvidia]).ToArray();
        var read = WindowsDriverEntryIdReader.ReadAdditionalIdsWithCoverage(
            new WindowsDriverEntryIdReaderTests.FakeUpdate(input), null);

        Assert.True(read.Truncated);
        Assert.Equal(1024, read.AdditionalHardwareIds.Count);
        Assert.Equal(input.Take(960),
            read.AdditionalHardwareIds.Take(960));
        Assert.Contains(Sndw, read.AdditionalHardwareIds);
        Assert.Contains(Acp, read.AdditionalHardwareIds);
        Assert.Contains(Nvidia, read.AdditionalHardwareIds);
        Assert.Equal(1024, read.AdditionalHardwareIds
            .Distinct(StringComparer.OrdinalIgnoreCase).Count());

        // These are exact COM-declared model IDs; no package metadata or
        // driver update GUID is synthesized by the reader.
        var candidate = new DriverUpdateInfo(
            "00000000-0000-4000-8000-000000000356",
            "Windows driver", null, "MEDIA", "Microsoft", null,
            null, false, "Microsoft", null, null, null,
            SearchSource: "Windows Update online",
            AdditionalHardwareIds: read.AdditionalHardwareIds,
            AdditionalHardwareIdsIncomplete: read.Truncated);
        Assert.Equal(2, DriverUpdateMatcher.MatchStrength(
            Device(Sndw), candidate));
        Assert.Equal(2, DriverUpdateMatcher.MatchStrength(
            Device(Acp), candidate));
        Assert.True(candidate.AdditionalHardwareIdsIncomplete);
    }

    [Fact]
    public void GenericAndMalformedModernAudioComIdsCannotConsumeFairSlots()
    {
        var invalid = new[]
        {
            @"SNDW\CLASS_00",
            @"SNDW\CLASS_00&MAN_025D",
            @"SNDW\CLASS_00&MAN_025D&PART_0711&FAKE_00",
            @"ACP\DEVTYPE_0005",
            @"ACP\DEVTYPE_0005&DEVREV_0000&VEN_1022",
            @"ACP\DEVTYPE_0005&DEVREV_0000&VEN_1022&DEV_15E2\PRIVATE",
            "SNDW\\CLASS_00&MAN_025D&PART_0711\nPRIVATE",
            @"C:\PRIVATE_HOST\driver.inf"
        };
        var read = WindowsDriverEntryIdReader.ReadAdditionalIdsWithCoverage(
            new WindowsDriverEntryIdReaderTests.FakeUpdate(
                Prefix(1680).Concat(invalid).Concat(
                    [Sndw, Acp]).ToArray()), null);

        Assert.Equal(1024, read.AdditionalHardwareIds.Count);
        Assert.True(read.Truncated);
        Assert.All(invalid, id =>
            Assert.DoesNotContain(id, read.AdditionalHardwareIds));
        Assert.Contains(Sndw, read.AdditionalHardwareIds);
        Assert.Contains(Acp, read.AdditionalHardwareIds);
        Assert.DoesNotContain(read.AdditionalHardwareIds,
            id => id.Contains("PRIVATE", StringComparison.Ordinal));
        Assert.False(DriverUpdateMatcher.IsCorrelatableHardwareId(
            @"SNDW\CLASS_00&MAN_025D"));
        Assert.False(DriverUpdateMatcher.IsCorrelatableHardwareId(
            @"ACP\DEVTYPE_0005&DEVREV_0000"));
    }

    [Fact]
    public void SampledVeryLongComListPreservesAudioWithoutClaimingCompleteness()
    {
        var ids = Prefix(5100);
        ids[2047] = Sndw;
        ids[^1] = Acp;
        var result = WindowsDriverEntryIdReader.ReadAdditionalIdsWithCoverage(
            new WindowsDriverEntryIdReaderTests.FakeUpdate(ids), null);

        Assert.True(result.Truncated);
        Assert.Equal(1024, result.AdditionalHardwareIds.Count);
        Assert.Contains(Sndw, result.AdditionalHardwareIds);
        Assert.Contains(Acp, result.AdditionalHardwareIds);
        Assert.DoesNotContain(result.AdditionalHardwareIds,
            id => id.Contains("PRIVATE", StringComparison.Ordinal));
    }

    [Fact]
    public void SmallComEntryListRetainsOriginalOrderAndCompleteStatus()
    {
        var input = new[] { @"SNDW\CLASS_00", Sndw, Acp };
        var result = WindowsDriverEntryIdReader.ReadAdditionalIdsWithCoverage(
            new WindowsDriverEntryIdReaderTests.FakeUpdate(input), null);

        Assert.Equal(input, result.AdditionalHardwareIds);
        Assert.False(result.Truncated);
        // A generic ID may be retained for diagnostics in a small list,
        // but cannot correlate to a codec or authorize installation.
        Assert.False(DriverUpdateMatcher.IsCorrelatableHardwareId(
            result.AdditionalHardwareIds[0]));
    }
}
