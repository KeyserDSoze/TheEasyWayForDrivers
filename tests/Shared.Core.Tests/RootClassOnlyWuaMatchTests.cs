using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;
using TheEasyWayForDrivers.Core.Update;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

/// <summary>
/// #548: a generic ROOT class is not a distinct Windows PnP model;
/// exact text equality with a WUA GUID cannot create a per-device match.
/// </summary>
public sealed class RootClassOnlyWuaMatchTests
{
    private const string Audio = @"HDAUDIO\FUNC_01&VEN_10EC&DEV_0295";
    private const string RootGeneric = @"ROOT\MEDIA";

    private static DriverInfo Device(
        string id, IReadOnlyList<string>? compatible = null) =>
        new("PRIVATE_AUDIO_INSTANCE", "Generic Windows Device",
            "Microsoft", "MEDIA", "Microsoft", "1.0.0.0", null,
            "oem42.inf", true, true, 0, [id], compatible ?? []);

    private static DriverUpdateInfo Offer(int index, string id) =>
        new($"00000000-0000-4000-8000-{index:D12}",
            "Synthetic Windows Driver", null, "MEDIA", "Microsoft",
            null, null, false, "Microsoft", null, id, null,
            SearchSource: "Windows Update online");

    private static DriverSearchReport Report(
        params DriverUpdateInfo[] updates) =>
        new(updates,
        [
            new DriverSearchPassStatus("Windows Update online",
                "Completed", updates.Length, "", updates.Length)
        ]);

    [Theory]
    [InlineData(@"ROOT\MEDIA")]
    [InlineData(@"root\net")]
    [InlineData(@"ROOT\SOFTWARE")]
    [InlineData(@"ROOT\SYSTEM")]
    [InlineData(@"ROOT\DISPLAY")]
    [InlineData(@"ROOT\UNKNOWN")]
    [InlineData(@"ROOT\DEVICE")]
    [InlineData(@"ROOT\BLUETOOTH")]
    [InlineData(@"ROOT\SOFTWARECOMPONENT")]
    [InlineData(@"ROOT\EXTENSION")]
    [InlineData("  ROOT\\MEDIA  ")]
    public void RootClassAliasesAreNotSpecificHardwareIds(string alias)
    {
        Assert.False(DriverUpdateMatcher.IsCorrelatableHardwareId(alias));
        Assert.Equal(0, DriverUpdateMatcher.MatchStrength(
            Device(alias), Offer(548, alias)));
        Assert.Empty(DriverUpdateMatcher.FindMatches(
            Device(alias), [Offer(548, alias)]));
        var prepared = DeviceWuaMatchGroups.Prepare([Offer(548, alias)]);
        Assert.True(prepared.UsesPnpIndex);
        var match = prepared.ForDevice(Device(alias));
        Assert.Empty(match.Ordinary);
        Assert.Empty(match.ReviewOnly);
        Assert.Empty(match.ModelOnlyReview!);
        Assert.Equal(0, match.TotalCorrelated);
    }

    [Fact]
    public void GenericRootCompatibleAliasDoesNotMatchUnrelatedVirtualDevice()
    {
        var virtualDevice = Device(@"ROOT\VIRTUAL_ADAPTER", [RootGeneric]);
        var genericOffer = Offer(548, RootGeneric);
        var matches = DeviceWuaMatchGroups
            .Prepare([genericOffer]).ForDevice(virtualDevice);

        Assert.Equal(0, DriverUpdateMatcher.MatchStrength(
            virtualDevice, genericOffer));
        Assert.Empty(matches.Ordinary);
        Assert.Empty(matches.ReviewOnly);
        Assert.Empty(matches.ModelOnlyReview!);
    }

    [Fact]
    public void GenericRootTokenDoesNotMaskSpecificCompatibleHardwareMatch()
    {
        var device = Device(RootGeneric, [Audio]);
        var classOffer = Offer(548, RootGeneric);
        var codecOffer = Offer(549, Audio);
        var prepared = DeviceWuaMatchGroups.Prepare([classOffer, codecOffer]);
        var matched = prepared.ForDevice(device);

        Assert.Equal(codecOffer.Id, Assert.Single(matched.Ordinary).Id);
        Assert.Empty(matched.ReviewOnly);
        Assert.Empty(matched.ModelOnlyReview!);
        Assert.Equal(0, DriverUpdateMatcher.MatchStrength(device, classOffer));
        Assert.Equal(1, DriverUpdateMatcher.MatchStrength(device, codecOffer));

        var audit = WuaPnpAssociationAudit.Create(
            [device], Report(classOffer, codecOffer), true);
        Assert.Equal(0, audit.ExactHardware);
        Assert.Equal(1, audit.CompatibleOnly);
        Assert.Equal(1, audit.Unattributed);
        Assert.Equal(1, audit.OnlyNonCorrelatableWuaIds);
    }

    [Fact]
    public void GenericRootWuaGuidIsUnattributedInAnonymousDiagnostics()
    {
        var device = Device(RootGeneric);
        var report = Report(Offer(548, RootGeneric));
        var audit = WuaPnpAssociationAudit.Create([device], report, true);
        Assert.True(audit.InventoryVerified);
        Assert.False(audit.OverBudget);
        Assert.Equal(1, audit.ConfirmedGuids);
        Assert.Equal(0, audit.ExactHardware);
        Assert.Equal(0, audit.CompatibleOnly);
        Assert.Equal(1, audit.Unattributed);
        Assert.Equal(1, audit.OnlyNonCorrelatableWuaIds);

        var text = ScanCoverageText.Create(report, [], 1, 0, 0,
            [device], inventoryComplete: true);
        Assert.Contains("GUID WUA dichiarano solo ID generici", text);
        Assert.DoesNotContain(RootGeneric, text);
        Assert.DoesNotContain("PRIVATE_", text);
        Assert.DoesNotContain(Offer(548, RootGeneric).Id, text);
    }

    [Theory]
    [InlineData(@"ROOT\VIRTUAL_ADAPTER")]
    [InlineData(@"ROOT\NVVAD")]
    [InlineData(@"ACPI\NVDA0820")]
    [InlineData(@"PCI\VEN_10DE&DEV_28A0")]
    public void SpecificWindowsModelsKeepExactMatches(string id)
    {
        var device = Device(id);
        var offer = Offer(548, id);
        Assert.True(DriverUpdateMatcher.IsCorrelatableHardwareId(id));
        Assert.Equal(2, DriverUpdateMatcher.MatchStrength(device, offer));
        var match = DeviceWuaMatchGroups.Prepare([offer]).ForDevice(device);
        Assert.Equal(offer.Id, Assert.Single(match.Ordinary).Id);
        Assert.Empty(match.ModelOnlyReview!);
    }
}
