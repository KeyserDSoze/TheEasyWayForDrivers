using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;
using TheEasyWayForDrivers.Core.Update;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

/// <summary>
/// A single real WUA GUID may be formatted as D, B, N or P by different
/// sources. These are NOT four drivers. Preserve source trust, BrowseOnly
/// vetoes and all independently declared exact PnP identifiers.
/// </summary>
public sealed class WindowsDriverGuidCanonicalMergeTests
{
    private const string GuidD = "00000000-0000-4000-8000-000000000363";
    private const string Nvidia = @"PCI\VEN_10DE&DEV_28A0";
    private const string Killer = @"PCI\VEN_8086&DEV_51F1";
    private const string Realtek = @"HDAUDIO\FUNC_01&VEN_10EC&DEV_0295";

    private static DriverUpdateInfo Offer(
        string id, string hardware,
        string source = "Windows Update online",
        bool optional = false) =>
        new(id, "Windows driver", null, "Display", "Microsoft",
            null, null, false, "Microsoft", null, hardware, null,
            IsOptional: optional, SearchSource: source);

    private static DriverInfo Device(string id) =>
        new("PRIVATE_DEVICE_INSTANCE", "Generic Windows hardware",
            "Microsoft", "Display", "Microsoft", "1.0", null,
            "oem12.inf", true, true, 0, [id], []);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void EquivalentWuaGuidsMergeOnceAndBrowseOnlyVetoSurvives(
        bool browseOnlyFirst)
    {
        var guid = Guid.Parse(GuidD);
        var recommended = Offer(guid.ToString("B").ToUpperInvariant(),
            Nvidia, "Windows Update online");
        var optional = Offer(guid.ToString("N"),
            Killer, "Microsoft Update online", optional: true);
        var passOrder = browseOnlyFirst
            ? new[] { optional, recommended }
            : [recommended, optional];
        var rows = new Dictionary<string, DriverUpdateInfo>(
            StringComparer.OrdinalIgnoreCase);

        Assert.Equal(1, WindowsDriverCandidateMerge.Merge(rows, passOrder));
        var merged = Assert.Single(rows).Value;

        Assert.True(rows.ContainsKey(GuidD));
        Assert.Equal(GuidD, merged.Id);
        Assert.Contains(Killer, new[] { merged.HardwareId }
            .Concat(merged.AdditionalHardwareIds ?? []));
        Assert.Contains(Nvidia, new[] { merged.HardwareId }
            .Concat(merged.AdditionalHardwareIds ?? []));
        Assert.True(merged.IsOptional);
        Assert.False(QuickUpdatePolicy.IsEligible(merged));
        Assert.Empty(QuickUpdatePolicy.AllOrdinaryIds(rows.Values));
        Assert.Equal(2, DriverUpdateMatcher.MatchStrength(
            Device(merged.HardwareId!), merged));
        // Both devices are represented by ONE record, never two packages.
        Assert.Equal(2, DriverUpdateMatcher.MatchStrength(
            Device(Nvidia), merged));
        Assert.Equal(2, DriverUpdateMatcher.MatchStrength(
            Device(Killer), merged));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void UnknownOriginEquivalentGuidCannotContaminateVerifiedWua(
        bool untrustedFirst)
    {
        var untrusted = Offer("{" + GuidD + "}",
            @"PCI\VEN_9999&DEV_1000", "Unverified OEM website");
        var valid = Offer("(" + GuidD + ")", Nvidia,
            "Windows Update online");
        var input = untrustedFirst ? new[] { untrusted, valid }
            : [valid, untrusted];
        var rows = new Dictionary<string, DriverUpdateInfo>(
            StringComparer.OrdinalIgnoreCase);
        Assert.Equal(1, WindowsDriverCandidateMerge.Merge(rows, input));

        var merged = Assert.Single(rows).Value;
        Assert.Equal(GuidD, merged.Id);
        Assert.Equal(Nvidia, merged.HardwareId);
        Assert.DoesNotContain(@"PCI\VEN_9999&DEV_1000",
            merged.AdditionalHardwareIds ?? []);
        Assert.True(QuickUpdatePolicy.IsEligible(merged));
        Assert.Single(QuickUpdatePolicy.AllOrdinaryIds(rows.Values));
    }

    [Fact]
    public void SameGuidAcrossThreeFormatsKeepsSecondaryRealtekEvidence()
    {
        var rows = new Dictionary<string, DriverUpdateInfo>(
            StringComparer.OrdinalIgnoreCase);
        var guid = Guid.Parse(GuidD);
        var passOne = Offer(guid.ToString("P"), Nvidia) with
        {
            AdditionalHardwareIds = [Realtek]
        };
        var passTwo = Offer(guid.ToString("N"), Killer,
            "Microsoft Update online");

        Assert.Equal(1, WindowsDriverCandidateMerge.Merge(rows,
            [passOne, passTwo]));
        var record = Assert.Single(rows).Value;
        Assert.Equal(GuidD, record.Id);
        Assert.Contains(Realtek, record.AdditionalHardwareIds ?? []);
        Assert.Equal(2, DriverUpdateMatcher.MatchStrength(
            Device(Realtek), record));
        Assert.Equal(2, DriverUpdateMatcher.MatchStrength(
            Device(Killer), record));
        Assert.Equal(1, rows.Count);
    }

    [Fact]
    public void DistinctGuidsAndLegacySyntheticKeysStayDistinct()
    {
        var another = "00000000-0000-4000-8000-000000000364";
        var rows = new Dictionary<string, DriverUpdateInfo>(
            StringComparer.OrdinalIgnoreCase);

        Assert.Equal(3, WindowsDriverCandidateMerge.Merge(rows,
        [
            Offer(GuidD, Nvidia),
            Offer("{" + another + "}", Killer),
            Offer("legacy-synthetic-id", Realtek)
        ]));
        Assert.Equal(3, rows.Count);
        Assert.Equal(0, WindowsDriverCandidateMerge.Merge(rows,
            [Offer(Guid.Parse(GuidD).ToString("N"), Nvidia),
             Offer(another, Killer)]));
        Assert.Contains("legacy-synthetic-id", rows.Keys);
        Assert.Equal("legacy-synthetic-id",
            rows["legacy-synthetic-id"].Id);
        Assert.Contains(another, rows.Keys);
        Assert.DoesNotContain("{" + another + "}", rows.Keys);
        Assert.False(QuickUpdatePolicy.IsEligible(
            rows["legacy-synthetic-id"]));
    }
}
