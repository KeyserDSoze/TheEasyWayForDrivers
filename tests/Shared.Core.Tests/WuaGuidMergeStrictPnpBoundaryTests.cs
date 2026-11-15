using System.Text.Json;
using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;
using TheEasyWayForDrivers.Core.Update;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

/// <summary>
/// #465: public WUA GUID merger accepts legacy/deserialized reports as well
/// as service COM results. All PnP identities must cross the SAME strict
/// safety boundary as WuaDriverCandidateReader, including after GUID merge.
/// </summary>
public sealed class WuaGuidMergeStrictPnpBoundaryTests
{
    private const string GuidId = "00000000-0000-4000-8000-000000000465";
    private const string Nvidia = @"PCI\VEN_10DE&DEV_28A0";
    private const string Killer = @"PCI\VEN_8086&DEV_51F1";
    private const string Realtek = @"HDAUDIO\FUNC_01&VEN_10EC&DEV_0295";
    private const string Acpi = @"ACPI\NVDA0820";
    private const string SoundWire = @"SOUNDWIRE\SDCA_06&MAN_025D&FUNC_0711";
    private const string Sndw = @"SNDW\CLASS_00&MAN_025D&PART_0711";
    private const string Acp =
        @"ACP\DEVTYPE_0005&DEVREV_0000&VEN_1022&DEV_15E2";
    private const string UsbStor = @"USBSTOR\DISK&VEN_SANDISK&PROD_ULTRA";
    private const string Hid = @"HID\VID_046D&PID_C534";
    private const string HidUsage = @"HID\VID_046D&UP:0001_U:0006";

    private static DriverUpdateInfo Offer(string? hardware,
        IReadOnlyList<string>? extras = null,
        string source = "Windows Update online",
        bool advanced = false) =>
        new(GuidId, "Windows driver", null, "Display", "Microsoft",
            null, null, false, "Microsoft", null, hardware, null,
            IsAdvancedCandidate: advanced, SearchSource: source,
            AdditionalHardwareIds: extras);

    private static DriverInfo Device(string hardware) =>
        new("PRIVATE_WINDOWS_INSTANCE", "Generic Windows device",
            "Microsoft", "Display", "Microsoft", "1.0.0.0",
            null, "oem25.inf", true, true, 0, [hardware], []);

    private static DriverUpdateInfo Merge(params DriverUpdateInfo[] items)
    {
        var byGuid = new Dictionary<string, DriverUpdateInfo>(
            StringComparer.OrdinalIgnoreCase);
        Assert.Equal(1, WindowsDriverCandidateMerge.Merge(byGuid, items));
        Assert.Single(byGuid);
        return Assert.Single(byGuid.Values);
    }

    [Theory]
    [InlineData(@"PCI\VEN_10DE&DEV_28A0\PRIVATE_SERIAL")]
    [InlineData(@"C:\PRIVATE_ORDER\driver.inf")]
    [InlineData(@"PCI\VEN_10DE&DEV_28A0&FAKE_01")]
    [InlineData(@"PCI\VEN_10DE")]
    [InlineData(@"HDAUDIO\FUNC_01&VEN_10DE\PRIVATE_INSTANCE")]
    [InlineData(@"USB\VID_8087&PID_0033\PRIVATE_INSTANCE")]
    [InlineData("PCI\\VEN_10DE&DEV_28A0\t")]
    [InlineData("PCI\\VEN_10DE&DEV_28A0\n")]
    [InlineData("PCI\\VEN_10DE&DEV_28A0\u00A0")]
    [InlineData("PCI\\VEN_10DE&DEV_28A0\u202E")]
    [InlineData("PCI\\VEN_10DE&DEV_28A0\u200B")]
    public void MalformedAdditionalIdNeverSurvivesConfirmedWuaGuidMerge(
        string malformed)
    {
        Assert.False(PnpIdentifierFallback.IsStrictSingleId(malformed));
        var merged = Merge(Offer(Nvidia, [malformed]));

        Assert.Equal(Nvidia, merged.HardwareId);
        Assert.Empty(merged.AdditionalHardwareIds!);
        Assert.True(merged.AdditionalHardwareIdsIncomplete);
        Assert.True(QuickUpdatePolicy.IsEligible(merged));
        Assert.Equal(2, DriverUpdateMatcher.MatchStrength(
            Device(Nvidia), merged));
        Assert.Equal(0, DriverUpdateMatcher.MatchStrength(
            Device(Killer), merged));
        var serialized = JsonSerializer.Serialize(merged);
        Assert.DoesNotContain(malformed, serialized);
        Assert.DoesNotContain("PRIVATE_SERIAL", serialized);
        Assert.DoesNotContain("PRIVATE_ORDER", serialized);
    }

    [Theory]
    [InlineData(@"PCI\VEN_10DE&DEV_28A0\PRIVATE_SERIAL")]
    [InlineData(@"PCI\VEN_10DE&DEV_28A0&FAKE_01")]
    [InlineData("PCI\\VEN_10DE&DEV_28A0\t")]
    [InlineData("PCI\\VEN_10DE&DEV_28A0\u00A0")]
    public void InvalidPrimaryIsNotReturnedButValidCompatibleIdsSurvive(
        string malformed)
    {
        var merged = Merge(Offer(malformed, [Killer, Realtek]));
        Assert.Null(merged.HardwareId);
        Assert.Equal([Killer, Realtek], merged.AdditionalHardwareIds);
        Assert.True(merged.AdditionalHardwareIdsIncomplete);
        Assert.Equal(2, DriverUpdateMatcher.MatchStrength(
            Device(Killer), merged));
        Assert.Equal(2, DriverUpdateMatcher.MatchStrength(
            Device(Realtek), merged));
        Assert.Equal(0, DriverUpdateMatcher.MatchStrength(
            Device(Nvidia), merged));
        Assert.DoesNotContain(malformed, JsonSerializer.Serialize(merged));
    }

    [Fact]
    public void MissingPrimaryIsNormalWhenSecondaryWuaEntryIsVerified()
    {
        var merged = Merge(Offer(null, [Nvidia, Hid]));
        Assert.Null(merged.HardwareId);
        Assert.Equal([Nvidia, Hid], merged.AdditionalHardwareIds);
        Assert.False(merged.AdditionalHardwareIdsIncomplete);
        Assert.Equal(2, DriverUpdateMatcher.MatchStrength(
            Device(Nvidia), merged));
        Assert.True(QuickUpdatePolicy.IsEligible(merged));
    }

    [Theory]
    [InlineData("  ")]
    [InlineData(" ")]
    public void AsciiSpacePaddingIsNormalizedWithoutDiscardingTrueModels(
        string padding)
    {
        var merged = Merge(Offer(padding + Nvidia.ToLowerInvariant() +
            padding, [" " + Killer + " "]));
        Assert.Equal(Nvidia, merged.HardwareId, ignoreCase: true);
        Assert.Equal([Killer], merged.AdditionalHardwareIds);
        Assert.False(merged.AdditionalHardwareIdsIncomplete);
        Assert.Equal(2, DriverUpdateMatcher.MatchStrength(
            Device(Nvidia), merged));
        Assert.Equal(2, DriverUpdateMatcher.MatchStrength(
            Device(Killer), merged));
    }

    [Fact]
    public void AllStrictModernBusAndHidUsageIdentifiersSurvive()
    {
        var ids = new[] { Nvidia, Killer, Realtek, Acpi, SoundWire,
            Sndw, Acp, UsbStor, Hid, HidUsage };
        Assert.All(ids, id =>
            Assert.True(PnpIdentifierFallback.IsStrictSingleId(id), id));

        var merged = Merge(Offer(null, ids));
        Assert.Equal(ids, merged.AdditionalHardwareIds);
        Assert.False(merged.AdditionalHardwareIdsIncomplete);
        foreach (var id in ids.Where(DriverUpdateMatcher.IsCorrelatableHardwareId))
        {
            Assert.Equal(2, DriverUpdateMatcher.MatchStrength(
                Device(id), merged));
        }
        Assert.Equal(0, DriverUpdateMatcher.MatchStrength(
            Device(@"PCI\VEN_10DE&DEV_28A1"), merged));
    }

    [Fact]
    public void MixedCompletedWuaPassesCannotReintroducePrivateInstanceId()
    {
        var advanced = Offer(@"PCI\VEN_10DE&DEV_28A0\PRIVATE_SERIAL",
            [Killer + "\n", Realtek], source: "Windows Update online · avanzato",
            advanced: true);
        var ordinary = Offer(Nvidia, [Killer, @"C:\PRIVATE_ORDER\oem.inf"],
            source: "Microsoft Update online");
        var merged = Merge(advanced, ordinary);

        Assert.Equal(Nvidia, merged.HardwareId);
        Assert.Contains(Killer, merged.AdditionalHardwareIds!);
        Assert.Contains(Realtek, merged.AdditionalHardwareIds!);
        Assert.True(merged.AdditionalHardwareIdsIncomplete);
        Assert.False(merged.IsAdvancedCandidate);
        Assert.True(QuickUpdatePolicy.IsEligible(merged));
        Assert.Single(QuickUpdatePolicy.AllOrdinaryIds([merged]));
        Assert.DoesNotContain("PRIVATE_", JsonSerializer.Serialize(merged));
        Assert.Equal(2, DriverUpdateMatcher.MatchStrength(
            Device(Realtek), merged));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void UnknownOriginCannotPoisonConfirmedGuidInEitherOrder(
        bool unknownFirst)
    {
        var unknown = Offer(@"PCI\VEN_10DE&DEV_28A0\PRIVATE_SERIAL",
            [Killer], source: "Unknown OEM site");
        var known = Offer(Nvidia, [Realtek], source: "Windows Update online");
        var merged = unknownFirst
            ? Merge(unknown, known) : Merge(known, unknown);

        Assert.Equal(Nvidia, merged.HardwareId);
        Assert.Equal([Realtek], merged.AdditionalHardwareIds);
        Assert.False(merged.AdditionalHardwareIdsIncomplete);
        Assert.True(QuickUpdatePolicy.IsEligible(merged));
        Assert.DoesNotContain("PRIVATE_", JsonSerializer.Serialize(merged));
    }

    [Fact]
    public void RejectedIdsRemainIncompleteInAnonymousCoverageCopy()
    {
        var sanitized = Merge(Offer(Nvidia,
            [@"PCI\VEN_10DE&DEV_28A0\PRIVATE_SERIAL"]));
        var report = new DriverSearchReport([sanitized], [
            new DriverSearchPassStatus("Windows Update online",
                "Completed", 1, "", 1)
        ]);
        var device = Device(Nvidia);
        var audited = WuaPnpAssociationAudit.Create([device], report, true);
        Assert.Equal(1, audited.ConfirmedGuids);
        Assert.Equal(1, audited.ExactHardware);
        Assert.Equal(1, audited.IncompleteWuaIds);

        var copy = ScanCoverageText.Create(report, [], 1, 0, 0,
            [device], inventoryComplete: true);
        Assert.Contains("Candidati WUA con associazioni dispositivo incomplete: 1",
            copy);
        Assert.DoesNotContain("PRIVATE_", copy);
        Assert.DoesNotContain(Nvidia, copy);
        Assert.DoesNotContain(GuidId, copy);
        Assert.False(audited.IsInstallable);
    }
}
