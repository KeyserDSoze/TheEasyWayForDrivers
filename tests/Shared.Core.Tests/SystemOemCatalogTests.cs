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
    [Theory]
    [InlineData("Dell", true)]
    [InlineData("Dell Inc.", true)]
    [InlineData(" DELL Technologies ", true)]
    [InlineData("Dell, Inc.", true)]
    [InlineData("NotDell", false)]
    [InlineData("Other Dell", false)]
    [InlineData("DellCorp", false)]
    [InlineData("Dell\tInc.", false)]
    [InlineData("Dell\u202E Inc.", false)]
    [InlineData("Dell\n", false)]
    [InlineData("Dell\u200B", false)]
    [InlineData("Dell\u00A0Inc.", false)]
    public void DellManufacturerRequiresTrustedWordBoundary(
        string manufacturer, bool expected)
    {
        Assert.Equal(expected,
            SystemOemCatalog.IsDellManufacturer(manufacturer));
        Assert.Equal(expected, SystemOemCatalog.Match(
            manufacturer, "Latitude 7440")?.ProviderId == "dell");
    }

    [Theory]
    [InlineData("HP", true)]
    [InlineData("HP Inc.", true)]
    [InlineData(" Hewlett-Packard ", true)]
    [InlineData("Hewlett-Packard Company", true)]
    [InlineData("Hewlett Packard", true)]
    [InlineData("Other Hewlett-PC", false)]
    [InlineData("HPEvil", false)]
    [InlineData("NotHP", false)]
    [InlineData("HP\tInc.", false)]
    [InlineData("HP\u202E Inc.", false)]
    [InlineData("Hewlett Packard Enterprise", false)]
    [InlineData("Hewlett-Packard Enterprise", false)]
    public void HpManufacturerRequiresTrustedWordBoundary(
        string manufacturer, bool expected)
    {
        Assert.Equal(expected,
            SystemOemCatalog.IsHpManufacturer(manufacturer));
        Assert.Equal(expected, SystemOemCatalog.Match(
            manufacturer, "EliteBook 840")?.ProviderId == "hp");
    }

    [Fact]
    public void ManufacturerIdentityRejectsOverlongAndInvisibleMetadata()
    {
        var overlong = "Dell " + new string('A', 170);
        Assert.False(SystemOemCatalog.IsDellManufacturer(overlong));
        Assert.False(SystemOemCatalog.IsHpManufacturer("HP\u2066 Inc."));
    }


    [Theory]
    [InlineData("Lenovo", true)]
    [InlineData("LENOVO", true)]
    [InlineData("Lenovo Group Limited", true)]
    [InlineData(" Lenovo ", true)]
    [InlineData("NotLenovo", false)]
    [InlineData("Other Lenovo", false)]
    [InlineData("LenovoFake", false)]
    [InlineData("Lenovo\tGroup", false)]
    [InlineData("Lenovo\n", false)]
    [InlineData("Lenovo\u202E Group", false)]
    [InlineData("Lenovo\u00A0Group", false)]
    public void LenovoIdentityMatchesActualManufacturerOnly(
        string manufacturer, bool expected)
    {
        Assert.Equal(expected,
            SystemOemCatalog.IsLenovoManufacturer(manufacturer));
        Assert.Equal(expected, SystemOemCatalog.Match(
            manufacturer, "ThinkPad T14")?.ProviderId == "lenovo");
    }

    [Theory]
    [InlineData("Microsoft Corporation", "Surface Laptop 7", true)]
    [InlineData("Microsoft", "Surface Pro 11", true)]
    [InlineData("Microsoft Corporation", "Microsoft Surface Book", true)]
    [InlineData("Microsoft Corporation", "Virtual Machine", false)]
    [InlineData("NotMicrosoft", "Surface Laptop 7", false)]
    [InlineData("Other Microsoft", "Surface Laptop 7", false)]
    [InlineData("MicrosoftFake", "Surface Pro", false)]
    [InlineData("Microsoft Corporation", "NotSurface Laptop", false)]
    [InlineData("Microsoft Corporation", "Other Surface Laptop", false)]
    [InlineData("Microsoft Corporation", "SurfaceFake Laptop", false)]
    [InlineData("Microsoft Corporation", "Surface\n Laptop", false)]
    [InlineData("Microsoft Corporation", "Surface\u202E Laptop", false)]
    [InlineData("Microsoft Corporation", "Surface\u00A0Laptop", false)]
    [InlineData("Microsoft\tCorporation", "Surface Laptop 7", false)]
    public void SurfaceHandoffRequiresBothVerifiedMicrosoftAndModel(
        string manufacturer, string model, bool expected)
    {
        Assert.Equal(expected, SystemOemCatalog.IsMicrosoftSurfacePc(
            manufacturer, model));
        Assert.Equal(expected, SystemOemCatalog.Match(
            manufacturer, model)?.ProviderId == "surface");
    }

    [Theory]
    [InlineData("NotAcer", "Swift")]
    [InlineData("Other Acer", "Swift")]
    [InlineData("AcerFake", "Swift")]
    [InlineData("NotASUSTeK", "ROG")]
    [InlineData("Other ASUS", "ROG")]
    [InlineData("ASUSFake", "ROG")]
    [InlineData("NotMicro-Star", "Raider")]
    [InlineData("Other Micro-Star", "Raider")]
    [InlineData("NotMSI", "Raider")]
    [InlineData("MSIFake", "Raider")]
    [InlineData("NotGigabyte", "AORUS")]
    [InlineData("Other GIGABYTE", "AORUS")]
    [InlineData("GIGABYTEFake", "AORUS")]
    [InlineData("Acer\n", "Swift")]
    [InlineData("GIGABYTE\u200B", "AORUS")]
    [InlineData("ASUSTeK\u00A0 COMPUTER", "ROG")]
    [InlineData("PCSpecialist\n", "Recoil 16")]
    [InlineData("PCSpecialist\u202E", "Recoil 16")]
    public void OtherSupportedOemMatcherRejectsSubstringAndInvisibleSpoofs(
        string manufacturer, string model)
    {
        Assert.Null(SystemOemCatalog.Match(manufacturer, model));
    }

    [Theory]
    [InlineData("PCSpecialist", "Recoil 16", "pcspecialist")]
    [InlineData("PC Specialist Ltd.", "Recoil 16", "pcspecialist")]
    [InlineData("ASUSTeK COMPUTER INC.", "ROG", "asus")]
    [InlineData("ASUS", "ROG", "asus")]
    [InlineData("Acer Incorporated", "Swift", "acer")]
    [InlineData("MSI", "Raider", "msi")]
    [InlineData("Micro-Star International Co., Ltd.", "Raider", "msi")]
    [InlineData("Gigabyte Technology Co., Ltd.", "AORUS", "gigabyte")]
    public void SupportedOemBoundariesKeepRealManufacturers(
        string manufacturer, string model, string provider)
    {
        Assert.Equal(provider, SystemOemCatalog.Match(
            manufacturer, model)?.ProviderId);
    }

}
