using TheEasyWayForDrivers.Core.Drivers;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

public sealed class SurfaceSupportCatalogTests
{
    [Theory]
    [InlineData("Surface Pro 8", 103503)]
    [InlineData("Surface Pro 9 with 5G", 105941)]
    [InlineData("Surface Laptop Studio 2", 105610)]
    public void KnownModels_MapToOfficialMicrosoftDownloadCenter(string model, int id) =>
        Assert.Equal($"https://www.microsoft.com/download/details.aspx?id={id}",
            SurfaceSupportCatalog.Resolve(model));

    [Theory]
    [InlineData("Surface Pro 12")]
    [InlineData("Surface Pro 9 custom")]
    [InlineData("Surface Laptop")]
    [InlineData("Surface Pro 9 with 5G experimental")]
    [InlineData("")]
    public void UnknownOrPartialModel_AlwaysFallsBackToOfficialSupport(string model)
    {
        Assert.False(SurfaceSupportCatalog.HasExactMatch(model));
        Assert.Equal(SurfaceSupportCatalog.OfficialSupportPage,
            SurfaceSupportCatalog.Resolve(model));
    }

    [Fact]
    public void MissingModelFallsBackToOfficialSupport() =>
        Assert.Equal(SurfaceSupportCatalog.OfficialSupportPage,
            SurfaceSupportCatalog.Resolve(null));
}
