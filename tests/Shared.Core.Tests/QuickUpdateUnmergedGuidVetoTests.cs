using TheEasyWayForDrivers.Core.Models;
using TheEasyWayForDrivers.Core.Update;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

/// <summary>
/// Update All accepts legacy/raw multi-pass snapshots as well as merged WUA
/// results. It must never allow two representations of the same GUID, or
/// discard trusted BrowseOnly/firmware evidence before deciding eligibility.
/// </summary>
public sealed class QuickUpdateUnmergedGuidVetoTests
{
    private const string GuidOne = "00000000-0000-4000-8000-000000000367";
    private const string GuidTwo = "00000000-0000-4000-8000-000000000368";

    private static DriverUpdateInfo Offer(
        string id, string source = "Windows Update online",
        bool optional = false, bool advanced = false,
        string title = "Generic Windows driver") =>
        new(id, title, null, "Display", "Microsoft",
            null, null, false, "Microsoft", null,
            @"PCI\VEN_10DE&DEV_28A0", null,
            IsOptional: optional,
            IsAdvancedCandidate: advanced,
            SearchSource: source);

    [Fact]
    public void AllGuidFormatsReturnOneCanonicalWuaIdAndPreserveOrder()
    {
        var guid = Guid.Parse(GuidOne);
        var rows = new[]
        {
            Offer(guid.ToString("B").ToUpperInvariant()),
            Offer(GuidTwo),
            Offer(guid.ToString("N")),
            Offer(guid.ToString("P")),
            Offer(GuidOne.ToUpperInvariant()),
            Offer("non-guid-private-id"),
            Offer("{" + GuidTwo + "}")
        };
        Assert.Equal([GuidOne, GuidTwo],
            QuickUpdatePolicy.AllOrdinaryIds(rows));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TrustedBrowseOnlyVetoIsOrderIndependentEvenWithoutMerge(
        bool optionalFirst)
    {
        var guid = Guid.Parse(GuidOne);
        var recommended = Offer(guid.ToString("B"),
            "Windows Update online");
        var browseOnly = Offer(guid.ToString("N"),
            "Microsoft Update online", optional: true);
        var rows = optionalFirst
            ? new[] { browseOnly, recommended }
            : [recommended, browseOnly];

        Assert.Empty(QuickUpdatePolicy.AllOrdinaryIds(rows));

        // The already-merged normal pipeline must make the same decision.
        var merged = new Dictionary<string, DriverUpdateInfo>(
            StringComparer.OrdinalIgnoreCase);
        Assert.Equal(1, WindowsDriverCandidateMerge.Merge(merged, rows));
        Assert.True(Assert.Single(merged.Values).IsOptional);
        Assert.Empty(QuickUpdatePolicy.AllOrdinaryIds(merged.Values));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TrustedFirmwareMetadataVetoIsOrderIndependent(
        bool firmwareFirst)
    {
        var ordinary = Offer(GuidOne);
        var firmware = Offer("{" + GuidOne + "}",
            "Microsoft Update online", title: "UEFI Firmware Update");
        var rows = firmwareFirst
            ? new[] { firmware, ordinary }
            : [ordinary, firmware];

        Assert.Empty(QuickUpdatePolicy.AllOrdinaryIds(rows));
        var merged = new Dictionary<string, DriverUpdateInfo>(
            StringComparer.OrdinalIgnoreCase);
        WindowsDriverCandidateMerge.Merge(merged, rows);
        Assert.Empty(QuickUpdatePolicy.AllOrdinaryIds(merged.Values));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void FakeOemFirmwareOrOptionalFlagsCannotPoisonRealWuaGuid(
        bool untrustedFirst)
    {
        var valid = Offer(GuidOne);
        var fake = Offer(Guid.Parse(GuidOne).ToString("P"),
            "Unverified OEM website", optional: true,
            title: "OEM BIOS Firmware PRIVATE_SERIAL");
        var rows = untrustedFirst ? new[] { fake, valid }
            : [valid, fake];

        Assert.Equal([GuidOne], QuickUpdatePolicy.AllOrdinaryIds(rows));
        var merged = new Dictionary<string, DriverUpdateInfo>(
            StringComparer.OrdinalIgnoreCase);
        WindowsDriverCandidateMerge.Merge(merged, rows);
        Assert.Equal([GuidOne],
            QuickUpdatePolicy.AllOrdinaryIds(merged.Values));
    }

    [Fact]
    public void AdvancedOnlyIsNeverBulkInstallableAndTrustedAdvancedVetoPersists()
    {
        var advanced = Offer(Guid.Parse(GuidOne).ToString("N"),
            "Windows Update online · avanzato", advanced: true);
        Assert.Empty(QuickUpdatePolicy.AllOrdinaryIds([advanced]));

        var recommended = Offer(GuidOne);
        Assert.Equal([GuidOne],
            QuickUpdatePolicy.AllOrdinaryIds([advanced, recommended]));

        var optionalAdvanced = advanced with { IsOptional = true };
        Assert.Empty(QuickUpdatePolicy.AllOrdinaryIds(
            [recommended, optionalAdvanced]));
    }

    [Fact]
    public void UnknownOriginAndNonGuidRowsNeverAddAnUpdate()
    {
        var foreignOnly = Offer(GuidOne,
            "Unverified OEM driver catalog");
        var forged = Offer("PRIVATE_SERIAL_NOT_A_GUID",
            "Windows Update online");
        var invalid = Offer("https://example.org/driver.exe",
            "Microsoft Update online");

        Assert.Empty(QuickUpdatePolicy.AllOrdinaryIds(
            [foreignOnly, forged, invalid]));
        Assert.Equal([GuidTwo], QuickUpdatePolicy.AllOrdinaryIds(
            [foreignOnly, forged, invalid, Offer(GuidTwo)]));
    }

    [Fact]
    public void DistinctTrustedGuidsRemainInstallableWhenOnlyOtherGuidIsOptional()
    {
        var items = new[]
        {
            Offer(GuidOne),
            Offer(GuidTwo, optional: true),
            Offer(Guid.Parse(GuidOne).ToString("B"),
                "Microsoft Update online")
        };
        Assert.Equal([GuidOne], QuickUpdatePolicy.AllOrdinaryIds(items));
    }
}
