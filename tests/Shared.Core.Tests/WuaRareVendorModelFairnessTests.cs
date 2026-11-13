using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;
using TheEasyWayForDrivers.Core.Update;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

/// <summary>
/// #441: for late uncommon vendors, preserve a second exact public model
/// before hundreds of SUBSYS/REV variants of the first model. Selection is
/// transport fairness, not Windows driver ranking or install permission.
/// </summary>
public sealed class WuaRareVendorModelFairnessTests
{
    private const string Nvidia = @"PCI\VEN_10DE&DEV_28A0";
    private const string IntelKiller = @"PCI\VEN_8086&DEV_51F1";
    private const string PciBase = @"PCI\VEN_1234&DEV_1100";
    private const string PciSecond = @"PCI\VEN_1234&DEV_2200&SUBSYS_55AA1234";
    private const string UsbBase = @"USB\VID_1234&PID_1100";
    private const string UsbSecond = @"USB\VID_1234&PID_2200&MI_00";

    private static string[] Baseline(int count = 960) =>
        Enumerable.Range(1, count)
            .Select(i => $@"PCI\VEN_9999&DEV_{i:X4}").ToArray();

    private static string[] Revisions(string model, int count = 150) =>
        Enumerable.Range(1, count)
            .Select(i => model + $"&SUBSYS_{i:X8}").ToArray();

    private static DriverUpdateInfo Wua(IReadOnlyList<string> ids) =>
        new("00000000-0000-4000-8000-000000000441",
            "Windows driver", null, "System", "Microsoft", null, null,
            false, "Microsoft", null, @"PCI\VEN_9999&DEV_0000", null,
            SearchSource: "Windows Update online",
            AdditionalHardwareIds: ids,
            AdditionalHardwareIdsIncomplete: ids.Count > 1024);

    private static DriverInfo Device(string hardware) =>
        new("PRIVATE_DEVICE_INSTANCE", "Generic Windows device",
            "Microsoft", "System", "Microsoft", "1.0", null, "oem1.inf",
            true, true, 0, [hardware], []);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SecondRarePciModelSurvivesFirstModelRevisionFlood(
        bool firstModelAlreadyInBaseline)
    {
        var first = PciBase + "&SUBSYS_00000001";
        var leading = firstModelAlreadyInBaseline
            ? new[] { first }.Concat(Baseline(959))
            : Baseline().AsEnumerable();
        var input = leading.Concat(Revisions(PciBase))
            .Concat([PciSecond, @"PCI\VEN_4321&DEV_0001",
                Nvidia, IntelKiller]).ToArray();

        var selection = WuaDriverEntryIdSelection.Select(input);
        Assert.Equal(1024, selection.HardwareIds.Count);
        Assert.Equal(input.Length - 1024, selection.DeferredIds);
        Assert.Equal(input.Take(960), selection.HardwareIds.Take(960));
        Assert.Contains(PciSecond, selection.HardwareIds);
        Assert.Contains(@"PCI\VEN_4321&DEV_0001", selection.HardwareIds);
        Assert.Contains(Nvidia, selection.HardwareIds);
        Assert.Contains(IntelKiller, selection.HardwareIds);
        Assert.Equal(1024, selection.HardwareIds.Distinct(
            StringComparer.OrdinalIgnoreCase).Count());

        var update = Wua(selection.HardwareIds);
        Assert.Equal(2, DriverUpdateMatcher.MatchStrength(
            Device(PciSecond), update));
        Assert.Equal(0, DriverUpdateMatcher.MatchStrength(
            Device(@"PCI\VEN_1234&DEV_3300"), update));
        Assert.Single(QuickUpdatePolicy.AllOrdinaryIds([update]));
    }

    [Fact]
    public void LateUsbPidIsDistinctProductNotAnotherRevisionOfSameVendor()
    {
        var variants = Enumerable.Range(1, 160)
            .Select(i => UsbBase + $"&REV_{i:X4}");
        var input = Baseline().Concat(variants)
            .Concat([UsbSecond, Nvidia, IntelKiller]).ToArray();

        var result = WuaDriverEntryIdSelection.Select(input);
        Assert.Contains(UsbSecond, result.HardwareIds);
        Assert.Contains(Nvidia, result.HardwareIds);
        Assert.Contains(IntelKiller, result.HardwareIds);
        Assert.Equal(2, DriverUpdateMatcher.MatchStrength(
            Device(UsbSecond), Wua(result.HardwareIds)));
        Assert.Equal(0, DriverUpdateMatcher.MatchStrength(
            Device(@"USB\VID_1234&PID_3300&MI_00"),
            Wua(result.HardwareIds)));
    }

    [Theory]
    [InlineData("HDAUDIO")]
    [InlineData("INTELAUDIO")]
    [InlineData("SWC")]
    public void LateSecondAudioOrSoftwareComponentModelSurvives(
        string bus)
    {
        var original = bus == "SWC"
            ? @"SWC\VEN_1234&DEV_1100"
            : bus + @"\FUNC_01&VEN_1234&DEV_1100";
        var second = original.Replace("DEV_1100", "DEV_2200",
            StringComparison.Ordinal) + "&SUBSYS_01234567";
        var input = Baseline().Concat(Revisions(original, 165))
            .Concat([second, Nvidia]).ToArray();

        var result = WuaDriverEntryIdSelection.Select(input);
        Assert.Contains(second, result.HardwareIds);
        Assert.Equal(2, DriverUpdateMatcher.MatchStrength(
            Device(second), Wua(result.HardwareIds)));
        Assert.Equal(0, DriverUpdateMatcher.MatchStrength(
            Device(original.Replace("DEV_1100", "DEV_3300",
                StringComparison.Ordinal)), Wua(result.HardwareIds)));
    }

    [Fact]
    public void NewForeignVendorPrecedesFloodOfModelsFromOneRareVendor()
    {
        var manyModels = Enumerable.Range(1, 180)
            .Select(i => $@"PCI\VEN_1234&DEV_{i:X4}");
        const string lateVendor = @"PCI\VEN_4321&DEV_9000";
        var input = Baseline().Concat(manyModels)
            .Concat([lateVendor, Nvidia]).ToArray();

        var result = WuaDriverEntryIdSelection.Select(input);
        Assert.Contains(lateVendor, result.HardwareIds);
        Assert.Contains(Nvidia, result.HardwareIds);
        Assert.Equal(2, DriverUpdateMatcher.MatchStrength(
            Device(lateVendor), Wua(result.HardwareIds)));
    }

    [Theory]
    [InlineData(@"PCI\VEN_1234&DEV_2200&FAKE_01")]
    [InlineData(@"PCI\VEN_1234&DEV_2200\PRIVATE_INSTANCE")]
    [InlineData(@"PCI\VEN_1234")]
    [InlineData(@"USB\VID_1234&PID_2200&FAKE_01")]
    [InlineData(@"USB\VID_1234&PID_2200\PRIVATE_INSTANCE")]
    [InlineData(@"HDAUDIO\FUNC_01&VEN_1234&DEV_2200&FAKE_01")]
    [InlineData(@"SWC\VEN_1234&DEV_2200&FAKE_01")]
    public void InvalidIdsNeverConsumeProtectedModelSlot(string invalid)
    {
        Assert.Null(MicrosoftUpdateCatalogSearch.TryGetPublicModelKey(invalid));
        var otherVendors = Enumerable.Range(0, 60)
            .Select(i => $@"PCI\VEN_{(0x4000 + i):X4}&DEV_0001");
        var input = Baseline().Concat(Revisions(PciBase, 110))
            .Concat([invalid, PciSecond]).Concat(otherVendors)
            .Concat([Nvidia]).ToArray();
        var result = WuaDriverEntryIdSelection.Select(input);

        Assert.DoesNotContain(invalid, result.HardwareIds);
        Assert.Contains(PciSecond, result.HardwareIds);
        Assert.Contains(Nvidia, result.HardwareIds);
        Assert.Equal(1024, result.HardwareIds.Count);
    }

    [Fact]
    public void SmallCollectionsPreserveEveryOriginalIdentifierAndOrder()
    {
        var input = new[]
        {
            PciBase, PciSecond, UsbBase, UsbSecond, Nvidia, IntelKiller
        };
        var result = WuaDriverEntryIdSelection.Select(input);
        Assert.Equal(input, result.HardwareIds);
        Assert.Equal(0, result.DeferredIds);
        Assert.Throws<InvalidDataException>(() =>
            WuaDriverEntryIdSelection.Select(
                Baseline(WuaDriverEntryLimits.MaximumEntriesToInspect + 1)));
    }
}
