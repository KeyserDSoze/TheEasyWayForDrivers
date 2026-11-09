using System.Management;
using System.Runtime.InteropServices;
using System.Security;
using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;
using TheEasyWayForDrivers.ServiceApp.Services;
using Xunit;

namespace TheEasyWayForDrivers.ServiceApp.Tests;

public sealed class WmiPnpIdentifierFieldReaderTests
{
    private const string Nvidia = @"PCI\VEN_10DE&DEV_28A0";
    private const string Killer = @"PCI\VEN_8086&DEV_51F1";
    private const string Realtek = @"HDAUDIO\FUNC_01&VEN_10EC&DEV_0295";

    [Fact]
    public void MixedVariantArrayRetainsSafeGpuWifiAudioIdsAndDeduplicates()
    {
        object?[] raw =
        [
            @"C:\Users\PRIVATE_SERIAL\driver.inf",
            Nvidia, null, 28, Killer, Nvidia.ToLowerInvariant(),
            @"PCI\VEN_10DE&DEV_28A0\PRIVATE_INSTANCE",
            Realtek, new object()
        ];

        var result = WmiPnpIdentifierFieldReader.Read(() => raw);
        Assert.Equal([Nvidia, Killer, Realtek], result);
        Assert.DoesNotContain(result, id => id.Contains("PRIVATE"));
    }

    [Fact]
    public void ScalarValidIdSupportsStrictReadOnlyCorrelation()
    {
        var ids = WmiPnpIdentifierFieldReader.Read(() => Nvidia);
        Assert.Equal([Nvidia], ids);

        var resolved = PnpSetupApiIdentity.Merge(
            ids, [], [Killer], [], Killer, null);
        Assert.Equal([Nvidia], resolved.HardwareIds);
        Assert.False(resolved.UsedSetupApi);
        Assert.False(resolved.UsedSignedDriver);

        var device = new DriverInfo("PRIVATE_PNP_INSTANCE", "Generic display",
            "Microsoft", "Display", "Microsoft", null, null, null,
            false, false, 0, resolved.HardwareIds, resolved.CompatibleIds);
        var wua = new DriverUpdateInfo(
            "00000000-0000-4000-8000-000000000323",
            "Synthetic", null, "Display", "Microsoft", null,
            null, false, "Microsoft", null, Nvidia, null);
        Assert.Equal(2, DriverUpdateMatcher.MatchStrength(device, wua));
        Assert.Equal("NVIDIA",
            VendorSourceDiscovery.GetRecognizedSupportVendor(device));
    }

    [Theory]
    [InlineData("wmi")]
    [InlineData("com")]
    [InlineData("access")]
    [InlineData("security")]
    [InlineData("cast")]
    public void BadGetterIsolatedToOneCategoryAndNextDevice(string kind)
    {
        var hardware = WmiPnpIdentifierFieldReader.Read(() =>
            throw kind switch
            {
                "wmi" => new ManagementException("PRIVATE_SERIAL"),
                "com" => new COMException("PRIVATE_PATH"),
                "access" => new UnauthorizedAccessException("PRIVATE_HOST"),
                "security" => new SecurityException("PRIVATE_USER"),
                _ => new InvalidCastException("PRIVATE_COM_VARIANT")
            });

        var compatible = WmiPnpIdentifierFieldReader.Read(() =>
            new string[] { Killer });
        var merged = PnpSetupApiIdentity.Merge(
            hardware, compatible, [Nvidia], [Realtek], null, null);
        Assert.Equal([Nvidia], merged.HardwareIds);
        Assert.Equal([Killer], merged.CompatibleIds);
        Assert.True(merged.UsedSetupApi);
        Assert.DoesNotContain(Realtek, merged.CompatibleIds);

        var next = WmiPnpIdentifierFieldReader.Read(() =>
            new string[] { Realtek });
        Assert.Equal([Realtek], next);
    }

    [Fact]
    public void ExcessiveOrUnexpectedWmiVariantsNeverClaimCompleteIdentity()
    {
        var tooMany = Enumerable.Repeat(
            Nvidia, PnpSetupApiIdentity.MaximumIds + 1).ToArray();
        Assert.Empty(WmiPnpIdentifierFieldReader.Read(() => tooMany));
        Assert.Empty(WmiPnpIdentifierFieldReader.Read(() => 28));
        Assert.Empty(WmiPnpIdentifierFieldReader.Read(() => null));
        Assert.Empty(WmiPnpIdentifierFieldReader.Read(() =>
            @"PCI\VEN_10DE&DEV_28A0\PRIVATE_INSTANCE"));

        var recovered = PnpSetupApiIdentity.Merge(
            WmiPnpIdentifierFieldReader.Read(() => tooMany),
            [], [Nvidia], [], Killer, null);
        Assert.Equal([Nvidia], recovered.HardwareIds);
        Assert.True(recovered.UsedSetupApi);
        Assert.False(recovered.UsedSignedDriver);
    }

    [Fact]
    public void CancellationAndUnexpectedProgrammingErrorsAreNotHidden()
    {
        Assert.Throws<OperationCanceledException>(() =>
            WmiPnpIdentifierFieldReader.Read(() =>
                throw new OperationCanceledException("cancel")));
        Assert.Throws<InvalidOperationException>(() =>
            WmiPnpIdentifierFieldReader.Read(() =>
                throw new InvalidOperationException("bug")));
    }

    [Fact]
    public void UnusableValuesCannotInventWuaMatchesOrCrossDeviceIds()
    {
        var bad = WmiPnpIdentifierFieldReader.Read(() =>
            new object?[]
            {
                @"C:\PRIVATE\driver.inf",
                @"PCI\VEN_10DE&DEV_28A0\PRIVATE_INSTANCE",
                "VEN_10DE", 0
            });
        var merged = PnpSetupApiIdentity.Merge(
            bad, [], [], [], null, null);
        Assert.Empty(merged.HardwareIds);
        Assert.False(merged.UsedSetupApi);
        Assert.False(merged.UsedSignedDriver);
    }
}
