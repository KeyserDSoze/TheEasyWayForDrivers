using TheEasyWayForDrivers.Core.Drivers;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

public sealed class SystemOemCatalogTests
{
    [Theory]
    [InlineData("Dell Inc.", "XPS 15", "Dell")]
    [InlineData("LENOVO", "ThinkPad T14", "Lenovo")]
    [InlineData("HP", "EliteBook 840", "HP")]
    [InlineData("Hewlett-Packard", "ProBook", "HP")]
    [InlineData("ASUSTeK COMPUTER INC.", "ROG Zephyrus", "ASUS")]
    [InlineData("Acer", "Swift", "Acer")]
    [InlineData("Micro-Star International Co., Ltd.", "MS-7D91", "MSI")]
    [InlineData("MSI", "Raider GE78", "MSI")]
    [InlineData("GIGABYTE TECHNOLOGY CO., LTD.", "AORUS 17", "GIGABYTE")]
    [InlineData("Microsoft Corporation", "Surface Laptop 7", "Microsoft Surface")]
    public void Match_RecognizesSupportedSystemOem(
        string manufacturer,
        string model,
        string expected)
    {
        var result =
            SystemOemCatalog.Match(
                manufacturer,
                model);

        Assert.Equal(
            expected,
            result?.DisplayName);
    }

    [Fact]
    public void Match_DoesNotTreatGenericMicrosoftPcAsSurface()
    {
        Assert.Null(
            SystemOemCatalog.Match(
                "Microsoft Corporation",
                "Virtual Machine"));
    }

    [Fact]
    public void Match_ReturnsNullForUnknownManufacturer()
    {
        Assert.Null(
            SystemOemCatalog.Match(
                "Custom Builder",
                "Desktop"));
    }
}
