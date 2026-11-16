using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;
using TheEasyWayForDrivers.Core.Update;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

/// <summary>
/// #537: Discovery+ validates bounded legacy IPC WUA entries BEFORE
/// allocating vendor-fair display slots. Late PnP identities are
/// diagnostic evidence only, not permission to install an OEM driver.
/// </summary>
public sealed class DiscoveryWuaLateModelFairSelectionTests
{
    private const string UpdateGuid =
        "00000000-0000-4000-8000-000000000537";
    private const string Primary = @"PCI\VEN_9999&DEV_0000";
    private const string Nvidia = @"PCI\VEN_10DE&DEV_28A0";
    private const string Killer = @"PCI\VEN_8086&DEV_51F1&SUBSYS_16721A56";
    private const string Realtek =
        @"HDAUDIO\FUNC_01&VEN_10EC&DEV_0295";

    private static DriverUpdateInfo Update(IReadOnlyList<string> ids) =>
        new(UpdateGuid, "Windows Update driver", null, "Display", "Microsoft",
            "31.0.0.0", null, false, "Microsoft", null, Primary, null,
            AdditionalHardwareIds: ids);

    private static DriverInfo Device(string identity, string kind) =>
        new("PRIVATE_INSTANCE_" + kind, "Generic Windows device", "Microsoft",
            kind, "Windows", "31.0.0.0", null, null,
            true, true, 0, [identity], []);

    [Fact]
    public void LateNvidiaKillerAndRealtekSurviveLegacy1024FifoBoundary()
    {
        var filler = Enumerable.Range(1, 1600)
            .Select(i => $@"PCI\VEN_9999&DEV_{i:X4}");
        var rawIds = filler.Concat([Nvidia, Killer, Realtek]).ToArray();
        var row = Assert.Single(DiscoveryResultAdapter.FromWindowsUpdate(
            [Update(rawIds)]));

        Assert.Equal(UpdateGuid, row.Identifier);
        Assert.NotNull(row.AdditionalHardwareIds);
        Assert.Equal(WuaDriverEntryLimits.MaximumAdditionalIds,
            row.AdditionalHardwareIds.Count);
        Assert.Equal(rawIds.Take(960), row.AdditionalHardwareIds.Take(960));
        Assert.Contains(Nvidia, row.AdditionalHardwareIds);
        Assert.Contains(Killer, row.AdditionalHardwareIds);
        Assert.Contains(Realtek, row.AdditionalHardwareIds);
        Assert.True(row.WuaIdsIncomplete);
        Assert.Contains("incompleto", row.Evidence);
        Assert.DoesNotContain("PRIVATE_", row.Evidence);
        Assert.Equal(2, DiscoveryDeviceEvidence.MatchStrength(
            row, Device(Nvidia, "Display")));
        Assert.Equal(2, DiscoveryDeviceEvidence.MatchStrength(
            row, Device(Killer, "Net")));
        Assert.Equal(2, DiscoveryDeviceEvidence.MatchStrength(
            row, Device(Realtek, "MEDIA")));
        Assert.False(DiscoveryDeviceEvidence.IsDeviceCandidate(
            row, Device(@"PCI\VEN_9999&DEV_0700", "Display")));
    }

    [Fact]
    public void InvalidIdsAndDuplicatePrimaryCannotConsumePrioritySlots()
    {
        var filler = Enumerable.Range(1, 1400)
            .Select(i => $@"PCI\VEN_9999&DEV_{i:X4}");
        var invalid = Enumerable.Range(1, 110)
            .Select(i => $@"PCI\VEN_10DE&FAKE_{i:X4}");
        var inputs = filler.Concat(invalid).Concat([
            Primary, Primary.ToLowerInvariant(),
            @"C:\Users\PRIVATE_ACCOUNT\drivers.inf",
            Nvidia, Killer, Realtek
        ]).ToArray();
        var row = Assert.Single(DiscoveryResultAdapter.FromWindowsUpdate(
            [Update(inputs)]));

        Assert.NotNull(row.AdditionalHardwareIds);
        Assert.Equal(1024, row.AdditionalHardwareIds.Count);
        Assert.Contains(Nvidia, row.AdditionalHardwareIds);
        Assert.Contains(Killer, row.AdditionalHardwareIds);
        Assert.Contains(Realtek, row.AdditionalHardwareIds);
        Assert.DoesNotContain(Primary, row.AdditionalHardwareIds);
        Assert.DoesNotContain(row.AdditionalHardwareIds,
            id => id.Contains("FAKE_", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain("PRIVATE_", row.Evidence);
        Assert.True(row.WuaIdsIncomplete);
        Assert.Equal(row.AdditionalHardwareIds.Count,
            row.AdditionalHardwareIds.Distinct(
                StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void MoreThan2560RawEntriesNeverEscapeInspectionBudget()
    {
        var raw = Enumerable.Range(1,
            WuaDriverEntryLimits.MaximumEntriesToInspect + 100)
            .Select(i => $@"PCI\VEN_9999&DEV_{i:X4}")
            .Append(Nvidia)
            .ToArray();
        var row = Assert.Single(DiscoveryResultAdapter.FromWindowsUpdate(
            [Update(raw)]));
        Assert.True(row.WuaIdsIncomplete);
        Assert.NotNull(row.AdditionalHardwareIds);
        Assert.Equal(WuaDriverEntryLimits.MaximumAdditionalIds,
            row.AdditionalHardwareIds.Count);
        Assert.DoesNotContain(Nvidia, row.AdditionalHardwareIds);
        Assert.Equal(0, DiscoveryDeviceEvidence.MatchStrength(
            row, Device(Nvidia, "Display")));
        Assert.Contains("assenza non verificabile", row.Evidence);
    }

    [Fact]
    public void WithinTransportCapPreservesOrderAndCompleteness()
    {
        var raw = Enumerable.Range(1, 1024)
            .Select(i => $@"PCI\VEN_9999&DEV_{i:X4}").ToArray();
        var row = Assert.Single(DiscoveryResultAdapter.FromWindowsUpdate(
            [Update(raw)]));
        Assert.NotNull(row.AdditionalHardwareIds);
        Assert.Equal(raw, row.AdditionalHardwareIds);
        Assert.False(row.WuaIdsIncomplete);
        Assert.Equal(UpdateGuid, row.Identifier);
    }

    [Fact]
    public void NoValidHardwareIdIsInventedFromLateMalformedTokens()
    {
        var raw = Enumerable.Range(1, 1100)
            .Select(i => $@"PCI\VEN_9999&DEV_{i:X4}")
            .Concat([
                @"PCI\VEN_10DE0&DEV_28A0",
                @"PCI\VEN_80860&DEV_51F1",
                @"HDAUDIO\FUNC_01&VEN_10EC0&DEV_0295"
            ]).ToArray();
        var row = Assert.Single(DiscoveryResultAdapter.FromWindowsUpdate(
            [Update(raw)]));

        Assert.True(row.WuaIdsIncomplete);
        Assert.NotNull(row.AdditionalHardwareIds);
        Assert.DoesNotContain(row.AdditionalHardwareIds,
            id => id.Contains("VEN_10DE0", StringComparison.Ordinal));
        Assert.False(DiscoveryDeviceEvidence.IsDeviceCandidate(
            row, Device(Nvidia, "Display")));
        Assert.False(DiscoveryDeviceEvidence.IsDeviceCandidate(
            row, Device(Killer, "Net")));
        Assert.False(DiscoveryDeviceEvidence.IsDeviceCandidate(
            row, Device(Realtek, "MEDIA")));
    }
}
