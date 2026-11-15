using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

/// <summary>
/// #461: the public INF evidence matcher must apply the same PnP identity
/// validation to local Windows IDs as the WUA matcher and targeted INF
/// scanner. A malformed local ID is NOT an OEM driver-device match.
/// </summary>
public sealed class InfDirectMatchLocalIdentityBoundaryTests
{
    private const string Nvidia = @"PCI\VEN_10DE&DEV_28A0";
    private const string NvidiaOem =
        @"PCI\VEN_10DE&DEV_28A0&SUBSYS_16A217AA";
    private const string Killer =
        @"PCI\VEN_8086&DEV_51F1&SUBSYS_16721A56";
    private const string Realtek =
        @"HDAUDIO\FUNC_01&VEN_10EC&DEV_0295";
    private const string Bluetooth = @"USB\VID_8087&PID_0033";
    private const string Logitech = @"HID\VID_046D&PID_C534";

    private static DriverInfo Device(
        IReadOnlyList<string> hardware,
        IReadOnlyList<string>? compatible = null) =>
        new("PRIVATE_WINDOWS_INSTANCE", "Microsoft Generic Device",
            "Microsoft", "System", "Microsoft", "1.0", null,
            "oem-private.inf", true, true, 0,
            hardware, compatible ?? []);

    private static string Inf(string primary, string? alias = null) =>
        "[Version]\nSignature=\"$Windows NT$\"\n" +
        "[Manufacturer]\nVendor=Models,NTamd64\n" +
        "[Models.NTamd64]\nModel=Install," + primary +
        (alias is null ? "" : "," + alias) + "\n";

    private static DriverUpdateInfo Wua(string hardware) =>
        new("00000000-0000-4000-8000-000000000461",
            "Windows driver", null, "System", "Microsoft",
            null, null, false, "Microsoft", null, hardware, null,
            SearchSource: "Windows Update online");

    [Theory]
    [InlineData(Nvidia)]
    [InlineData(Killer)]
    [InlineData(Realtek)]
    [InlineData(Bluetooth)]
    [InlineData(Logitech)]
    public void RealPrimaryHardwareModelRemainsExactInInfAndWua(string model)
    {
        var device = Device([model]);
        var declared = InfModelEvidence.ReadDeclaredIds(Inf(model));
        Assert.Equal(2, InfModelEvidence.MatchStrength(device, declared));
        Assert.Equal(2, DriverUpdateMatcher.MatchStrength(device, Wua(model)));
        Assert.Equal(2, InfModelEvidence.MatchStrength(device,
            [new InfModelEvidence.DeclaredId("Models", model)]));
        Assert.Equal(model.ToUpperInvariant(),
            Assert.Single(InfModelEvidence.ReadMatchingIds(
                Inf(model), [device])).HardwareId);
    }

    [Theory]
    [InlineData("\t")]
    [InlineData("\n")]
    [InlineData("\r")]
    [InlineData("\u00A0")]
    [InlineData("\u200B")]
    [InlineData("\u202E")]
    [InlineData("  \t")]
    public void InvalidTailCannotBeNormalizedToExactInfMatch(string suffix)
    {
        var bad = Nvidia + suffix;
        var device = Device([bad], [bad]);
        var declared = InfModelEvidence.ReadDeclaredIds(Inf(Nvidia));

        Assert.False(DriverUpdateMatcher.IsCorrelatableHardwareId(bad));
        Assert.Equal(0, DriverUpdateMatcher.MatchStrength(device, Wua(Nvidia)));
        Assert.Equal(0, InfModelEvidence.MatchStrength(device, declared));
        Assert.Empty(InfModelEvidence.ReadMatchingIds(Inf(Nvidia), [device]));
    }

    [Theory]
    [InlineData("\t")]
    [InlineData("\n")]
    [InlineData("\r")]
    [InlineData("\u00A0")]
    [InlineData("\u202E")]
    public void InvalidPrefixCannotBecomeHardwareOrCompatibleMatch(
        string prefix)
    {
        var bad = prefix + Realtek;
        var declared = InfModelEvidence.ReadDeclaredIds(Inf(Realtek));
        Assert.False(DriverUpdateMatcher.IsCorrelatableHardwareId(bad));
        Assert.Equal(0, InfModelEvidence.MatchStrength(
            Device([bad]), declared));
        Assert.Equal(0, InfModelEvidence.MatchStrength(
            Device([], [bad]), declared));
        Assert.Equal(0, DriverUpdateMatcher.MatchStrength(
            Device([bad]), Wua(Realtek)));
    }

    [Theory]
    [InlineData(@"PCI\VEN_10DE&DEV_28A0\PRIVATE_INSTANCE")]
    [InlineData(@"PCI\VEN_10DE&DEV_28A0&SERIAL_PRIVATE")]
    [InlineData(@"PCI\VEN_10DE&DEV_28A0&FAKE_01")]
    [InlineData(@"PCI\VEN_10DE")]
    [InlineData(@"PCI\CC_030000")]
    public void InvalidOrGenericLocalHardwareCannotCreateInfEvidence(
        string bad)
    {
        Assert.False(DriverUpdateMatcher.IsCorrelatableHardwareId(bad));
        var local = Device([bad], [bad]);
        var declaration = new InfModelEvidence.DeclaredId(
            "Models.NTamd64", bad);
        Assert.Equal(0, InfModelEvidence.MatchStrength(local, [declaration]));
        Assert.Equal(0, InfModelEvidence.MatchStrength(local,
            InfModelEvidence.ReadDeclaredIds(Inf(Nvidia))));
    }

    [Theory]
    [InlineData("\t")]
    [InlineData("\n")]
    [InlineData("\u00A0")]
    [InlineData("\u202E")]
    public void DirectMalformedInfDeclarationCannotMatchGenuinePnp(
        string suffix)
    {
        var inf = new InfModelEvidence.DeclaredId(
            "Models", Nvidia + suffix);
        var local = Device([Nvidia], [Nvidia]);
        Assert.False(DriverUpdateMatcher.IsCorrelatableHardwareId(
            inf.HardwareId));
        Assert.Equal(0, InfModelEvidence.MatchStrength(local, [inf]));
    }

    [Fact]
    public void AsciiSpacePaddingOnBothSidesRemainsValidAndConsistent()
    {
        var local = Device(["  " + Nvidia.ToLowerInvariant() + "  "],
            ["  " + Killer.ToLowerInvariant() + "  "]);
        InfModelEvidence.DeclaredId[] declared = [
            new InfModelEvidence.DeclaredId(
                "Models.NTamd64", " " + Nvidia + " "),
            new InfModelEvidence.DeclaredId(
                "Models.NTamd64", "  " + Killer + "  ", false)
        ];
        Assert.True(DriverUpdateMatcher.IsCorrelatableHardwareId(
            declared[0].HardwareId));
        Assert.Equal(2, InfModelEvidence.MatchStrength(local, declared));
        Assert.Equal(2, DriverUpdateMatcher.MatchStrength(
            local, Wua(Nvidia)));
        Assert.Equal(1, InfModelEvidence.MatchStrength(
            Device([], [" " + Killer + " "]), [declared[1]]));
    }

    [Fact]
    public void InfSecondaryIdNeverUpgradesEvenIfLocalHardwareIdIsExact()
    {
        var declared = InfModelEvidence.ReadDeclaredIds(Inf(NvidiaOem, Nvidia));
        Assert.Equal(2, declared.Count);
        Assert.True(declared[0].IsPrimaryHardwareId);
        Assert.False(declared[1].IsPrimaryHardwareId);
        Assert.Equal(1, InfModelEvidence.MatchStrength(
            Device([Nvidia]), declared));
        Assert.Equal(1, InfModelEvidence.MatchStrength(
            Device([], [Nvidia]), declared));
        Assert.Equal(2, InfModelEvidence.MatchStrength(
            Device([NvidiaOem]), declared));
    }

    [Fact]
    public void MixedMalformedPrimaryAndValidCompatibleCannotRaiseToExact()
    {
        var device = Device([Realtek + "\t"], [Realtek]);
        var declared = InfModelEvidence.ReadDeclaredIds(Inf(Realtek));
        Assert.Equal(1, InfModelEvidence.MatchStrength(device, declared));
        Assert.Equal(1, DriverUpdateMatcher.MatchStrength(
            device, Wua(Realtek)));
    }

    [Fact]
    public void InvalidLocalDeviceDoesNotBecomeAnOemUpdate()
    {
        var local = Device([Killer + "\n"]);
        var ids = InfModelEvidence.ReadDeclaredIds(Inf(Killer));
        Assert.Equal(0, InfModelEvidence.MatchStrength(local, ids));
        Assert.Empty(InfModelEvidence.ReadMatchingIds(Inf(Killer), [local]));
        Assert.Equal(0, DriverUpdateMatcher.MatchStrength(local, Wua(Killer)));
    }
}
