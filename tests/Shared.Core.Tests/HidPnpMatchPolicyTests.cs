using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;
using TheEasyWayForDrivers.Core.Update;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

public sealed class HidPnpMatchPolicyTests
{
    private static DriverInfo Device(string instance, string id,
        IReadOnlyList<string>? compatible = null) =>
        new(instance, "Generic HID device", "Microsoft", "HIDClass",
            "Microsoft", "1.0", null, "oem42.inf", true, true,
            0, [id], compatible ?? []);

    private static DriverUpdateInfo Offer(string id) =>
        new("00000000-0000-4000-8000-000000000329", "HID driver",
            null, "HIDClass", "Microsoft", null, null, false,
            "Microsoft", null, id, null,
            SearchSource: "Windows Update online");

    private static string Inf(string id) =>
        "[Version]\nSignature=\"$Windows NT$\"\n" +
        "[Manufacturer]\n%Maker%=Models,NTamd64\n" +
        "[Models.NTamd64]\n%Device%=Install," + id;

    [Theory]
    [InlineData(@"HID\HID_DEVICE")]
    [InlineData(@"HID\HID_DEVICE_SYSTEM_KEYBOARD")]
    [InlineData(@"HID\HID_DEVICE_SYSTEM_MOUSE")]
    [InlineData(@"HID\HID_DEVICE_UP_0001")]
    [InlineData(@"HID\VID_046D")]
    [InlineData(@"HID\VID_046D&PID_")]
    [InlineData(@"HID\VID_046D&PID_C53")]
    [InlineData(@"HID\VID_046D&PID_C534&FAKE_01")]
    [InlineData(@"HID\VID_046D&PID_C534&COL0G")]
    [InlineData(@"HID\VID_046D&PID_C534\PRIVATE_INSTANCE")]
    [InlineData(@"HID\VID_046D&PID_C534&REV_000G")]
    [InlineData(@"HID\VID_046D&PID_C534&MI_000")]
    public void GenericOrMalformedHidMustNotCreateExactDeviceCorrelation(
        string generic)
    {
        Assert.False(HidPnpMatchPolicy.IsSpecificHardwareId(generic));
        Assert.False(DriverUpdateMatcher.IsCorrelatableHardwareId(generic));
        var device = Device("PRIVATE_INSTANCE", generic);
        var update = Offer(generic);
        Assert.Equal(0, DriverUpdateMatcher.MatchStrength(device, update));
        var matches = DeviceWuaMatchGroups.Create(device, [update]);
        Assert.Empty(matches.Ordinary);
        Assert.Empty(matches.ReviewOnly);
        Assert.Empty(matches.ModelOnlyReview!);
        Assert.Empty(QuickUpdatePolicy.AllOrdinaryIds(matches.Ordinary));

        // INF can list such a generic ID as metadata, but it cannot prove
        // that one particular HID model on this PC matches that package.
        var inf = Inf(generic);
        var declared = InfModelEvidence.ReadDeclaredIds(inf);
        if (!generic.Contains("PRIVATE", StringComparison.Ordinal) &&
            !generic.Contains("FAKE", StringComparison.Ordinal))
            Assert.NotEmpty(declared);
        Assert.Empty(InfModelEvidence.ReadMatchingIds(inf, [device]));
        Assert.Equal(0, InfModelEvidence.MatchStrength(device, declared));
    }

    [Theory]
    [InlineData(@"HID\VID_046D&PID_C534")]
    [InlineData(@"HID\VID_046D&PID_C534&REV_0100")]
    [InlineData(@"HID\VID_046D&PID_C534&MI_00")]
    [InlineData(@"HID\VID_046D&PID_C534&MI_00&COL01")]
    [InlineData(@"HID\VID_046D&PID_C534&MI_00&COL01&IG_00")]
    [InlineData(@"HID\VID_046D&PID_C534&COL01")]
    [InlineData(@"HID\MSFT0001")]
    public void SpecificHidKeepsExactAndCompatibleMatching(
        string id)
    {
        Assert.True(HidPnpMatchPolicy.IsSpecificHardwareId(id));
        Assert.True(DriverUpdateMatcher.IsCorrelatableHardwareId(id));
        var offer = Offer(id);
        Assert.Equal(2, DriverUpdateMatcher.MatchStrength(
            Device("PRIVATE_MOUSE", id), offer));
        Assert.Equal(1, DriverUpdateMatcher.MatchStrength(
            Device("PRIVATE_MOUSE", @"ACPI\PNP0C09", [id]), offer));

        var inf = Inf(id);
        Assert.Equal(2, InfModelEvidence.MatchStrength(
            Device("PRIVATE_MOUSE", id),
            InfModelEvidence.ReadMatchingIds(inf, [Device("PRIVATE_MOUSE", id)])));
    }

    [Fact]
    public void GenericHidWithOneCompletedWuaGuidIsUnattributedInAnonymousAudit()
    {
        const string generic = @"HID\HID_DEVICE_SYSTEM_KEYBOARD";
        var device = Device("PRIVATE_DEVICE_SERIAL", generic);
        var search = new DriverSearchReport([Offer(generic)],
        [
            new DriverSearchPassStatus("Windows Update online",
                "Completed", 1, "PRIVATE_PATH")
        ]);
        var result = WuaPnpAssociationAudit.Create([device], search, true);
        Assert.Equal(1, result.ConfirmedGuids);
        Assert.Equal(0, result.ExactHardware);
        Assert.Equal(0, result.CompatibleOnly);
        Assert.Equal(0, result.GenericModelOnly);
        Assert.Equal(1, result.Unattributed);
        Assert.False(result.IsInstallable);
        Assert.DoesNotContain("PRIVATE", result.Text);
        Assert.DoesNotContain("HID_DEVICE", result.Text);
        Assert.DoesNotContain("00000000-0000-4000", result.Text);
    }

    [Fact]
    public void GenericHidCompatibleIdCannotOutrankSpecificDeviceModel()
    {
        const string generic = @"HID\HID_DEVICE_SYSTEM_MOUSE";
        const string specific = @"HID\VID_046D&PID_C534&MI_00&COL01";
        var device = Device("PRIVATE_MOUSE",
            specific, [generic]);
        var genericOffer = Offer(generic);
        var specificOffer = Offer(specific) with
        { Id = "00000000-0000-4000-8000-000000000330" };
        var found = DriverUpdateMatcher.FindMatches(device,
            [genericOffer, specificOffer]);
        Assert.Equal(specificOffer.Id, Assert.Single(found).Id);
    }
}
