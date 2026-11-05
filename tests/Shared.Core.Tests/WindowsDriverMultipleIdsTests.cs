using System.Text.Json;
using System.Text.Json.Nodes;
using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;
using TheEasyWayForDrivers.Core.Update;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

public sealed class WindowsDriverMultipleIdsTests
{
    private static DriverInfo Device(string hardwareId, string? compatibleId = null) =>
        new(hardwareId + @"\instance", "Generic Windows device", "Microsoft",
            "Net", "Microsoft", "1.0", null, null, true, true, 0,
            [hardwareId], compatibleId is null ? [] : [compatibleId]);

    private static DriverUpdateInfo Update(string primary,
        IReadOnlyList<string>? additional = null, bool advanced = false) =>
        new(Guid.NewGuid().ToString("D"), "Windows Update driver",
            null, "Net", "Intel", null, null, false, "Intel", null,
            primary, null, IsAdvancedCandidate: advanced,
            SearchSource: "Windows Update online",
            AdditionalHardwareIds: additional);

    [Fact]
    public void AlternativeHardwareEntryCanAssociateExistingUpdateWithOtherDevice()
    {
        var wua = Update(@"PCI\VEN_8086&DEV_1111",
            [@"PCI\VEN_8086&DEV_51F1", @"USB\VID_8087&PID_0033"]);
        Assert.Equal(2, DriverUpdateMatcher.MatchStrength(
            Device(@"PCI\VEN_8086&DEV_51F1"), wua));
        Assert.Equal(2, DriverUpdateMatcher.MatchStrength(
            Device(@"USB\VID_8087&PID_0033"), wua));
        Assert.Equal(0, DriverUpdateMatcher.MatchStrength(
            Device(@"PCI\VEN_10DE&DEV_28A0"), wua));
    }

    [Fact]
    public void ExactAlternativeOutranksCompatiblePrimary()
    {
        var wua = Update(@"PCI\VEN_8086&DEV_1234",
            [@"PCI\VEN_8086&DEV_1234&SUBSYS_00018086"]);
        var actual = Device(
            @"PCI\VEN_8086&DEV_1234&SUBSYS_00018086",
            @"PCI\VEN_8086&DEV_1234");

        Assert.Equal(2, DriverUpdateMatcher.MatchStrength(actual, wua));
    }

    [Fact]
    public void NoPrefixGuessingOrNameGuessingForMultipleEntries()
    {
        var wua = Update(@"PCI\VEN_8086&DEV_1234",
            [@"PCI\VEN_10DE&DEV_28A0"]);
        Assert.Equal(0, DriverUpdateMatcher.MatchStrength(
            Device(@"PCI\VEN_10DE0&DEV_28A0"), wua));
        Assert.Equal(0, DriverUpdateMatcher.MatchStrength(
            Device(@"PCI\VEN_80860&DEV_1234"), wua));
        Assert.Equal(0, DriverUpdateMatcher.MatchStrength(
            Device(@"USB\VID_8087&PID_0034"), wua));
    }

    [Fact]
    public void DedupedUpdatePreservesDeviceEntriesFromDifferentWuaPasses()
    {
        var baseUpdate = Update(@"PCI\VEN_8086&DEV_1111",
            [@"PCI\VEN_8086&DEV_51F1"], advanced: true);
        var sameUpdate = baseUpdate with
        {
            Title = "Recommended",
            HardwareId = @"USB\VID_8087&PID_0033",
            AdditionalHardwareIds = [@"PCI\VEN_8086&DEV_1111"],
            IsAdvancedCandidate = false
        };

        var destination = new Dictionary<string, DriverUpdateInfo>(
            StringComparer.OrdinalIgnoreCase);
        Assert.Equal(1, WindowsDriverCandidateMerge.Merge(
            destination, [baseUpdate]));
        Assert.Equal(0, WindowsDriverCandidateMerge.Merge(
            destination, [sameUpdate]));

        var merged = Assert.Single(destination.Values);
        Assert.Equal("Recommended", merged.Title);
        Assert.False(merged.IsAdvancedCandidate);
        Assert.Equal(sameUpdate.HardwareId, merged.HardwareId);
        Assert.Equal(2, DriverUpdateMatcher.MatchStrength(
            Device(@"PCI\VEN_8086&DEV_51F1"), merged));
        Assert.Equal(2, DriverUpdateMatcher.MatchStrength(
            Device(@"PCI\VEN_8086&DEV_1111"), merged));
        Assert.Equal(2, DriverUpdateMatcher.MatchStrength(
            Device(@"USB\VID_8087&PID_0033"), merged));
        Assert.Equal(1, QuickUpdatePolicy.AllOrdinaryIds(destination.Values).Count);
    }

    [Fact]
    public void IdenticalGuidAcrossSourcesStillCountsAsOneDriverUpdate()
    {
        var baseUpdate = Update(@"PCI\VEN_10DE&DEV_28A0");
        var fromOtherPass = baseUpdate with
        {
            HardwareId = @"HDAUDIO\FUNC_01&VEN_10DE&DEV_009B",
            SearchSource = "Microsoft Update online"
        };
        var destination = new Dictionary<string, DriverUpdateInfo>(
            StringComparer.OrdinalIgnoreCase);
        Assert.Equal(1, WindowsDriverCandidateMerge.Merge(destination, [baseUpdate]));
        Assert.Equal(0, WindowsDriverCandidateMerge.Merge(destination, [fromOtherPass]));
        Assert.Single(destination);
        Assert.Equal(2, DriverUpdateMatcher.MatchStrength(
            Device(@"HDAUDIO\FUNC_01&VEN_10DE&DEV_009B"), destination[baseUpdate.Id]));
    }

    [Fact]
    public void OptionalIdsSerializeAcrossIpcAndOlderPayloadsStillLoad()
    {
        var newer = Update(@"PCI\VEN_8086&DEV_1234",
            [@"PCI\VEN_8086&DEV_51F1"]);
        var serialized = JsonSerializer.Serialize(newer);
        var restored = JsonSerializer.Deserialize<DriverUpdateInfo>(serialized);
        Assert.NotNull(restored);
        Assert.Equal(2, DriverUpdateMatcher.MatchStrength(
            Device(@"PCI\VEN_8086&DEV_51F1"), restored));

        // Previous Desktop/Service payloads lacked AdditionalHardwareIds.
        var legacyPayload = JsonNode.Parse(serialized)!.AsObject();
        Assert.True(legacyPayload.Remove(nameof(DriverUpdateInfo.AdditionalHardwareIds)));
        var older = legacyPayload.ToJsonString();
        var restoredOld = JsonSerializer.Deserialize<DriverUpdateInfo>(older);
        Assert.NotNull(restoredOld);
        Assert.Null(restoredOld.AdditionalHardwareIds);
        Assert.Equal(2, DriverUpdateMatcher.MatchStrength(
            Device(@"PCI\VEN_8086&DEV_1234"), restoredOld));
    }

    [Fact]
    public void PrivacySafeCoverageReportCountsCorrelationsNotHardwareIds()
    {
        var update = Update(@"PCI\VEN_8086&DEV_1111",
            [@"PCI\VEN_8086&DEV_51F1"]);
        var report = new DriverSearchReport([update],
            [new DriverSearchPassStatus("Windows Update online", "Completed", 1, "OK", 1)]);
        var devices = new[]
        {
            Device(@"PCI\VEN_8086&DEV_1111"),
            Device(@"PCI\VEN_8086&DEV_51F1")
        };

        var coverage = ScanCoverageText.Create(report, [], 2, 0, 0,
            devices, inventoryComplete: true);
        Assert.Contains("Windows Update: 1 candidati distinti", coverage);
        Assert.DoesNotContain(@"PCI\VEN", coverage);
        Assert.DoesNotContain("DEV_51F1", coverage);
    }
}
