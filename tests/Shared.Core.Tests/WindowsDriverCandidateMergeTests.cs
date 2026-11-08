using TheEasyWayForDrivers.Core.Models;
using TheEasyWayForDrivers.Core.Update;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

public sealed class WindowsDriverCandidateMergeTests
{
    private static DriverUpdateInfo Candidate(
        string id, string title, bool advanced = false) =>
        new(id, title, null, "Display", "NVIDIA", null, null, false,
            "NVIDIA", "GeForce RTX", @"PCI\VEN_10DE&DEV_28A0", null,
            false, false, advanced);

    [Fact]
    public void DifferentSourcesCannotInflateUniqueUpdateCount()
    {
        var updates = new Dictionary<string, DriverUpdateInfo>(
            StringComparer.OrdinalIgnoreCase);
        Assert.Equal(2, WindowsDriverCandidateMerge.Merge(updates,
        [
            Candidate("nvidia-1", "NVIDIA GPU"),
            Candidate("intel-1", "Intel WiFi")
        ]));
        Assert.Equal(1, WindowsDriverCandidateMerge.Merge(updates,
        [
            Candidate("NVIDIA-1", "duplicate NVIDIA"),
            Candidate("amd-1", "AMD graphics")
        ]));
        Assert.Equal(3, updates.Count);
        Assert.Equal("NVIDIA GPU", updates["nvidia-1"].Title);
    }

    [Fact]
    public void RecommendedResultOverridesAdvancedResultWithoutExtraCount()
    {
        var updates = new Dictionary<string, DriverUpdateInfo>(
            StringComparer.OrdinalIgnoreCase);
        Assert.Equal(1, WindowsDriverCandidateMerge.Merge(updates,
            [Candidate("same", "Advanced", advanced: true)]));
        Assert.Equal(0, WindowsDriverCandidateMerge.Merge(updates,
            [Candidate("SAME", "Recommended")]));
        Assert.Equal("Recommended", updates["same"].Title);
        Assert.False(updates["same"].IsAdvancedCandidate);
        Assert.Equal(0, WindowsDriverCandidateMerge.Merge(updates,
            [Candidate("same", "New advanced", advanced: true)]));
        Assert.Equal("Recommended", updates["same"].Title);
    }

    private static DriverUpdateInfo RichCandidate(
        string guid, string primary, IReadOnlyList<string> ids,
        bool advanced = false) =>
        new(guid, "PRIVATE DRIVER TITLE", null, "Display", "Windows",
            null, null, false, "Windows", null, primary, null,
            IsAdvancedCandidate: advanced,
            AdditionalHardwareIds: ids);

    [Fact]
    public void LaterSourceSecondaryNvidiaKillerRealtekIdsSurviveFullFirstPass()
    {
        const string guid = "00000000-0000-4000-8000-000000000260";
        var first = Enumerable.Range(1, 1024)
            .Select(i => $@"PCI\VEN_9999&DEV_{i:X4}").ToArray();
        var nvidia = @"PCI\VEN_10DE&DEV_28A0&SUBSYS_16A217AA";
        var killer = @"PCI\VEN_8086&DEV_51F1";
        var realtek = @"HDAUDIO\FUNC_01&VEN_10EC&DEV_0295";
        var second = Enumerable.Range(1025, 1021)
            .Select(i => $@"PCI\VEN_9999&DEV_{i:X4}")
            .Concat([nvidia, killer, realtek]).ToArray();

        var updates = new Dictionary<string, DriverUpdateInfo>(
            StringComparer.OrdinalIgnoreCase);
        Assert.Equal(1, WindowsDriverCandidateMerge.Merge(updates,
            [RichCandidate(guid, @"PCI\VEN_9999&DEV_FFFE", first)]));
        Assert.Equal(0, WindowsDriverCandidateMerge.Merge(updates,
            [RichCandidate(guid.ToUpperInvariant(),
                @"PCI\VEN_9999&DEV_FFFD", second)]));

        var merged = Assert.Single(updates).Value;
        Assert.Equal(1024, merged.AdditionalHardwareIds!.Count);
        Assert.True(merged.AdditionalHardwareIdsIncomplete);
        Assert.Equal(@"PCI\VEN_9999&DEV_FFFE", merged.HardwareId);
        Assert.Equal(@"PCI\VEN_9999&DEV_FFFD", merged.AdditionalHardwareIds[0]);
        Assert.Contains(nvidia, merged.AdditionalHardwareIds);
        Assert.Contains(killer, merged.AdditionalHardwareIds);
        Assert.Contains(realtek, merged.AdditionalHardwareIds);
        Assert.Equal(1024, merged.AdditionalHardwareIds.Distinct(
            StringComparer.OrdinalIgnoreCase).Count());

        foreach (var id in new[] { nvidia, killer, realtek })
        {
            var device = new DriverInfo("PRIVATE_DEVICE", "Windows device",
                "Microsoft", "Display", "Microsoft", "1.0.0.0",
                null, "oem12.inf", true, true, 0, [id], []);
            Assert.Equal(2, TheEasyWayForDrivers.Core.Drivers.DriverUpdateMatcher
                .MatchStrength(device, merged));
        }
    }

    [Fact]
    public void AllSevenVendorsArePreservedWhenSecondPassIdsAreLate()
    {
        const string id = "00000000-0000-4000-8000-000000000261";
        var first = Enumerable.Range(1, 1024)
            .Select(i => $@"PCI\VEN_9999&DEV_{i:X4}").ToArray();
        var markers = new[]
        {
            @"PCI\VEN_10DE&DEV_28A0",
            @"PCI\VEN_8086&DEV_51F1",
            @"HDAUDIO\FUNC_01&VEN_10EC&DEV_0295",
            @"PCI\VEN_1002&DEV_164E",
            @"PCI\VEN_17CB&DEV_1103",
            @"PCI\VEN_14E4&DEV_43A0",
            @"PCI\VEN_14C3&DEV_0616"
        };
        var second = Enumerable.Range(1025, 1017)
            .Select(i => $@"PCI\VEN_9999&DEV_{i:X4}")
            .Concat(markers).ToArray();
        var entries = new Dictionary<string, DriverUpdateInfo>(
            StringComparer.OrdinalIgnoreCase);
        WindowsDriverCandidateMerge.Merge(entries,
        [
            RichCandidate(id, @"PCI\VEN_9999&DEV_FFFE", first)
        ]);
        WindowsDriverCandidateMerge.Merge(entries,
        [
            RichCandidate(id, @"PCI\VEN_9999&DEV_FFFD", second)
        ]);

        var updated = Assert.Single(entries).Value;
        Assert.All(markers, marker =>
            Assert.Contains(marker, updated.AdditionalHardwareIds!));
        Assert.True(updated.AdditionalHardwareIdsIncomplete);
        Assert.Single(entries);
    }

    [Fact]
    public void SeparateWuaPassesKeepLateNvidiaPlatformAndRealtekDchAssociations()
    {
        const string guid = "00000000-0000-4000-8000-000000000268";
        var first = Enumerable.Range(1, 1024)
            .Select(i => $@"PCI\VEN_9999&DEV_{i:X4}").ToArray();
        var platform = @"ACPI\NVDA0820";
        var nvidiaSwc = @"SWC\VEN_10DE&DEV_28A0";
        var realtekAudio = @"INTELAUDIO\FUNC_01&VEN_10EC&DEV_0295";
        var second = Enumerable.Range(1025, 1021)
            .Select(i => $@"PCI\VEN_8888&DEV_{i:X4}")
            .Concat([platform, nvidiaSwc, realtekAudio]).ToArray();
        var merged = new Dictionary<string, DriverUpdateInfo>(
            StringComparer.OrdinalIgnoreCase);

        Assert.Equal(1, WindowsDriverCandidateMerge.Merge(merged,
            [RichCandidate(guid, @"PCI\VEN_9999&DEV_0000",
                first, advanced: true)]));
        Assert.Equal(0, WindowsDriverCandidateMerge.Merge(merged,
            [RichCandidate(guid, @"PCI\VEN_8888&DEV_0000", second)]));
        var offer = Assert.Single(merged).Value;

        Assert.False(offer.IsAdvancedCandidate);
        Assert.True(offer.AdditionalHardwareIdsIncomplete);
        Assert.Equal(1024, offer.AdditionalHardwareIds?.Count);
        foreach (var id in new[] { platform, nvidiaSwc, realtekAudio })
        {
            Assert.Contains(id, offer.AdditionalHardwareIds!);
            var device = new DriverInfo("PRIVATE_INSTANCE", "Generic",
                "Microsoft", "System", "Microsoft", "1.0.0.0",
                null, "oem42.inf", true, true, 0, [id], []);
            Assert.Equal(2, TheEasyWayForDrivers.Core.Drivers.DriverUpdateMatcher
                .MatchStrength(device, offer));
        }
        // Multiple device associations must never create multiple update GUIDs.
        Assert.Single(merged);
    }

    [Fact]
    public void PreferredRecommendedMetadataWinsWhileAdvancedIdsSurvive()
    {
        const string id = "00000000-0000-4000-8000-000000000262";
        var advanced = RichCandidate(id,
            @"PCI\VEN_8086&DEV_51F1",
            [@"HDAUDIO\FUNC_01&VEN_10EC&DEV_0295"],
            advanced: true) with { Title = "PRIVATE ADVANCED" };
        var recommended = RichCandidate(id.ToUpperInvariant(),
            @"PCI\VEN_10DE&DEV_28A0",
            Enumerable.Range(1, 1024)
                .Select(i => $@"PCI\VEN_9999&DEV_{i:X4}").ToArray())
            with { Title = "PRIVATE RECOMMENDED" };

        var entries = new Dictionary<string, DriverUpdateInfo>(
            StringComparer.OrdinalIgnoreCase);
        WindowsDriverCandidateMerge.Merge(entries, [advanced]);
        WindowsDriverCandidateMerge.Merge(entries, [recommended]);
        var selected = Assert.Single(entries).Value;

        Assert.Equal("PRIVATE RECOMMENDED", selected.Title);
        Assert.False(selected.IsAdvancedCandidate);
        Assert.Equal(recommended.HardwareId, selected.HardwareId);
        Assert.Contains(advanced.HardwareId, selected.AdditionalHardwareIds!);
        Assert.Contains(advanced.AdditionalHardwareIds![0],
            selected.AdditionalHardwareIds!);
        Assert.True(selected.AdditionalHardwareIdsIncomplete);
        Assert.Single(entries);
    }

    [Fact]
    public void SmallCrossSourceMergePreservesExistingOrderingAndDedup()
    {
        var id = "00000000-0000-4000-8000-000000000263";
        var entries = new Dictionary<string, DriverUpdateInfo>(
            StringComparer.OrdinalIgnoreCase);
        var first = RichCandidate(id, @"PCI\VEN_9999&DEV_1000",
            [@"PCI\VEN_9999&DEV_1001", @"PCI\VEN_9999&DEV_1002"]);
        var second = RichCandidate(id, @"PCI\VEN_9999&DEV_2000",
            [@"PCI\VEN_9999&DEV_1002",
             @"PCI\VEN_9999&DEV_2001"]);
        WindowsDriverCandidateMerge.Merge(entries, [first, second]);
        var merged = Assert.Single(entries).Value;
        Assert.Equal(new[]
        {
            @"PCI\VEN_9999&DEV_2000",
            @"PCI\VEN_9999&DEV_1001",
            @"PCI\VEN_9999&DEV_1002",
            @"PCI\VEN_9999&DEV_2001"
        }, merged.AdditionalHardwareIds);
        Assert.False(merged.AdditionalHardwareIdsIncomplete);
    }

    [Fact]
    public void TwoMaximumSizedInputPassesStayInsideTransportAndMarkIncomplete()
    {
        const string id = "00000000-0000-4000-8000-000000000264";
        var first = Enumerable.Range(1, 2048)
            .Select(i => $@"PCI\VEN_9999&DEV_{i:X4}").ToArray();
        var second = Enumerable.Range(2049, 2048)
            .Select(i => $@"PCI\VEN_8888&DEV_{i:X4}").ToArray();
        second[^1] = @"PCI\VEN_10EC&DEV_8168";

        var entries = new Dictionary<string, DriverUpdateInfo>(
            StringComparer.OrdinalIgnoreCase);
        WindowsDriverCandidateMerge.Merge(entries,
            [RichCandidate(id, @"PCI\VEN_9999&DEV_FFFE", first)]);
        WindowsDriverCandidateMerge.Merge(entries,
            [RichCandidate(id, @"PCI\VEN_8888&DEV_FFFD", second)]);
        var output = Assert.Single(entries).Value;

        Assert.Equal(1024, output.AdditionalHardwareIds!.Count);
        Assert.True(output.AdditionalHardwareIdsIncomplete);
        Assert.Contains(@"PCI\VEN_10EC&DEV_8168",
            output.AdditionalHardwareIds);
        Assert.Contains(@"PCI\VEN_8888&DEV_FFFD",
            output.AdditionalHardwareIds);
    }

    [Fact]
    public void InvalidBlankIdsNeverCountAsNewUpdates()
    {
        var updates = new Dictionary<string, DriverUpdateInfo>(
            StringComparer.OrdinalIgnoreCase);
        Assert.Equal(1, WindowsDriverCandidateMerge.Merge(updates,
        [
            Candidate("", "Fake"),
            Candidate(" ", "Fake whitespace"),
            Candidate("VALID", "Valid")
        ]));
        Assert.Single(updates);
    }

    [Fact]
    public void NewMicrosoftUpdateDriverMayIncreaseCountButNeverFabricatesCandidates()
    {
        var updates = new Dictionary<string, DriverUpdateInfo>(
            StringComparer.OrdinalIgnoreCase);
        Assert.Equal(2, WindowsDriverCandidateMerge.Merge(updates,
        [
            Candidate("wu-intel", "Intel WiFi"),
            Candidate("wu-bt", "Intel Bluetooth")
        ]));
        Assert.Equal(0, WindowsDriverCandidateMerge.Merge(updates, []));
        Assert.Equal(1, WindowsDriverCandidateMerge.Merge(updates,
        [
            Candidate("wu-intel", "Duplicate"),
            Candidate("mu-nvidia", "NVIDIA GeForce RTX")
        ]));
        Assert.Equal(3, updates.Count); // Synthetic fixture, NOT a hardware claim.
    }
}
