using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

public sealed class DriverOemProviderSelectorTests
{
    private static OemProviderStatus Provider(
        string id, string url, int count = 1, string? manufacturer = null) =>
        new(id, id, count, null, null, "Official", url, "Test",
            SystemManufacturer: manufacturer ??
                (id == "system-oem" ? "Dell" : null));

    [Fact]
    public void ChoosesChipVendorForGenericDevice()
    {
        var result = DriverOemProviderSelector.Choose("NVIDIA",
        [
            Provider("system-oem", "https://www.dell.com/support/home/"),
            Provider("nvidia", OemProviderOfficialLink.NvidiaDriversUrl)
        ]);
        Assert.Equal("nvidia", result?.ProviderId);
    }

    [Theory]
    [InlineData("NVIDIA · Dell OEM")]
    [InlineData("Dell (OEM PC)")]
    [InlineData("Intel · Lenovo OEM")]
    public void PrioritizesSystemOemForCustomizedDrivers(string source)
    {
        var result = DriverOemProviderSelector.Choose(source,
        [
            Provider("nvidia", OemProviderOfficialLink.NvidiaDriversUrl),
            Provider("intel", VendorSourceDiscovery.IntelDsaUrl),
            Provider("system-oem", "https://www.dell.com/support/home/")
        ]);
        Assert.Equal("system-oem", result?.ProviderId);
    }

    [Fact]
    public void LaptopGpuEvidenceCanPrioritizePcSpecialistOrderSpecificSource()
    {
        var selected = DriverOemProviderSelector.Choose(
            "NVIDIA · PCSpecialist OEM",
            [
                Provider("nvidia", OemProviderOfficialLink.NvidiaDriversUrl),
                Provider("system-oem", SystemOemCatalog.PcSpecialistDriversUrl,
                    manufacturer: "PCSpecialist")
            ]);

        Assert.Equal("system-oem", selected?.ProviderId);
        Assert.Equal(SystemOemCatalog.PcSpecialistDriversUrl,
            selected?.OfficialSupportUrl);
    }

    [Fact]
    public void FallsBackToSystemOem()
    {
        var result = DriverOemProviderSelector.Choose("Windows / OEM",
            [Provider("system-oem", "https://www.dell.com/support/home/")]);
        Assert.Equal("system-oem", result?.ProviderId);
    }

    [Fact]
    public void NeverSelectsInapplicableOrNonHttpsProviders()
    {
        var result = DriverOemProviderSelector.Choose("Intel",
        [
            Provider("intel", "http://example.com", 1),
            Provider("system-oem", "https://www.dell.com/support/home/", 0)
        ]);
        Assert.Null(result);
    }

    [Fact]
    public void FallsBackWhenSystemOemIsNotAvailable()
    {
        var result = DriverOemProviderSelector.Choose("NVIDIA · Dell OEM",
            [Provider("nvidia", OemProviderOfficialLink.NvidiaDriversUrl)]);
        Assert.Equal("nvidia", result?.ProviderId);
    }
}
