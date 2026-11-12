using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

/// <summary>
/// #406: OEM preview must not echo a nonempty but unsafe WMI Name,
/// such as a private PnP instance, local path or network URL.
/// </summary>
public sealed class VendorHandoffUntrustedNameTests
{
    private const string PrivateInstance =
        @"PCI\VEN_10DE&DEV_28A0\PRIVATE_GPU_SERIAL_42";

    private static DriverInfo Device(
        string? name, string hardware, string? deviceClass = "Display",
        string provider = "Microsoft", string? instanceId = null) =>
        new(instanceId ?? PrivateInstance, name!, provider, deviceClass,
            provider, "31.0.15.6094", null, null,
            true, true, 0, [hardware], []);

    [Theory]
    [InlineData(@"PCI\VEN_10DE&DEV_28A0\PRIVATE_GPU_SERIAL_42")]
    [InlineData(@"PCI\VEN_10DE&DEV_28A0")]
    [InlineData(@"C:\Users\PRIVATE_USER\driver.inf")]
    [InlineData(@"\\PRIVATE-WSUS\share\driver.inf")]
    [InlineData("https://private-intranet.example/drivers?serial=PRIVATE")]
    [InlineData("NVIDIA GPU VEN_10DE&DEV_28A0")]
    [InlineData("GPU SUBSYS_16A217AA")]
    [InlineData("NVIDIA GPU \u202E cmd.exe")]
    [InlineData("NVIDIA Controller\r\nPRIVATE_SERIAL")]
    public void StrictNvidiaPnpStillGetsAnonymousHandoffWhenNameIsUnsafe(
        string rawName)
    {
        var driver = Device(rawName, @"PCI\VEN_10DE&DEV_28A0");
        var row = Assert.Single(VendorSourceDiscovery.Find([driver], []));

        Assert.Equal("NVIDIA", row.Vendor);
        Assert.Equal(VendorSourceDiscovery.NvidiaDriversUrl, row.OfficialUrl);
        Assert.Equal("Scheda video (nome non disponibile)", row.DeviceName);
        Assert.Equal("Scheda video (nome non disponibile)", row.Title);
        Assert.DoesNotContain("PRIVATE", row.DeviceName + row.Detail);
        Assert.DoesNotContain("VEN_", row.DeviceName);
        Assert.DoesNotContain("cmd.exe", row.DeviceName);
        Assert.False(row.IsInstallable);
        Assert.False(row.IsVendorApplicable);
        Assert.Equal("NVIDIA",
            VendorSourceDiscovery.GetRecognizedSupportVendor(driver));
    }

    [Theory]
    [InlineData(@"PCI\VEN_10DE&DEV_28A0\PRIVATE_GPU_SERIAL_42")]
    [InlineData(@"\\PRIVATE-WSUS\share\driver.inf")]
    [InlineData("https://private-intranet.example/device")]
    [InlineData("NVIDIA VEN_10DE controller")]
    [InlineData("NVIDIA GPU \u202E cmd.exe")]
    public void UntrustedNameAndStaleProviderCannotInventOemSourceWithoutModel(
        string rawName)
    {
        var driver = Device(rawName, @"ROOT\UNKNOWN", provider: "NVIDIA");
        Assert.Empty(VendorSourceDiscovery.Find([driver], []));
        Assert.Null(VendorSourceDiscovery.GetRecognizedSupportVendor(driver));
    }

    [Theory]
    [InlineData(@"HDAUDIO\FUNC_01&VEN_10EC&DEV_0295",
        "MEDIA", "Realtek", "Dispositivo audio (nome non disponibile)")]
    [InlineData(@"PCI\VEN_8086&DEV_51F1&SUBSYS_16721A56",
        "Net", "Killer / Intel", "Scheda di rete (nome non disponibile)")]
    public void OtherValidVendorsAlsoUseSafeClassLabelWhenWmiNameIsPrivate(
        string model, string cls, string vendor, string expectedTitle)
    {
        var driver = Device(@"C:\Users\PRIVATE\driver.inf", model, cls);
        var row = Assert.Single(VendorSourceDiscovery.Find([driver], []));
        Assert.Equal(vendor, row.Vendor);
        Assert.Equal(expectedTitle, row.Title);
        Assert.DoesNotContain("PRIVATE", row.Title);
    }

    [Fact]
    public void ValidatedIntelPrimaryOutranksUnsafeNvidiaBrandText()
    {
        var driver = Device(@"PCI\VEN_10DE&DEV_28A0\PRIVATE_GPU_SERIAL_42",
            @"PCI\VEN_8086&DEV_46A6", provider: "NVIDIA")
            with { CompatibleIds = [@"PCI\VEN_10DE&DEV_28A0"] };
        var row = Assert.Single(VendorSourceDiscovery.Find([driver], []));
        Assert.Equal("Intel", row.Vendor);
        Assert.Equal(VendorSourceDiscovery.IntelDsaUrl, row.OfficialUrl);
        Assert.Equal("Scheda video (nome non disponibile)", row.Title);
        Assert.Equal("Intel", VendorSourceDiscovery.GetRecognizedSupportVendor(driver));
    }

    [Fact]
    public void WuaMatchingUsesOriginalHardwareIdAndNotSanitizedDisplayName()
    {
        const string id = @"PCI\VEN_10DE&DEV_28A0";
        var driver = Device(@"C:\PRIVATE\GPUSerial", id);
        var update = new DriverUpdateInfo(
            "00000000-0000-4000-8000-000000000406",
            "Windows driver", null, "Display", "Microsoft",
            null, null, false, "Microsoft", null, id, null);
        var matching = Assert.Single(VendorSourceDiscovery.Find([driver], [update]));
        var empty = Assert.Single(VendorSourceDiscovery.Find([driver], []));
        Assert.Equal(1, matching.WuaMatches);
        Assert.Equal(0, empty.WuaMatches);
        Assert.Equal("Scheda video (nome non disponibile)", matching.Title);
        Assert.False(matching.IsInstallable);
        Assert.False(empty.IsInstallable);
    }

    [Fact]
    public void DuplicateContradictoryVendorModelsAreStillRejected()
    {
        var nvidia = Device(@"C:\PRIVATE\name",
            @"PCI\VEN_10DE&DEV_28A0");
        var intel = Device(@"C:\PRIVATE\name",
            @"PCI\VEN_8086&DEV_46A6");
        Assert.Empty(VendorSourceDiscovery.Find([nvidia, intel], []));
    }

    [Theory]
    [InlineData("NVIDIA GeForce RTX 4060 Laptop GPU", @"PCI\VEN_10DE&DEV_28A0", "NVIDIA")]
    [InlineData("Intel(R) Killer Wi-Fi AX1675i", @"PCI\VEN_8086&DEV_51F1&SUBSYS_16721A56", "Killer / Intel")]
    public void SafeFriendlyNamesRemainUnchanged(
        string name, string model, string vendor)
    {
        var driver = Device(name, model, "Net");
        var row = Assert.Single(VendorSourceDiscovery.Find([driver], []));
        Assert.Equal(name, row.DeviceName);
        Assert.Equal(vendor, row.Vendor);
    }

    [Fact]
    public void UntrustedOneCharacterNameNeedsStrictVendorModel()
    {
        var valid = Device("X", @"PCI\VEN_10DE&DEV_28A0");
        var invalid = Device("X", @"PCI\VEN_9999&DEV_1234");
        Assert.Equal("Scheda video (nome non disponibile)",
            Assert.Single(VendorSourceDiscovery.Find([valid], [])).DeviceName);
        Assert.Empty(VendorSourceDiscovery.Find([invalid], []));
    }
}
