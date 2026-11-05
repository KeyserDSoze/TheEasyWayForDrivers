using TheEasyWayForDrivers.Core.Drivers;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

public sealed class SystemOemCatalogTests
{
    [Theory]
    [InlineData("PCSpecialist", "Recoil 16", "PCSpecialist")]
    [InlineData("PC Specialist Ltd.", "Recoil 16", "PCSpecialist")]
    [InlineData("PCSPECIALIST LTD", "Recoil 16", "PCSpecialist")]
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
    public void PcSpecialistUsesOnlyOfficialPerOrderDriverPortal()
    {
        var result = SystemOemCatalog.Match("PCSpecialist", "Recoil 16");
        Assert.NotNull(result);
        Assert.Equal("pcspecialist", result.ProviderId);
        Assert.Equal("https://www.pcspecialist.co.uk/download-drivers/",
            result.OfficialSupportUrl);
        Assert.False(result.CompanionDetectionSupported);
    }

    [Theory]
    [InlineData("Other PCSpecialist")]
    [InlineData("NotPCSpecialist")]
    [InlineData("PCSpecialistFake")]
    [InlineData("Other PC Specialist")]
    [InlineData("Personal Computer Specialist")]
    public void PcSpecialistMatcherDoesNotAcceptSubstringSpoofing(string manufacturer)
    {
        Assert.False(SystemOemCatalog.IsPcSpecialistManufacturer(manufacturer));
        Assert.Null(SystemOemCatalog.Match(manufacturer, "Recoil 16"));
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
