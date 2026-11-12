using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;
using TheEasyWayForDrivers.Core.Update;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

/// <summary>
/// Strict USB storage product IDs may occur late in one WUA update with
/// thousands of IWindowsDriverUpdateEntry rows. They must receive an
/// exact-ID association slot, not a new GUID or install eligibility.
/// </summary>
public sealed class WuaUsbStorEntryFairnessTests
{
    private const string Sandisk =
        @"USBSTOR\Disk&Ven_SanDisk&Prod_Cruzer_Blade&Rev_1.00";
    private const string Kingston =
        @"USBSTOR\Disk&Ven_Kingston&Prod_DataTraveler&Rev_2.10";
    private const string Padded =
        @"USBSTOR\DiskSanDisk_Cruzer_Blade____0001";
    private const string Untyped =
        @"USBSTOR\SanDisk_Cruzer_Blade____1";
    private const string Nvidia = @"PCI\VEN_10DE&DEV_28A0";
    private const string Sndw =
        @"SNDW\CLASS_00&MAN_025D&PART_0711";

    private static string[] Prefix(int length, int start = 1) =>
        Enumerable.Range(start, length)
            .Select(i => $@"PCI\VEN_9999&DEV_{i:X4}")
            .ToArray();

    private static string[] RareManufacturers(int count) =>
        Enumerable.Range(0, count)
            .Select(i => $@"PCI\VEN_{(0x3000 + i):X4}&DEV_0001")
            .ToArray();

    private static DriverInfo Device(string id) =>
        new("PRIVATE_STORAGE_SERIAL", "Disk drive", "Microsoft",
            "DiskDrive", "Microsoft", "1.0", null, "PRIVATE_oem42.inf",
            true, true, 0, [id], []);

    private static DriverUpdateInfo Offer(
        IReadOnlyList<string> ids, string source = "Windows Update online") =>
        new("00000000-0000-4000-8000-000000000384",
            "Windows driver", null, "DiskDrive", "Microsoft",
            null, null, false, "Microsoft", null,
            @"PCI\VEN_9999&DEV_0000", null,
            SearchSource: source, AdditionalHardwareIds: ids,
            AdditionalHardwareIdsIncomplete: ids.Count >= 1024);

    [Theory]
    [InlineData(Sandisk, @"USBSTOR\DISK:&VEN_SANDISK")]
    [InlineData(Kingston, @"USBSTOR\DISK:&VEN_KINGSTON")]
    [InlineData(Padded, @"USBSTOR\DISK:SANDISK_")]
    [InlineData(Untyped, @"USBSTOR\UNTYPED:SANDISK_")]
    public void ValidProductHasStableManufacturerTypeFamily(
        string id, string expected)
    {
        Assert.True(UsbStorPnpMatchPolicy.IsSpecificHardwareId(id));
        Assert.True(DriverUpdateMatcher.IsCorrelatableHardwareId(id));
        Assert.Equal(expected,
            UsbStorPnpMatchPolicy.TryGetVendorFamilyKey(id));
        Assert.Null(MicrosoftUpdateCatalogSearch.TryGetPublicModelKey(id));
    }

    [Fact]
    public void ManyRarePciFamiliesCannotDisplaceLateUsbStorDottedOrPadded()
    {
        var baseIds = Prefix(1360);
        var end = new[] { Sandisk, Padded, Kingston, Nvidia, Sndw };
        var input = baseIds.Concat(RareManufacturers(150))
            .Concat(end).ToArray();

        var result = WuaDriverEntryIdSelection.Select(input);
        Assert.Equal(1024, result.HardwareIds.Count);
        Assert.Equal(input.Length - 1024, result.DeferredIds);
        Assert.Equal(baseIds.Take(960), result.HardwareIds.Take(960));
        Assert.All(end, id => Assert.Contains(id, result.HardwareIds));
        Assert.Equal(1024, result.HardwareIds.Distinct(
            StringComparer.OrdinalIgnoreCase).Count());

        var offer = Offer(result.HardwareIds);
        foreach (var id in end)
            Assert.Equal(2, DriverUpdateMatcher.MatchStrength(
                Device(id), offer));
        Assert.Equal(0, DriverUpdateMatcher.MatchStrength(
            Device(@"USBSTOR\GenDisk"), offer));
        Assert.Single(QuickUpdatePolicy.AllOrdinaryIds([offer]));
    }

    [Fact]
    public void OneManufacturerProductFloodCannotBuryAnotherStorageVendor()
    {
        var sameMaker = Enumerable.Range(0, 160)
            .Select(i => $@"USBSTOR\Disk&Ven_SanDisk&Prod_Cruzer_{i:D3}&Rev_1.00");
        var input = Prefix(1240).Concat(sameMaker)
            .Concat([Kingston, Untyped]).ToArray();

        var chosen = WuaDriverEntryIdSelection.Select(input);
        Assert.Equal(1024, chosen.HardwareIds.Count);
        Assert.Contains(@"USBSTOR\Disk&Ven_SanDisk&Prod_Cruzer_000&Rev_1.00",
            chosen.HardwareIds);
        Assert.Contains(Kingston, chosen.HardwareIds);
        Assert.Contains(Untyped, chosen.HardwareIds);
        Assert.Equal(2, DriverUpdateMatcher.MatchStrength(
            Device(Kingston), Offer(chosen.HardwareIds)));
    }

    [Theory]
    [InlineData(@"USBSTOR\GenDisk")]
    [InlineData(@"USBSTOR\Disk")]
    [InlineData(@"USBSTOR\Disk&Ven_SanDisk")]
    [InlineData(@"USBSTOR\Disk&Ven_SanDisk&Prod_")]
    [InlineData(@"USBSTOR\Disk&Ven_SanDisk&Prod_Cruzer&Rev_1..00")]
    [InlineData(@"USBSTOR\Disk&Ven_SanDisk&Prod_Cruzer&Rev_1.00&FAKE_01")]
    [InlineData(@"USBSTOR\Disk&Ven_SanDisk&Prod_Cruzer\PRIVATE")]
    [InlineData(@"USBSTOR\Disk&Ven_SanDisk&Prod_Cruzer/PRIVATE")]
    [InlineData(@"USBSTOR\DiskSanDisk_")]
    [InlineData(@"PCI\VEN_10DE&FAKE_0012")]
    public void GenericForeignOrPrivateTokensCannotTakeProtectedUsbStorSlot(
        string invalid)
    {
        Assert.Null(UsbStorPnpMatchPolicy.TryGetVendorFamilyKey(invalid));
        var input = Prefix(1290).Concat(RareManufacturers(130))
            .Concat([invalid, Sandisk, Nvidia]).ToArray();
        var selected = WuaDriverEntryIdSelection.Select(input);
        Assert.Equal(1024, selected.HardwareIds.Count);
        Assert.DoesNotContain(invalid, selected.HardwareIds);
        Assert.Contains(Sandisk, selected.HardwareIds);
        Assert.Contains(Nvidia, selected.HardwareIds);
        Assert.True(selected.DeferredIds > 0);
    }

    [Fact]
    public void SameWuaGuidAcrossTwoKnownPassesRetainsLateUsbStorModel()
    {
        var first = Offer(Prefix(1024));
        var second = Offer(Prefix(1023, 1025).Append(Sandisk).ToArray(),
            "Microsoft Update online");
        var merged = new Dictionary<string, DriverUpdateInfo>(
            StringComparer.OrdinalIgnoreCase);
        Assert.Equal(1, WindowsDriverCandidateMerge.Merge(merged,
            [first, second]));
        var update = Assert.Single(merged.Values);
        Assert.Equal(1024, update.AdditionalHardwareIds?.Count);
        Assert.Contains(Sandisk, update.AdditionalHardwareIds!);
        Assert.True(update.AdditionalHardwareIdsIncomplete);
        Assert.Equal(2, DriverUpdateMatcher.MatchStrength(
            Device(Sandisk), update));
        Assert.Single(QuickUpdatePolicy.AllOrdinaryIds([update]));
    }

    [Fact]
    public void UsbStorRevisionIsValidLocalInfEvidenceButNotAnInstallVerdict()
    {
        var inf = "[Version]\nSignature=\"$Windows NT$\"\n" +
            "[Manufacturer]\n%Disk%=Models,NTamd64\n" +
            "[Models.NTamd64]\n%Disk%=Install, " + Sandisk + "\n";
        var evidence = Assert.Single(InfModelEvidence.ReadDeclaredIds(inf));
        Assert.Equal(Sandisk.ToUpperInvariant(), evidence.HardwareId);
        Assert.Equal(2, InfModelEvidence.MatchStrength(
            Device(Sandisk), [evidence]));
        Assert.Equal(0, InfModelEvidence.MatchStrength(
            Device(Kingston), [evidence]));
        var summary = ScanCoverageText.Create(null, [], 1, 0, 0,
            [Device(Sandisk)], inventoryComplete: true);
        Assert.DoesNotContain("PRIVATE", summary);
        Assert.DoesNotContain(Sandisk, summary);
    }

    [Fact]
    public void InputCeilingAndSmallCollectionsDoNotChange()
    {
        var small = new[] { Sandisk, @"USBSTOR\GenDisk", Kingston };
        var original = WuaDriverEntryIdSelection.Select(small);
        Assert.Equal(small, original.HardwareIds);
        Assert.Equal(0, original.DeferredIds);

        Assert.Throws<InvalidDataException>(() =>
            WuaDriverEntryIdSelection.Select(
                Prefix(WuaDriverEntryLimits.MaximumEntriesToInspect + 1)));
    }
}
