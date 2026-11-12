using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

/// <summary>
/// #404: the Desktop must never navigate to a provider URL from IPC
/// without exact, locally reconstructed vendor ownership.
/// These tests inspect the selected URL, not a browser or hardware.
/// </summary>
public sealed class OemProviderOfficialLinkTests
{
    private static OemProviderStatus Provider(
        string id, string url, string? manufacturer = null,
        string? model = null, int detectedCount = 1) =>
        new(id, "IPC DISPLAY NAME", detectedCount, null, null,
            "Companion", url, "IPC DETAIL",
            SystemManufacturer: manufacturer, SystemModel: model);

    [Theory]
    [InlineData("intel",
        "https://www.intel.com/content/www/us/en/support/detect.html")]
    [InlineData("nvidia",
        "https://www.nvidia.com/en-us/geforce/drivers/")]
    [InlineData("amd",
        "https://www.amd.com/en/support/download/drivers.html")]
    public void CanonicalCompanionVendorUrlsCanBeOpened(
        string id, string canonical)
    {
        var p = Provider(id, canonical);
        Assert.True(OemProviderOfficialLink.TryGetOfficialUrl(p, out var url));
        Assert.Equal(canonical, url);
    }

    [Theory]
    [InlineData("PCSpecialist", "Recoil 16", "https://www.pcspecialist.co.uk/download-drivers/")]
    [InlineData("Dell Inc.", "XPS 15", "https://www.dell.com/support/home/")]
    [InlineData("Lenovo", "ThinkPad", "https://support.lenovo.com/")]
    [InlineData("HP", "HP EliteBook", "https://support.hp.com/")]
    [InlineData("ASUS", "ROG Strix", "https://www.asus.com/support/download-center/")]
    [InlineData("Acer", "Predator", "https://www.acer.com/us-en/support/drivers-and-manuals")]
    [InlineData("MSI", "Stealth", "https://www.msi.com/support/download/")]
    [InlineData("GIGABYTE", "AORUS", "https://www.gigabyte.com/Support")]
    [InlineData("Microsoft", "Surface Laptop 7", "https://support.microsoft.com/surface/drivers-firmware/download-drivers-and-firmware-for-surface")]
    public void OfficialSystemOemUrlRequiresMatchingManufacturerAndExactLink(
        string manufacturer, string model, string canonical)
    {
        var p = Provider("system-oem", canonical, manufacturer, model);
        Assert.True(OemProviderOfficialLink.TryGetOfficialUrl(p, out var url));
        Assert.Equal(canonical, url);
    }

    [Theory]
    [InlineData("https://example-attacker.invalid/")]
    [InlineData("https://www.nvidia.com.evil.invalid/en-us/geforce/drivers/")]
    [InlineData("https://www.nvidia.com@evil.invalid/en-us/geforce/drivers/")]
    [InlineData("https://www.nvidia.com:444/en-us/geforce/drivers/")]
    [InlineData("https://www.nvidia.com/en-us/geforce/drivers/?device=PRIVATE")]
    [InlineData("https://www.nvidia.com/en-us/geforce/drivers/#PRIVATE")]
    [InlineData("https://www.nvidia.com/en-us/geforce/drivers/redirect")]
    [InlineData("http://www.nvidia.com/en-us/geforce/drivers/")]
    [InlineData("file:///C:/Windows/System32/cmd.exe")]
    public void HttpsAloneOrLookalikeNvidiaSiteNeverPasses(string url)
    {
        Assert.False(OemProviderOfficialLink.TryGetOfficialUrl(
            Provider("nvidia", url), out var opened));
        Assert.Equal(string.Empty, opened);
    }

    [Fact]
    public void MismatchedProviderIdOrSystemManufacturerCannotOpenDifferentOemSite()
    {
        var nvidia = Provider("intel", OemProviderOfficialLink.NvidiaDriversUrl);
        var fakeSystem = Provider("system-oem",
            SystemOemCatalog.PcSpecialistDriversUrl, "Dell", "XPS");
        var unknownSystem = Provider("system-oem",
            "https://example-attacker.invalid/", "Other Manufacturer", "Model");
        var unknownProvider = Provider("evil-provider",
            OemProviderOfficialLink.NvidiaDriversUrl);
        foreach (var candidate in new[]
            { nvidia, fakeSystem, unknownSystem, unknownProvider })
            Assert.False(OemProviderOfficialLink.TryGetOfficialUrl(
                candidate, out _));
    }

    [Fact]
    public void InvalidAndInapplicableProvidersAreNotSelectedOrOpened()
    {
        var injected = Provider("nvidia",
            "https://example-attacker.invalid/");
        var disabled = Provider("intel", VendorSourceDiscovery.IntelDsaUrl,
            detectedCount: 0);
        Assert.False(OemProviderOfficialLink.TryGetOfficialUrl(
            injected, out _));
        Assert.False(OemProviderOfficialLink.TryGetOfficialUrl(
            disabled, out _));
        Assert.False(OemProviderOfficialLink.TryGetOfficialUrl(null, out _));
        Assert.Null(DriverOemProviderSelector.Choose(
            "NVIDIA", [injected, disabled]));
    }

    [Fact]
    public void DeviceHandoffNeverRecommendsInjectedHttpsOverValidSystemOem()
    {
        var attacker = Provider("nvidia",
            "https://www.nvidia.com@evil.invalid/drivers");
        var officialSystem = Provider("system-oem",
            "https://www.dell.com/support/home/", "Dell", "XPS");
        var chosen = DriverOemProviderSelector.Choose(
            "NVIDIA · Dell OEM", [attacker, officialSystem]);
        Assert.Same(officialSystem, chosen);
        Assert.True(OemProviderOfficialLink.TryGetOfficialUrl(
            chosen, out var url));
        Assert.Equal("https://www.dell.com/support/home/", url);
    }

    [Fact]
    public void UnknownUrlCannotBecomeBrowserNavigationEvenForTrustedProviderName()
    {
        var p = Provider("NVIDIA", "https://www.nvidia.com/en-us/geforce/drivers/?redirect=evil.invalid");
        Assert.False(OemProviderOfficialLink.TryGetOfficialUrl(
            p, out var url));
        Assert.Empty(url);
    }
}
