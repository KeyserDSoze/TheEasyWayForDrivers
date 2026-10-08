using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

public sealed class DriverOemProviderSelectorTests
{
    private static OemProviderStatus Provider(string id, string url, int count = 1) =>
        new(id, id, count, null, null, "Official", url, "Test");

    [Fact]
    public void ChoosesChipVendorForGenericDevice()
    {
        var result = DriverOemProviderSelector.Choose("NVIDIA",
        [
            Provider("system-oem", "https://www.dell.com/support"),
            Provider("nvidia", "https://www.nvidia.com/support")
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
            Provider("nvidia", "https://www.nvidia.com/support"),
            Provider("intel", "https://www.intel.com/support"),
            Provider("system-oem", "https://www.dell.com/support")
        ]);
        Assert.Equal("system-oem", result?.ProviderId);
    }

    [Fact]
    public void FallsBackToSystemOem()
    {
        var result = DriverOemProviderSelector.Choose("Windows / OEM",
            [Provider("system-oem", "https://www.dell.com/support")]);
        Assert.Equal("system-oem", result?.ProviderId);
    }

    [Fact]
    public void NeverSelectsInapplicableOrNonHttpsProviders()
    {
        var result = DriverOemProviderSelector.Choose("Intel",
        [
            Provider("intel", "http://example.com", 1),
            Provider("system-oem", "https://www.dell.com/support", 0)
        ]);
        Assert.Null(result);
    }

    [Fact]
    public void FallsBackWhenSystemOemIsNotAvailable()
    {
        var result = DriverOemProviderSelector.Choose("NVIDIA · Dell OEM",
            [Provider("nvidia", "https://www.nvidia.com/support")]);
        Assert.Equal("nvidia", result?.ProviderId);
    }
}
