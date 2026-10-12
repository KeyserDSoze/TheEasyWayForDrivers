using TheEasyWayForDrivers.Core.Drivers;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

public sealed class OemCatalogRegistryTests
{
    [Fact]
    public void CatalogCapabilities_DoNotClaimThatSupportPortalsAreApis()
    {
        Assert.Equal(OemCatalogAccess.PublishedCatalog,
            OemCatalogRegistry.Find("Dell")?.Access);
        Assert.Equal(OemCatalogAccess.DocumentedLocalTool,
            OemCatalogRegistry.Find(" lenovo ")?.Access);
        Assert.Equal(OemCatalogAccess.DocumentedLocalTool,
            OemCatalogRegistry.Find("HP")?.Access);
        foreach (var vendor in new[] { "ASUS", "Acer", "MSI", "GIGABYTE",
                 "Intel", "NVIDIA", "AMD", "Samsung", "Framework", "Razer", "Logitech" })
            Assert.Equal(OemCatalogAccess.OfficialSupportOnly,
                OemCatalogRegistry.Find(vendor)?.Access);
    }

    [Fact]
    public void EverySourceHasOfficialHttpsDocumentation()
    {
        Assert.Equal(15, OemCatalogRegistry.Sources.Count);
        foreach (var item in OemCatalogRegistry.Sources)
        {
            Assert.Equal("https", new Uri(item.OfficialDocumentation).Scheme);
            Assert.False(string.IsNullOrWhiteSpace(item.Limitations));
        }
    }

    [Fact]
    public void UnknownVendorIsNotGuessed() =>
        Assert.Null(OemCatalogRegistry.Find("Unrecognized Manufacturer"));
}
