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

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void OrdinaryWuaMetadataOutranksHiddenSameGuidInEitherArrivalOrder(
        bool hiddenFirst)
    {
        const string guid = "00000000-0000-4000-8000-000000000280";
        const string nvidia = @"PCI\VEN_10DE&DEV_28A0";
        const string intel = @"PCI\VEN_8086&DEV_46A6";
        var hidden = RichCandidate(guid, intel, []) with
        {
            Title = "Hidden WUA", IsHidden = true,
            SearchSource = "Windows configurato"
        };
        var ordinary = RichCandidate(guid, nvidia, []) with
        {
            Title = "Ordinary WUA", SearchSource = "Microsoft Update online"
        };
        var inputs = hiddenFirst
            ? new[] { hidden, ordinary } : [ordinary, hidden];
        var byGuid = new Dictionary<string, DriverUpdateInfo>(
            StringComparer.OrdinalIgnoreCase);

        Assert.Equal(1, WindowsDriverCandidateMerge.Merge(byGuid, inputs));
        var merged = Assert.Single(byGuid).Value;
        Assert.Equal("Ordinary WUA", merged.Title);
        Assert.Equal(nvidia, merged.HardwareId);
        Assert.Contains(intel, merged.AdditionalHardwareIds!);
        Assert.False(merged.IsHidden);
        Assert.True(QuickUpdatePolicy.IsEligible(merged));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void UnknownOriginNeverContributesPnpIdsToEligibleSameGuid(
        bool unknownFirst)
    {
        const string guid = "00000000-0000-4000-8000-000000000281";
        const string nvidia = @"PCI\VEN_10DE&DEV_28A0";
        const string untrustedId = @"PCI\VEN_9999&DEV_1234";
        var untrusted = RichCandidate(guid, untrustedId, []) with
        {
            Title = "Untrusted OEM",
            SearchSource = "Unverified website"
        };
        var ordinary = RichCandidate(guid, nvidia, []) with
        {
            Title = "Confirmed WUA", SearchSource = "Windows Update online"
        };
        var inputs = unknownFirst
            ? new[] { untrusted, ordinary } : [ordinary, untrusted];
        var byGuid = new Dictionary<string, DriverUpdateInfo>(
            StringComparer.OrdinalIgnoreCase);

        WindowsDriverCandidateMerge.Merge(byGuid, inputs);
        var merged = Assert.Single(byGuid).Value;
        Assert.Equal("Confirmed WUA", merged.Title);
        Assert.Equal(nvidia, merged.HardwareId);
        Assert.DoesNotContain(untrustedId, merged.AdditionalHardwareIds!);
        Assert.True(QuickUpdatePolicy.IsEligible(merged));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void FirmwareConflictCannotBePromotedBySameGuidOrdinaryTitle(
        bool firmwareFirst)
    {
        const string guid = "00000000-0000-4000-8000-000000000282";
        var firmware = RichCandidate(guid, @"PCI\VEN_10DE&DEV_28A0", [])
            with { Title = "NVIDIA Firmware", SearchSource = "Windows Update online" };
        var ordinary = firmware with
        {
            Title = "NVIDIA Display",
            SearchSource = "Microsoft Update online"
        };
        var inputs = firmwareFirst
            ? new[] { firmware, ordinary } : [ordinary, firmware];
        var byGuid = new Dictionary<string, DriverUpdateInfo>(
            StringComparer.OrdinalIgnoreCase);

        WindowsDriverCandidateMerge.Merge(byGuid, inputs);
        var merged = Assert.Single(byGuid).Value;
        Assert.Equal("NVIDIA Firmware", merged.Title);
        Assert.False(QuickUpdatePolicy.IsEligible(merged));
        Assert.Empty(QuickUpdatePolicy.AllOrdinaryIds(byGuid.Values));
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

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TrustedBrowseOnlyVetoPersistsAcrossOrdinaryWuaSourceOrder(
        bool optionalFirst)
    {
        const string guid = "00000000-0000-4000-8000-000000000331";
        const string gpu = @"PCI\VEN_10DE&DEV_28A0";
        const string killer = @"PCI\VEN_8086&DEV_51F1";
        var optional = RichCandidate(guid, killer, []) with
        {
            Title = "Intel Killer - Net - optional",
            SearchSource = "Windows Update online",
            IsOptional = true
        };
        var ordinary = RichCandidate(guid.ToUpperInvariant(), gpu, []) with
        {
            Title = "NVIDIA - Display - recommended",
            SearchSource = "Microsoft Update online",
            IsOptional = false
        };
        var records = optionalFirst
            ? new[] { optional, ordinary }
            : [ordinary, optional];
        var merged = new Dictionary<string, DriverUpdateInfo>(
            StringComparer.OrdinalIgnoreCase);
        Assert.Equal(1, WindowsDriverCandidateMerge.Merge(merged, records));
        var final = Assert.Single(merged).Value;

        Assert.Equal("NVIDIA - Display - recommended", final.Title);
        Assert.Equal(gpu, final.HardwareId);
        Assert.Contains(killer, final.AdditionalHardwareIds!);
        Assert.True(final.IsOptional);
        Assert.False(final.IsAdvancedCandidate);
        Assert.False(QuickUpdatePolicy.IsEligible(final));
        Assert.Empty(QuickUpdatePolicy.AllOrdinaryIds(merged.Values));
        Assert.True(CompactResultFilter.IsVisible(final, "optional"));
        Assert.False(CompactResultFilter.IsVisible(final, "recommended"));
        Assert.True(CompactResultFilter.IsVisible(final, "all"));
        var summary = DriverSearchSummary.Create(merged.Values, 0);
        Assert.Equal(0, summary.Recommended);
        Assert.Equal(1, summary.Optional);

        var device = new DriverInfo("PRIVATE_GPU_SERIAL", "Windows display",
            "Microsoft", "Display", "Microsoft", "1.0", null, "oem42.inf",
            true, true, 0, [gpu], []);
        var group = TheEasyWayForDrivers.Core.Drivers.DeviceWuaMatchGroups
            .Create(device, merged.Values);
        Assert.Empty(group.Ordinary);
        Assert.Equal(guid, Assert.Single(group.ReviewOnly).Id,
            ignoreCase: true);
        Assert.Empty(group.ModelOnlyReview!);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void BrowseOnlyOnAdvancedTrustedWuaPassCannotBeLost(
        bool advancedFirst)
    {
        const string guid = "00000000-0000-4000-8000-000000000332";
        var optionalAdvanced = RichCandidate(guid,
            @"HDAUDIO\FUNC_01&VEN_10DE&DEV_009B", [], advanced: true)
            with
            {
                SearchSource = "Windows Update online · avanzato",
                Title = "NVIDIA HD Audio optional",
                IsOptional = true
            };
        var ordinary = RichCandidate(guid,
            @"PCI\VEN_10DE&DEV_28A0", []) with
            {
                SearchSource = "Windows configurato",
                Title = "NVIDIA - Display - recommended"
            };
        var inputs = advancedFirst
            ? new[] { optionalAdvanced, ordinary }
            : [ordinary, optionalAdvanced];
        var merged = new Dictionary<string, DriverUpdateInfo>(
            StringComparer.OrdinalIgnoreCase);
        WindowsDriverCandidateMerge.Merge(merged, inputs);
        var final = Assert.Single(merged).Value;

        Assert.True(final.IsOptional);
        Assert.False(QuickUpdatePolicy.IsEligible(final));
        Assert.Equal(ordinary.Title, final.Title);
        Assert.Contains(optionalAdvanced.HardwareId,
            final.AdditionalHardwareIds!);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void UntrustedOptionalSourceCannotPoisonTrustedRecommendedGuid(
        bool untrustedFirst)
    {
        const string guid = "00000000-0000-4000-8000-000000000333";
        const string gpu = @"PCI\VEN_10DE&DEV_28A0";
        const string fakeId = @"PCI\VEN_9999&DEV_F00D";
        var ordinary = RichCandidate(guid, gpu, []) with
        {
            SearchSource = "Windows Update online",
            Title = "NVIDIA Display - confirmed"
        };
        var untrusted = RichCandidate(guid, fakeId, []) with
        {
            SearchSource = "Unverified OEM website",
            Title = "PRIVATE fake optional",
            IsOptional = true
        };
        var entries = new Dictionary<string, DriverUpdateInfo>(
            StringComparer.OrdinalIgnoreCase);
        WindowsDriverCandidateMerge.Merge(entries, untrustedFirst
            ? new[] { untrusted, ordinary } : [ordinary, untrusted]);
        var final = Assert.Single(entries).Value;

        Assert.False(final.IsOptional);
        Assert.True(QuickUpdatePolicy.IsEligible(final));
        Assert.Equal(gpu, final.HardwareId);
        Assert.DoesNotContain(fakeId, final.AdditionalHardwareIds!);
        Assert.Equal("NVIDIA Display - confirmed", final.Title);
        Assert.Equal([guid], QuickUpdatePolicy.AllOrdinaryIds(entries.Values));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void UnknownFirmwareTitleCannotHideConfirmedWuaDisplayGuid(
        bool unknownFirst)
    {
        const string guid = "00000000-0000-4000-8000-000000000344";
        const string gpu = @"PCI\VEN_10DE&DEV_28A0";
        const string fake = @"PCI\VEN_9999&DEV_F00D";
        var confirmed = RichCandidate(guid, gpu,
            [@"HDAUDIO\FUNC_01&VEN_10DE&DEV_009B"]) with
        {
            Title = "NVIDIA Display", SearchSource = "Windows Update online"
        };
        var unknown = RichCandidate(guid.ToUpperInvariant(), fake, [fake]) with
        {
            Title = "Unverified firmware BIOS",
            DriverClass = "Firmware",
            IsHidden = true,
            IsOptional = true,
            AdditionalHardwareIdsIncomplete = true,
            SearchSource = "Unverified OEM catalogue"
        };
        var merged = new Dictionary<string, DriverUpdateInfo>(
            StringComparer.OrdinalIgnoreCase);
        WindowsDriverCandidateMerge.Merge(merged, unknownFirst
            ? [unknown, confirmed] : [confirmed, unknown]);
        var final = Assert.Single(merged).Value;

        Assert.Equal(confirmed.Title, final.Title);
        Assert.Equal(confirmed.SearchSource, final.SearchSource);
        Assert.Equal(gpu, final.HardwareId);
        Assert.False(final.IsHidden);
        Assert.False(final.IsOptional);
        Assert.False(final.AdditionalHardwareIdsIncomplete);
        Assert.False(QuickUpdatePolicy.IsFirmwareOrBios(
            final.Title, final.DriverClass));
        Assert.DoesNotContain(fake, final.AdditionalHardwareIds!);
        Assert.Contains(@"HDAUDIO\FUNC_01&VEN_10DE&DEV_009B",
            final.AdditionalHardwareIds!);
        Assert.True(QuickUpdatePolicy.IsEligible(final));
        Assert.Equal([guid], QuickUpdatePolicy.AllOrdinaryIds(merged.Values));
    }

    [Theory]
    [InlineData(true, "advanced")]
    [InlineData(false, "advanced")]
    [InlineData(true, "hidden")]
    [InlineData(false, "hidden")]
    [InlineData(true, "optional")]
    [InlineData(false, "optional")]
    public void UnknownOriginCannotInventPnpAssociationForReviewOnlyWua(
        bool unknownFirst, string reviewKind)
    {
        const string guid = "00000000-0000-4000-8000-000000000345";
        const string nvidia = @"PCI\VEN_10DE&DEV_28A0";
        const string killer = @"PCI\VEN_8086&DEV_51F1";
        var confirmed = RichCandidate(guid, nvidia, []) with
        {
            Title = "WUA driver in review",
            SearchSource = reviewKind == "advanced"
                ? "Windows Update online · avanzato"
                : "Windows Update online",
            IsAdvancedCandidate = reviewKind == "advanced",
            IsHidden = reviewKind == "hidden",
            IsOptional = reviewKind == "optional"
        };
        var unknown = RichCandidate(guid, killer, [killer]) with
        {
            Title = "Unverified ordinary driver",
            SearchSource = "Third-party driver mirror",
            AdditionalHardwareIdsIncomplete = true
        };
        var records = new Dictionary<string, DriverUpdateInfo>(
            StringComparer.OrdinalIgnoreCase);
        WindowsDriverCandidateMerge.Merge(records, unknownFirst
            ? [unknown, confirmed] : [confirmed, unknown]);
        var final = Assert.Single(records).Value;

        Assert.Equal(confirmed.Title, final.Title);
        Assert.Equal(confirmed.SearchSource, final.SearchSource);
        Assert.Equal(nvidia, final.HardwareId);
        Assert.Empty(final.AdditionalHardwareIds!);
        Assert.False(final.AdditionalHardwareIdsIncomplete);
        Assert.Equal(confirmed.IsAdvancedCandidate, final.IsAdvancedCandidate);
        Assert.Equal(confirmed.IsHidden, final.IsHidden);
        Assert.Equal(confirmed.IsOptional, final.IsOptional);
        Assert.False(QuickUpdatePolicy.IsEligible(final));

        var killerDevice = new TheEasyWayForDrivers.Core.Models.DriverInfo(
            "PRIVATE_KILLER", "Microsoft network adapter", "Microsoft",
            "Net", "Microsoft", "1.0", null, "oem1.inf",
            true, true, 0, [killer], []);
        Assert.Equal(0, TheEasyWayForDrivers.Core.Drivers.DriverUpdateMatcher
            .MatchStrength(killerDevice, final));
        var groups = TheEasyWayForDrivers.Core.Drivers.DeviceWuaMatchGroups
            .Create(killerDevice, records.Values);
        Assert.Equal(0, groups.TotalCorrelated);
        Assert.Empty(QuickUpdatePolicy.AllOrdinaryIds(records.Values));
    }

    [Fact]
    public void ThirdPartyGuidCollisionCannotPoisonTwoTrustedWuaPasses()
    {
        const string guid = "00000000-0000-4000-8000-000000000346";
        const string nvidia = @"PCI\VEN_10DE&DEV_28A0";
        const string killer = @"PCI\VEN_8086&DEV_51F1";
        const string fake = @"PCI\VEN_9999&DEV_DEAD";
        var first = RichCandidate(guid, nvidia, []) with
        {
            SearchSource = "Windows configurato"
        };
        var second = RichCandidate(guid, killer, []) with
        {
            SearchSource = "Microsoft Update online"
        };
        var untrusted = RichCandidate(guid, fake, [fake]) with
        {
            SearchSource = "Unknown OEM website",
            Title = "Firmware",
            AdditionalHardwareIdsIncomplete = true
        };
        var records = new Dictionary<string, DriverUpdateInfo>(
            StringComparer.OrdinalIgnoreCase);
        WindowsDriverCandidateMerge.Merge(records,
            [first, untrusted, second, untrusted]);
        var chosen = Assert.Single(records).Value;

        Assert.Equal(nvidia, chosen.HardwareId);
        Assert.Contains(killer, chosen.AdditionalHardwareIds!);
        Assert.DoesNotContain(fake, chosen.AdditionalHardwareIds!);
        Assert.Equal(first.Title, chosen.Title);
        Assert.False(chosen.AdditionalHardwareIdsIncomplete);
        Assert.True(QuickUpdatePolicy.IsEligible(chosen));
        Assert.Single(QuickUpdatePolicy.AllOrdinaryIds(records.Values));
    }

    [Fact]
    public void TrustedFirmwareAndBrowseOnlyStillVetoConflictingWua()
    {
        const string guid = "00000000-0000-4000-8000-000000000347";
        const string nvidia = @"PCI\VEN_10DE&DEV_28A0";
        var ordinary = RichCandidate(guid, nvidia, []) with
        {
            SearchSource = "Windows configurato",
            Title = "NVIDIA Display"
        };
        var firmware = ordinary with
        {
            Title = "NVIDIA UEFI Firmware",
            SearchSource = "Windows Update online"
        };
        var optional = ordinary with
        {
            SearchSource = "Microsoft Update online",
            IsOptional = true
        };
        var records = new Dictionary<string, DriverUpdateInfo>(
            StringComparer.OrdinalIgnoreCase);
        WindowsDriverCandidateMerge.Merge(records, [ordinary, firmware]);
        Assert.False(QuickUpdatePolicy.IsEligible(
            Assert.Single(records).Value));

        records.Clear();
        WindowsDriverCandidateMerge.Merge(records, [ordinary, optional]);
        Assert.False(QuickUpdatePolicy.IsEligible(
            Assert.Single(records).Value));
        Assert.True(Assert.Single(records).Value.IsOptional);
    }

    [Fact]
    public void AllOrdinaryMultiSourceWuaGuidsRemainEligibleAfterMerge()
    {
        const string guid = "00000000-0000-4000-8000-000000000334";
        var configured = RichCandidate(guid,
            @"PCI\VEN_8086&DEV_51F1", []) with
        {
            SearchSource = "Windows configurato",
            Title = "Intel Killer - Net",
        };
        var online = configured with
        {
            Title = "Intel Killer Net - online",
            SearchSource = "Windows Update online"
        };
        var merged = new Dictionary<string, DriverUpdateInfo>(
            StringComparer.OrdinalIgnoreCase);
        WindowsDriverCandidateMerge.Merge(merged, [configured, online]);
        var final = Assert.Single(merged).Value;
        Assert.False(final.IsOptional);
        Assert.True(QuickUpdatePolicy.IsEligible(final));
        Assert.Equal([guid], QuickUpdatePolicy.AllOrdinaryIds(merged.Values));
    }


    [Fact]
    public void TwoWuaSourcesKeepRareVendorAndHidIdsWithoutInventingUpdates()
    {
        const string guid = "00000000-0000-4000-8000-000000000334";
        var first = Enumerable.Range(1, 1024)
            .Select(i => $@"PCI\VEN_9999&DEV_{i:X4}").ToArray();
        var other = Enumerable.Range(1025, 1018)
            .Select(i => $@"PCI\VEN_9999&DEV_{i:X4}")
            .Concat([
                @"PCI\VEN_144D&DEV_A808",
                @"USB\VID_046D&PID_C534",
                @"HID\VID_046D&PID_C534"
            ]).ToArray();
        var merged = new Dictionary<string, DriverUpdateInfo>(
            StringComparer.OrdinalIgnoreCase);
        Assert.Equal(1, WindowsDriverCandidateMerge.Merge(merged,
            [RichCandidate(guid, @"PCI\VEN_9999&DEV_FFFE", first)]));
        Assert.Equal(0, WindowsDriverCandidateMerge.Merge(merged,
            [RichCandidate(guid, @"PCI\VEN_9999&DEV_FFFD", other)]));
        var chosen = Assert.Single(merged).Value;

        Assert.Equal(1024, chosen.AdditionalHardwareIds!.Count);
        Assert.True(chosen.AdditionalHardwareIdsIncomplete);
        foreach (var id in new[]
        {
            @"PCI\VEN_144D&DEV_A808",
            @"USB\VID_046D&PID_C534",
            @"HID\VID_046D&PID_C534"
        })
        {
            Assert.Contains(id, chosen.AdditionalHardwareIds);
            var device = new DriverInfo("PRIVATE_SERIAL", "Generic device",
                "Windows", "System", "Windows", "1.0", null,
                "oem42.inf", true, true, 0, [id], []);
            Assert.Equal(2, TheEasyWayForDrivers.Core.Drivers.DriverUpdateMatcher
                .MatchStrength(device, chosen));
        }
        Assert.Single(QuickUpdatePolicy.AllOrdinaryIds(merged.Values));
    }

    [Theory]
    [InlineData("\u00A0")]
    [InlineData("\u2003")]
    [InlineData("\u202F")]
    public void CrossSourceMergeCannotNormalizeUnicodePaddingIntoNvidiaMatch(
        string whitespace)
    {
        const string guid = "00000000-0000-4000-8000-000000000430";
        const string nvidia = @"PCI\VEN_10DE&DEV_28A0";
        const string unrelated = @"PCI\VEN_8888&DEV_1234";
        var malformed = whitespace + nvidia + whitespace;
        var ordinary = RichCandidate(guid, unrelated, [malformed]) with
        {
            SearchSource = "Windows Update online"
        };
        var other = RichCandidate(guid.ToUpperInvariant(),
            @"PCI\VEN_7777&DEV_1234", []) with
        {
            SearchSource = "Microsoft Update online"
        };
        var merged = new Dictionary<string, DriverUpdateInfo>(
            StringComparer.OrdinalIgnoreCase);

        Assert.False(TheEasyWayForDrivers.Core.Drivers.DriverUpdateMatcher
            .IsCorrelatableHardwareId(malformed));
        WindowsDriverCandidateMerge.Merge(merged, [ordinary, other]);
        var result = Assert.Single(merged).Value;
        Assert.DoesNotContain(nvidia, result.AdditionalHardwareIds!);
        Assert.DoesNotContain(malformed, result.AdditionalHardwareIds!);
        Assert.Single(merged);

        var gpu = new DriverInfo("PRIVATE_NVIDIA", "NVIDIA GPU",
            "NVIDIA", "Display", "NVIDIA", "31.0.0.0", null,
            "oem123.inf", true, true, 0, [nvidia], []);
        Assert.Equal(0, TheEasyWayForDrivers.Core.Drivers.DriverUpdateMatcher
            .MatchStrength(gpu, result));

        // Completed WUA source metadata does not make an invalid PnP ID
        // exact evidence. Only one update GUID exists across the passes.
        var report = new DriverSearchReport([ordinary, other], [
            new DriverSearchPassStatus("Windows Update online",
                "Completed", 1, "", 1),
            new DriverSearchPassStatus("Microsoft Update online",
                "Completed", 1, "", 0)
        ]);
        var audit = TheEasyWayForDrivers.Core.Drivers.WuaPnpAssociationAudit
            .Create([gpu], report, inventoryComplete: true);
        Assert.Equal(1, audit.ConfirmedGuids);
        Assert.Equal(0, audit.ExactHardware);
        Assert.Equal(1, audit.Unattributed);
        Assert.False(audit.IsInstallable);
        Assert.DoesNotContain("PRIVATE_", audit.Text);
    }

    [Theory]
    [InlineData("\u00A0")]
    [InlineData("\u2003")]
    public void OtherWuaPassPrimaryWithUnicodePaddingCannotBecomeExactExtra(
        string whitespace)
    {
        const string guid = "00000000-0000-4000-8000-000000000431";
        const string killer = @"PCI\VEN_8086&DEV_51F1";
        var first = RichCandidate(guid, @"PCI\VEN_9999&DEV_1111", [])
            with { SearchSource = "Windows Update online" };
        var second = RichCandidate(guid.ToUpperInvariant(),
            whitespace + killer + whitespace, [])
            with { SearchSource = "Microsoft Update online" };
        var updates = new Dictionary<string, DriverUpdateInfo>(
            StringComparer.OrdinalIgnoreCase);

        WindowsDriverCandidateMerge.Merge(updates, [first, second]);
        var result = Assert.Single(updates).Value;
        Assert.Equal(first.HardwareId, result.HardwareId);
        Assert.DoesNotContain(killer, result.AdditionalHardwareIds!);

        var device = new DriverInfo("PRIVATE_KILLER", "Intel Killer",
            "Intel", "Net", "Intel", "31.0.0.0", null, "oem12.inf",
            true, true, 0, [killer], []);
        Assert.Equal(0, TheEasyWayForDrivers.Core.Drivers.DriverUpdateMatcher
            .MatchStrength(device, result));
    }

    [Fact]
    public void AsciiPaddedIdsRemainAssociableAcrossTwoCompletedWuaSources()
    {
        const string guid = "00000000-0000-4000-8000-000000000432";
        const string nvidia = @"PCI\VEN_10DE&DEV_28A0";
        const string killer = @"PCI\VEN_8086&DEV_51F1";
        var first = RichCandidate(guid, @"PCI\VEN_9999&DEV_1111",
            ["  " + nvidia + "  "]) with
        {
            SearchSource = "Windows Update online"
        };
        var second = RichCandidate("{" + guid + "}",
            " " + killer + " ", []) with
        {
            SearchSource = "Microsoft Update online"
        };
        var merged = new Dictionary<string, DriverUpdateInfo>(
            StringComparer.OrdinalIgnoreCase);
        WindowsDriverCandidateMerge.Merge(merged, [first, second]);
        var result = Assert.Single(merged).Value;

        Assert.Equal(guid, result.Id);
        Assert.Contains(nvidia, result.AdditionalHardwareIds!);
        Assert.Contains(killer, result.AdditionalHardwareIds!);
        Assert.Equal(2, result.AdditionalHardwareIds!.Count);
        Assert.False(result.AdditionalHardwareIdsIncomplete);

        foreach (var id in new[] { nvidia, killer })
        {
            var device = new DriverInfo("PRIVATE_DEVICE", "Generic",
                "Microsoft", "System", "Microsoft", "1.0.0.0",
                null, "oem42.inf", true, true, 0, [id], []);
            Assert.Equal(2, TheEasyWayForDrivers.Core.Drivers.DriverUpdateMatcher
                .MatchStrength(device, result));
        }
        Assert.Single(QuickUpdatePolicy.AllOrdinaryIds(merged.Values));
    }

}
